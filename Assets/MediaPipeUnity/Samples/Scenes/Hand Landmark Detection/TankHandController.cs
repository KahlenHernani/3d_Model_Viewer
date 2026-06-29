using UnityEngine;

public class TankHandController : MonoBehaviour
{
    public DragRotateModel rotateModel;
    public CameraZoomController zoomController;
    public ExplodeView explodeView;

    [Header("Distance Controls")]
    [SerializeField] private float distanceSensitivity = 60f;
    [SerializeField] private float smoothingSpeed = 15f;
    [SerializeField] private float gestureHoldThreshold = 0.2f;
    [SerializeField] private float gestureCooldown = 0.55f;
    [SerializeField] private float groupEntryZoom = 0.15f;

    private float currentDistanceValue = 0.5f;
    private float targetDistanceValue = 0.5f;

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
    private float fistHoldTime = 0f;
    private bool fistLatched = false;
    private float thumbsUpHoldTime = 0f;
    private bool thumbsUpLatched = false;
    private float gestureCooldownRemaining = 0f;

    private void Start()
    {
        zoomController.SetGroupFieldOfView(false);
    }

    private void Update()
    {
        currentDistanceValue = Mathf.Lerp(
            currentDistanceValue,
            targetDistanceValue,
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
            explodeView.SetExplodeAmount(currentDistanceValue, currentMode);
        }
        else if (currentMode == InteractionMode.Group)
        {
            zoomController.SetZoom(currentDistanceValue);
            explodeView.SetExplodeAmount(1f, currentMode);
        }

        UpdateFistMode();
        UpdateThumbsUpMode();
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
            rotateModel.SetRotationInput(delta * 15f);
        }

        previousWrist = wrist;
        hasPreviousWrist = true;
    }

    public void UpdateDistanceDelta(float delta)
    {
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

    private void UpdateFistMode()
    {
        if (gestureCooldownRemaining > 0f)
        {
            return;
        }

        if (!fistDetected)
        {
            fistHoldTime = 0f;
            fistLatched = false;
            return;
        }

        fistHoldTime += Time.deltaTime;
        if (fistLatched || fistHoldTime < gestureHoldThreshold)
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
        }
    }

    private void UpdateThumbsUpMode()
    {
        if (gestureCooldownRemaining > 0f)
        {
            return;
        }

        if (!thumbsUpDetected)
        {
            thumbsUpHoldTime = 0f;
            thumbsUpLatched = false;
            return;
        }

        thumbsUpHoldTime += Time.deltaTime;
        if (thumbsUpLatched || thumbsUpHoldTime < gestureHoldThreshold)
        {
            return;
        }

        thumbsUpLatched = true;

        if (currentMode == InteractionMode.Zoom)
        {
            SwitchMode(InteractionMode.Group);
            return;
        }

        if (currentMode == InteractionMode.Group)
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
            currentDistanceValue = groupEntryZoom;
            targetDistanceValue = groupEntryZoom;
        }
        else
        {
            currentDistanceValue = 0f;
            targetDistanceValue = 0f;
        }

        zoomController.SetGroupFieldOfView(nextMode == InteractionMode.Group);
        gestureCooldownRemaining = gestureCooldown;
        fistHoldTime = 0f;
        thumbsUpHoldTime = 0f;
        Debug.Log("Switched Mode To: " + currentMode);
    }
}
