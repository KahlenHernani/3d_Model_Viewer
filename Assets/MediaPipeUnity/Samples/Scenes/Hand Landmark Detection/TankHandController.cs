using UnityEngine;
using UnityEngine.InputSystem;

public class TankHandController : MonoBehaviour
{
    public DragRotateModel rotateModel;
    public CameraZoomController zoomController;
    public ExplodeView explodeView;
    public GroupCameraController groupCameraController;

    [Header("Distance Controls")]
    [SerializeField] private float distanceSensitivity = 60f;
    [SerializeField] private float smoothingSpeed = 15f;
    [SerializeField] private float gestureHoldThreshold = 0.25f;
    [SerializeField] private float fistSwitchHoldThreshold = 0.16f;
    [SerializeField] private float modeSwitchHoldThreshold = 0.3f;
    [SerializeField] private float gestureCooldown = 0.45f;
    [SerializeField] private float gestureDropGrace = 0.25f;
    [SerializeField] private float groupEntryZoom = 0.15f;
    [SerializeField] private float minimumGroupEntryZoom = 0.25f;
    [Header("Group Navigation")]
    [SerializeField] private float groupNavigationHoldThreshold = 0.18f;
    [SerializeField] private bool invertGroupNavigationDirection = false;
    private float currentDistanceValue = 0.5f;
    private float targetDistanceValue = 0.5f;
    private float currentExplodeAmount = 0f;
    private float targetExplodeAmount = 0f;

    public enum InteractionMode
    {
        Zoom,
        Explode,
        Group
    }

    [Header("Current Mode")]
    public InteractionMode currentMode = InteractionMode.Zoom;

    private Vector2 previousWrist;
    private bool hasPreviousWrist = false;

    private volatile bool fistDetected;
    private volatile bool thumbsUpDetected;
    private volatile bool openHandDetected;
    private float fistHoldTime = 0f;
    private float fistMissingTime = 0f;
    private bool fistLatched = false;
    private float thumbsUpHoldTime = 0f;
    private float thumbsUpMissingTime = 0f;
    private bool thumbsUpLatched = false;
    private float openHandHoldTime = 0f;
    private float openHandMissingTime = 0f;
    private bool openHandLatched = false;
    private float gestureCooldownRemaining = 0f;
    private volatile int groupNavigationDirection = 0;
    private int pendingGroupNavigationDirection = 0;
    private float groupNavigationHoldTime = 0f;
    private bool groupNavigationLatched = false;
    private ExplodeView.GroupViewState groupViewState = ExplodeView.GroupViewState.OrbitOverview;
    private bool explodeZoomLocked = false;
    private readonly object groupOrbitInputLock = new object();
    private Vector2 pendingGroupOrbitInput = Vector2.zero;

    private void Start()
    {
        EnsureGroupCameraConfigured();
        zoomController?.SetGroupFieldOfView(false);
    }

    private void Update()
    {
        currentDistanceValue = Mathf.Lerp(
            currentDistanceValue,
            targetDistanceValue,
            smoothingSpeed * Time.deltaTime
        );

        currentExplodeAmount = Mathf.Lerp(
            currentExplodeAmount,
            targetExplodeAmount,
            smoothingSpeed * Time.deltaTime
        );

        if (gestureCooldownRemaining > 0f)
        {
            gestureCooldownRemaining -= Time.deltaTime;
        }

        if (currentMode == InteractionMode.Zoom)
        {
            zoomController.SetZoom(currentDistanceValue);
            explodeView.SetExplodeAmount(0f, currentMode);
        }
        else if (currentMode == InteractionMode.Explode)
        {
            zoomController.SetZoom(currentDistanceValue);
            explodeView.SetExplodeAmount(currentExplodeAmount, currentMode);
        }
        else if (currentMode == InteractionMode.Group)
        {
            explodeView.SetExplodeAmount(1f, currentMode);

            if (groupViewState == ExplodeView.GroupViewState.FocusedGroup && groupCameraController != null)
            {
                groupCameraController.SetFocusZoom(currentDistanceValue);
            }
        }

        UpdateFistMode();
        UpdateThumbsUpMode();
        UpdateOpenHandExplodeLock();
        UpdateGroupNavigation();
        ApplyPendingGroupOrbitInput();
    }

    public void UpdateRotation(Vector2 wrist, bool isPinching)
    {
        if (!isPinching)
        {
            hasPreviousWrist = false;
            return;
        }

        if (hasPreviousWrist)
        {
            Vector2 delta = wrist - previousWrist;
            if (currentMode == InteractionMode.Group &&
                groupViewState == ExplodeView.GroupViewState.FocusedGroup)
            {
                QueueGroupOrbitInput(delta);
            }
            else
            {
                rotateModel.SetRotationInput(delta * 15f);
            }
        }

        previousWrist = wrist;
        hasPreviousWrist = true;
    }

    public void UpdateDistanceDelta(float delta)
    {
        if (currentMode == InteractionMode.Explode && !explodeZoomLocked)
        {
            targetExplodeAmount += delta * distanceSensitivity;
            targetExplodeAmount = Mathf.Clamp01(targetExplodeAmount);
            return;
        }

        targetDistanceValue += delta * distanceSensitivity;

        targetDistanceValue = Mathf.Clamp01(
            targetDistanceValue
        );
    }

    public void SetFistDetected(bool detected)
    {
        fistDetected = detected;
    }

    public void SetThumbsUpDetected(bool detected)
    {
        thumbsUpDetected = detected;
    }

    public void SetOpenHandDetected(bool detected)
    {
        openHandDetected = detected;
    }

    public void SetGroupNavigationDirection(int direction)
    {
        groupNavigationDirection = Mathf.Clamp(direction, -1, 1);
    }

    private void UpdateFistMode()
    {
        if (gestureCooldownRemaining > 0f)
        {
            return;
        }

        if (!fistDetected)
        {
            fistMissingTime += Time.deltaTime;
            if (fistMissingTime > gestureDropGrace)
            {
                fistHoldTime = 0f;
                fistLatched = false;
            }
            return;
        }

        fistMissingTime = 0f;
        fistHoldTime += Time.deltaTime;
        if (fistLatched || fistHoldTime < fistSwitchHoldThreshold)
        {
            return;
        }

        fistLatched = true;

        if (currentMode == InteractionMode.Zoom)
        {
            SwitchMode(InteractionMode.Explode);
            return;
        }

        if (currentMode == InteractionMode.Explode)
        {
            SwitchMode(InteractionMode.Zoom);
            return;
        }

        if (currentMode == InteractionMode.Group)
        {
            SwitchMode(InteractionMode.Explode);
        }
    }

    private void UpdateOpenHandExplodeLock()
    {
        if (currentMode != InteractionMode.Explode)
        {
            openHandHoldTime = 0f;
            openHandLatched = false;
            return;
        }

        if (gestureCooldownRemaining > 0f)
        {
            return;
        }

        if (!openHandDetected)
        {
            openHandMissingTime += Time.deltaTime;
            if (openHandMissingTime > gestureDropGrace)
            {
                openHandHoldTime = 0f;
                openHandLatched = false;
            }
            return;
        }

        openHandMissingTime = 0f;
        openHandHoldTime += Time.deltaTime;
        if (openHandLatched || openHandHoldTime < gestureHoldThreshold)
        {
            return;
        }

        openHandLatched = true;
        explodeZoomLocked = true;
        currentExplodeAmount = targetExplodeAmount;
        Debug.Log("Explode locked. Pinch now controls camera zoom.");
    }

    private void UpdateThumbsUpMode()
    {
        if (gestureCooldownRemaining > 0f)
        {
            return;
        }

        if (!thumbsUpDetected)
        {
            thumbsUpMissingTime += Time.deltaTime;
            if (thumbsUpMissingTime > gestureDropGrace)
            {
                thumbsUpHoldTime = 0f;
                thumbsUpLatched = false;
            }
            return;
        }

        thumbsUpMissingTime = 0f;
        thumbsUpHoldTime += Time.deltaTime;
        if (thumbsUpLatched || thumbsUpHoldTime < modeSwitchHoldThreshold)
        {
            return;
        }

        thumbsUpLatched = true;

        if (currentMode == InteractionMode.Zoom)
        {
            SwitchMode(InteractionMode.Group);
            return;
        }

        if (currentMode == InteractionMode.Group && groupViewState == ExplodeView.GroupViewState.FocusedGroup)
        {
            SwitchMode(InteractionMode.Zoom);
            return;
        }

        if (currentMode == InteractionMode.Group && groupViewState == ExplodeView.GroupViewState.OrbitOverview)
        {
            SwitchMode(InteractionMode.Zoom);
        }
    }

    private void SwitchMode(InteractionMode nextMode)
    {
        if (currentMode == nextMode)
        {
            return;
        }

        currentMode = nextMode;
        if (nextMode == InteractionMode.Group)
        {
            EnsureGroupCameraConfigured();
            currentDistanceValue = GetGroupEntryZoom();
            targetDistanceValue = GetGroupEntryZoom();
            groupViewState = ExplodeView.GroupViewState.OrbitOverview;
            zoomController?.SetExternalControl(true);
            explodeView.ResetGroupSelection();
            FocusSelectedGroup();
        }
        else if (nextMode == InteractionMode.Explode)
        {
            currentDistanceValue = 0f;
            targetDistanceValue = 0f;
            currentExplodeAmount = 0f;
            targetExplodeAmount = 0f;
            explodeZoomLocked = false;
            groupViewState = ExplodeView.GroupViewState.OrbitOverview;
            ClearPendingGroupOrbitInput();
            groupCameraController?.Deactivate();
            rotateModel?.ResetRotation();
            zoomController?.ResetToDefaultView(0f, true);
            explodeView.SetExplodeAmount(0f, InteractionMode.Explode);
        }
        else
        {
            currentDistanceValue = 0f;
            targetDistanceValue = 0f;
            currentExplodeAmount = 0f;
            targetExplodeAmount = 0f;
            explodeZoomLocked = false;
            groupViewState = ExplodeView.GroupViewState.OrbitOverview;
            ClearPendingGroupOrbitInput();
            groupCameraController?.Deactivate();
            rotateModel?.ResetRotation();
            zoomController?.ResetToDefaultView(0f, true);
            explodeView.SetExplodeAmount(0f, InteractionMode.Zoom);
        }

        zoomController?.SetGroupFieldOfView(nextMode == InteractionMode.Group);
        gestureCooldownRemaining = gestureCooldown;
        fistHoldTime = 0f;
        fistMissingTime = 0f;
        thumbsUpHoldTime = 0f;
        thumbsUpMissingTime = 0f;
        openHandHoldTime = 0f;
        openHandMissingTime = 0f;
        pendingGroupNavigationDirection = 0;
        groupNavigationHoldTime = 0f;
        groupNavigationLatched = false;
        Debug.Log("Switched Mode To: " + currentMode);
    }

    private void UpdateGroupNavigation()
    {
        if (currentMode != InteractionMode.Group)
        {
            pendingGroupNavigationDirection = 0;
            groupNavigationHoldTime = 0f;
            groupNavigationLatched = false;
            return;
        }

        if (gestureCooldownRemaining > 0f)
        {
            return;
        }

        if (Keyboard.current != null)
        {
            if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
            {
                NavigateGroupNext();
                gestureCooldownRemaining = gestureCooldown;
                return;
            }

            if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
            {
                NavigateGroupPrevious();
                gestureCooldownRemaining = gestureCooldown;
                return;
            }
        }

        if (groupNavigationDirection == 0)
        {
            pendingGroupNavigationDirection = 0;
            groupNavigationHoldTime = 0f;
            groupNavigationLatched = false;
            return;
        }

        int intendedDirection = invertGroupNavigationDirection
            ? -groupNavigationDirection
            : groupNavigationDirection;

        if (pendingGroupNavigationDirection != intendedDirection)
        {
            pendingGroupNavigationDirection = intendedDirection;
            groupNavigationHoldTime = 0f;
            groupNavigationLatched = false;
        }

        groupNavigationHoldTime += Time.deltaTime;

        if (groupNavigationLatched)
        {
            return;
        }

        if (groupNavigationHoldTime < groupNavigationHoldThreshold)
        {
            return;
        }

        groupNavigationLatched = true;

        if (intendedDirection > 0)
        {
            NavigateGroupNext();
        }
        else
        {
            NavigateGroupPrevious();
        }

        gestureCooldownRemaining = gestureCooldown;
    }

    public void NavigateGroupNext()
    {
        if (currentMode != InteractionMode.Group)
        {
            return;
        }

        explodeView.NextGroup();
        FocusSelectedGroup();
    }

    public void NavigateGroupPrevious()
    {
        if (currentMode != InteractionMode.Group)
        {
            return;
        }

        explodeView.PreviousGroup();
        FocusSelectedGroup();
    }

    private void FocusSelectedGroup()
    {
        if (groupCameraController == null)
        {
            return;
        }

        groupViewState = ExplodeView.GroupViewState.FocusedGroup;
        currentDistanceValue = GetGroupEntryZoom();
        targetDistanceValue = GetGroupEntryZoom();
        groupCameraController.FocusGroup(
            explodeView.GetGroupCenter(explodeView.SelectedGroupIndex)
        );
    }

    private float GetGroupEntryZoom()
    {
        return Mathf.Clamp01(Mathf.Max(groupEntryZoom, minimumGroupEntryZoom));
    }

    private void QueueGroupOrbitInput(Vector2 input)
    {
        lock (groupOrbitInputLock)
        {
            pendingGroupOrbitInput += input;
        }
    }

    private void ApplyPendingGroupOrbitInput()
    {
        if (groupCameraController == null ||
            currentMode != InteractionMode.Group ||
            groupViewState != ExplodeView.GroupViewState.FocusedGroup)
        {
            ClearPendingGroupOrbitInput();
            return;
        }

        Vector2 orbitInput;
        lock (groupOrbitInputLock)
        {
            orbitInput = pendingGroupOrbitInput;
            pendingGroupOrbitInput = Vector2.zero;
        }

        if (orbitInput == Vector2.zero)
        {
            return;
        }

        groupCameraController.OrbitAroundFocus(orbitInput);
    }

    private void ClearPendingGroupOrbitInput()
    {
        lock (groupOrbitInputLock)
        {
            pendingGroupOrbitInput = Vector2.zero;
        }
    }

    private void EnsureGroupCameraConfigured()
    {
        Camera controlledCamera = null;
        if (zoomController != null)
        {
            controlledCamera = zoomController.GetComponent<Camera>();
        }

        if (controlledCamera == null)
        {
            controlledCamera = Camera.main;
        }

        if (groupCameraController == null && controlledCamera != null)
        {
            groupCameraController = controlledCamera.GetComponent<GroupCameraController>();
            if (groupCameraController == null)
            {
                groupCameraController = controlledCamera.gameObject.AddComponent<GroupCameraController>();
            }
        }

        if (groupCameraController != null)
        {
            if (groupCameraController.targetCamera == null && controlledCamera != null)
            {
                groupCameraController.targetCamera = controlledCamera;
            }

            if (groupCameraController.lookTarget == null && explodeView != null)
            {
                groupCameraController.lookTarget = explodeView.transform;
            }
        }

        if (explodeView != null)
        {
            Camera groupCamera = controlledCamera;
            if (groupCameraController != null && groupCameraController.targetCamera != null)
            {
                groupCamera = groupCameraController.targetCamera;
            }

            explodeView.SetGroupCamera(groupCamera);
        }
    }
}
