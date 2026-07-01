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
    [SerializeField] private float fistSwitchHoldThreshold = 0.1f;
    [SerializeField] private float modeSwitchHoldThreshold = 0.3f;
    [SerializeField] private float gestureCooldown = 0.45f;
    [SerializeField] private float gestureDropGrace = 0.35f;
    [SerializeField] private float groupEntryZoom = 0.15f;
    [SerializeField] private float minimumGroupEntryZoom = 0.25f;
    [Header("Gesture Confidence")]
    [SerializeField, Range(0f, 1f)] private float fistConfidenceThreshold = 0.62f;
    [SerializeField, Range(0f, 1f)] private float thumbsUpConfidenceThreshold = 0.68f;
    [SerializeField, Range(0f, 1f)] private float openHandConfidenceThreshold = 0.75f;
    [Header("Gesture Buffering")]
    [SerializeField] private int gestureBufferSize = 15;
    [SerializeField, Range(0f, 1f)] private float modeGestureDominanceRatio = 0.7f;
    [SerializeField, Range(0f, 1f)] private float modeExitDominanceRatio = 0.82f;
    [SerializeField, Range(0f, 0.4f)] private float modeExitConfidenceBonus = 0.12f;
    [SerializeField, Range(0f, 1f)] private float actionGestureDominanceRatio = 0.6f;
    [Header("Group Navigation")]
    [SerializeField] private float groupNavigationHoldThreshold = 0.18f;
    [SerializeField, Range(0f, 1f)] private float groupNavigationConfidenceThreshold = 0.72f;
    [SerializeField, Range(0f, 1f)] private float groupNavigationDominanceRatio = 0.6f;
    [SerializeField] private bool invertGroupNavigationDirection = false;
    [Header("Gesture Debug")]
    [SerializeField, Range(0f, 1f)] private float lastFistConfidence = 0f;
    [SerializeField, Range(0f, 1f)] private float lastThumbsUpConfidence = 0f;
    [SerializeField, Range(0f, 1f)] private float lastOpenHandConfidence = 0f;
    [SerializeField, Range(0f, 1f)] private float lastGroupNavigationConfidence = 0f;
    [SerializeField] private int lastGroupNavigationDirection = 0;
    [SerializeField] private GestureIntent lastRawGestureIntent = GestureIntent.None;
    [SerializeField] private GestureIntent dominantGestureIntent = GestureIntent.None;
    [SerializeField, Range(0f, 1f)] private float dominantGestureRatio = 0f;
    [SerializeField, Range(0f, 1f)] private float dominantGestureConfidence = 0f;
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

    private bool fistDetected;
    private bool thumbsUpDetected;
    private bool openHandDetected;
    private readonly object gestureInputLock = new object();
    private float pendingFistConfidence = 0f;
    private float pendingThumbsUpConfidence = 0f;
    private float pendingOpenHandConfidence = 0f;
    private float pendingGroupNavigationConfidence = 0f;
    private int pendingGroupNavigationInputDirection = 0;
    private int pendingGestureFrameVersion = 0;
    private int consumedGestureFrameVersion = -1;
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
    private int groupNavigationDirection = 0;
    private int pendingGroupNavigationDirection = 0;
    private float groupNavigationHoldTime = 0f;
    private bool groupNavigationLatched = false;
    private ExplodeView.GroupViewState groupViewState = ExplodeView.GroupViewState.OrbitOverview;
    private bool explodeZoomLocked = false;
    private readonly object groupOrbitInputLock = new object();
    private Vector2 pendingGroupOrbitInput = Vector2.zero;
    private GestureFrame[] gestureFrames;
    private int gestureFrameIndex = 0;
    private int gestureFrameCount = 0;

    private enum GestureIntent
    {
        None,
        Fist,
        ThumbsUp,
        OpenHand,
        GroupNext,
        GroupPrevious
    }

    private struct GestureFrame
    {
        public GestureIntent intent;
        public float confidence;

        public GestureFrame(GestureIntent intent, float confidence)
        {
            this.intent = intent;
            this.confidence = confidence;
        }
    }

    private void Start()
    {
        EnsureGestureBuffer();
        EnsureGroupCameraConfigured();
        zoomController?.SetGroupFieldOfView(false);
    }

    private void Update()
    {
        RefreshGestureDetections();

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
        lock (gestureInputLock)
        {
            pendingFistConfidence = detected ? 1f : 0f;
            pendingGestureFrameVersion++;
        }
    }

    public void SetThumbsUpDetected(bool detected)
    {
        lock (gestureInputLock)
        {
            pendingThumbsUpConfidence = detected ? 1f : 0f;
            pendingGestureFrameVersion++;
        }
    }

    public void SetOpenHandDetected(bool detected)
    {
        lock (gestureInputLock)
        {
            pendingOpenHandConfidence = detected ? 1f : 0f;
            pendingGestureFrameVersion++;
        }
    }

    public void SetGroupNavigationDirection(int direction)
    {
        lock (gestureInputLock)
        {
            pendingGroupNavigationInputDirection = Mathf.Clamp(direction, -1, 1);
            pendingGroupNavigationConfidence = direction == 0 ? 0f : 1f;
            pendingGestureFrameVersion++;
        }
    }

    public void SetGestureConfidences(
        float fistConfidence,
        float thumbsUpConfidence,
        float openHandConfidence,
        int navigationDirection,
        float navigationConfidence)
    {
        lock (gestureInputLock)
        {
            pendingFistConfidence = Mathf.Clamp01(fistConfidence);
            pendingThumbsUpConfidence = Mathf.Clamp01(thumbsUpConfidence);
            pendingOpenHandConfidence = Mathf.Clamp01(openHandConfidence);
            pendingGroupNavigationInputDirection = Mathf.Clamp(navigationDirection, -1, 1);
            pendingGroupNavigationConfidence = Mathf.Clamp01(navigationConfidence);
            pendingGestureFrameVersion++;
        }
    }

    private void RefreshGestureDetections()
    {
        int frameVersion;
        lock (gestureInputLock)
        {
            lastFistConfidence = pendingFistConfidence;
            lastThumbsUpConfidence = pendingThumbsUpConfidence;
            lastOpenHandConfidence = pendingOpenHandConfidence;
            lastGroupNavigationConfidence = pendingGroupNavigationConfidence;
            lastGroupNavigationDirection = pendingGroupNavigationInputDirection;
            frameVersion = pendingGestureFrameVersion;
        }

        if (frameVersion != consumedGestureFrameVersion)
        {
            lastRawGestureIntent = ClassifyCurrentGestureFrame(out float rawConfidence);
            AddGestureFrame(lastRawGestureIntent, rawConfidence);
            consumedGestureFrameVersion = frameVersion;
        }

        UpdateDominantGestureDebug();

        bool thumbsUpAllowed =
            currentMode == InteractionMode.Zoom ||
            currentMode == InteractionMode.Group;
        bool openHandAllowed = currentMode == InteractionMode.Explode;
        float fistRequiredConfidence = currentMode == InteractionMode.Explode
            ? Mathf.Clamp01(fistConfidenceThreshold + modeExitConfidenceBonus)
            : fistConfidenceThreshold;
        float thumbsUpRequiredConfidence = currentMode == InteractionMode.Group
            ? Mathf.Clamp01(thumbsUpConfidenceThreshold + modeExitConfidenceBonus)
            : thumbsUpConfidenceThreshold;
        float fistRequiredRatio = currentMode == InteractionMode.Explode
            ? modeExitDominanceRatio
            : modeGestureDominanceRatio;
        float thumbsUpRequiredRatio = currentMode == InteractionMode.Group
            ? modeExitDominanceRatio
            : modeGestureDominanceRatio;

        thumbsUpDetected =
            thumbsUpAllowed &&
            lastRawGestureIntent == GestureIntent.ThumbsUp &&
            IsGestureDominant(GestureIntent.ThumbsUp, thumbsUpRequiredRatio, thumbsUpRequiredConfidence);
        openHandDetected =
            openHandAllowed &&
            !thumbsUpDetected &&
            lastRawGestureIntent == GestureIntent.OpenHand &&
            IsGestureDominant(GestureIntent.OpenHand, actionGestureDominanceRatio, openHandConfidenceThreshold);
        fistDetected =
            !thumbsUpDetected &&
            !openHandDetected &&
            lastRawGestureIntent == GestureIntent.Fist &&
            IsGestureDominant(GestureIntent.Fist, fistRequiredRatio, fistRequiredConfidence);

        groupNavigationDirection = 0;
        if (currentMode == InteractionMode.Group &&
            !thumbsUpDetected &&
            !openHandDetected &&
            !fistDetected &&
            (lastRawGestureIntent == GestureIntent.GroupNext ||
             lastRawGestureIntent == GestureIntent.GroupPrevious))
        {
            if (IsGestureDominant(
                    GestureIntent.GroupNext,
                    groupNavigationDominanceRatio,
                    groupNavigationConfidenceThreshold))
            {
                groupNavigationDirection = 1;
            }
            else if (IsGestureDominant(
                         GestureIntent.GroupPrevious,
                         groupNavigationDominanceRatio,
                         groupNavigationConfidenceThreshold))
            {
                groupNavigationDirection = -1;
            }
        }
    }

    private GestureIntent ClassifyCurrentGestureFrame(out float confidence)
    {
        GestureIntent bestIntent = GestureIntent.None;
        float bestScore = 0f;
        confidence = 0f;

        ConsiderGestureCandidate(
            GestureIntent.Fist,
            lastFistConfidence,
            fistConfidenceThreshold,
            ref bestIntent,
            ref bestScore,
            ref confidence
        );

        if (currentMode == InteractionMode.Zoom || currentMode == InteractionMode.Group)
        {
            ConsiderGestureCandidate(
                GestureIntent.ThumbsUp,
                lastThumbsUpConfidence,
                thumbsUpConfidenceThreshold,
                ref bestIntent,
                ref bestScore,
                ref confidence
            );
        }

        if (currentMode == InteractionMode.Explode)
        {
            ConsiderGestureCandidate(
                GestureIntent.OpenHand,
                lastOpenHandConfidence,
                openHandConfidenceThreshold,
                ref bestIntent,
                ref bestScore,
                ref confidence
            );
        }

        if (currentMode == InteractionMode.Group && lastGroupNavigationDirection != 0)
        {
            GestureIntent navigationIntent = lastGroupNavigationDirection > 0
                ? GestureIntent.GroupNext
                : GestureIntent.GroupPrevious;
            ConsiderGestureCandidate(
                navigationIntent,
                lastGroupNavigationConfidence,
                groupNavigationConfidenceThreshold,
                ref bestIntent,
                ref bestScore,
                ref confidence
            );
        }

        return bestIntent;
    }

    private static void ConsiderGestureCandidate(
        GestureIntent candidateIntent,
        float candidateConfidence,
        float threshold,
        ref GestureIntent bestIntent,
        ref float bestScore,
        ref float bestConfidence)
    {
        if (candidateConfidence < threshold)
        {
            return;
        }

        float score = threshold > 0f
            ? candidateConfidence / threshold
            : candidateConfidence;
        if (score <= bestScore)
        {
            return;
        }

        bestIntent = candidateIntent;
        bestScore = score;
        bestConfidence = candidateConfidence;
    }

    private void EnsureGestureBuffer()
    {
        int safeBufferSize = Mathf.Max(1, gestureBufferSize);
        if (gestureFrames != null && gestureFrames.Length == safeBufferSize)
        {
            return;
        }

        gestureFrames = new GestureFrame[safeBufferSize];
        gestureFrameIndex = 0;
        gestureFrameCount = 0;
    }

    private void AddGestureFrame(GestureIntent intent, float confidence)
    {
        EnsureGestureBuffer();
        gestureFrames[gestureFrameIndex] = new GestureFrame(intent, Mathf.Clamp01(confidence));
        gestureFrameIndex = (gestureFrameIndex + 1) % gestureFrames.Length;
        gestureFrameCount = Mathf.Min(gestureFrameCount + 1, gestureFrames.Length);
    }

    private bool IsGestureDominant(
        GestureIntent targetIntent,
        float requiredRatio,
        float requiredAverageConfidence)
    {
        if (gestureFrames == null || gestureFrameCount < gestureFrames.Length)
        {
            return false;
        }

        int matchingFrameCount = 0;
        float confidenceSum = 0f;

        for (int i = 0; i < gestureFrameCount; i++)
        {
            GestureFrame frame = gestureFrames[i];
            if (frame.intent != targetIntent)
            {
                continue;
            }

            matchingFrameCount++;
            confidenceSum += frame.confidence;
        }

        float ratio = matchingFrameCount / (float)gestureFrameCount;
        float averageConfidence = matchingFrameCount > 0
            ? confidenceSum / matchingFrameCount
            : 0f;

        return
            ratio >= requiredRatio &&
            averageConfidence >= requiredAverageConfidence;
    }

    private void UpdateDominantGestureDebug()
    {
        dominantGestureIntent = GestureIntent.None;
        dominantGestureRatio = 0f;
        dominantGestureConfidence = 0f;

        if (gestureFrames == null || gestureFrameCount == 0)
        {
            return;
        }

        GestureIntent[] intents =
        {
            GestureIntent.Fist,
            GestureIntent.ThumbsUp,
            GestureIntent.OpenHand,
            GestureIntent.GroupNext,
            GestureIntent.GroupPrevious
        };

        for (int intentIndex = 0; intentIndex < intents.Length; intentIndex++)
        {
            GestureIntent intent = intents[intentIndex];
            int matchingFrameCount = 0;
            float confidenceSum = 0f;

            for (int frameIndex = 0; frameIndex < gestureFrameCount; frameIndex++)
            {
                GestureFrame frame = gestureFrames[frameIndex];
                if (frame.intent != intent)
                {
                    continue;
                }

                matchingFrameCount++;
                confidenceSum += frame.confidence;
            }

            if (matchingFrameCount == 0)
            {
                continue;
            }

            float ratio = matchingFrameCount / (float)gestureFrameCount;
            if (ratio <= dominantGestureRatio)
            {
                continue;
            }

            dominantGestureIntent = intent;
            dominantGestureRatio = ratio;
            dominantGestureConfidence = confidenceSum / matchingFrameCount;
        }
    }

    private void ClearGestureBuffer()
    {
        gestureFrameIndex = 0;
        gestureFrameCount = 0;
        dominantGestureIntent = GestureIntent.None;
        dominantGestureRatio = 0f;
        dominantGestureConfidence = 0f;
        lastRawGestureIntent = GestureIntent.None;
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
        ClearGestureBuffer();
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
