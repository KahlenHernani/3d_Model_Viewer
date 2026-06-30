// GestureController.cs
// Receives hand landmark data and named gesture results from GestureRecognizerRunner,
// then drives the three model-control components (rotate, zoom, explode).
// Also fires Next / Previous / Reload events used by training-app UI.
//
// Gesture mapping
// ─────────────────────────────────────────────────────
//  One hand, pinch + move wrist  →  Rotate model
//  Two hands, both pinch + spread →  Zoom (default) or Explode (when in Explode mode)
//  Closed_Fist (held 0.5 s)      →  Toggle Zoom ↔ Explode mode
//  Pointing_Up (one hand)         →  Zoom — wrist Y position sets zoom level
//  Thumb_Up                       →  Next
//  Thumb_Down                     →  Previous
//  Open_Palm (held 0.5 s)         →  Reload
//  Victory / ILoveYou             →  Reset model rotation to zero
// ─────────────────────────────────────────────────────

using UnityEngine;
using UnityEngine.Events;

public class GestureController : MonoBehaviour
{
    [Header("Model Control Components")]
    public DragRotateModel rotateModel;
    public CameraZoomController zoomController;
    public ExplodeView explodeView;

    [Header("Sensitivity")]
    [SerializeField] private float distanceSensitivity = 60f;
    [SerializeField] private float smoothingSpeed = 15f;
    [SerializeField] private float rotationSensitivity = 15f;

    [Header("Hold Thresholds")]
    [SerializeField] private float fistHoldTime = 0.5f;
    [SerializeField] private float openPalmHoldTime = 0.5f;

    [Header("Training App Events")]
    public UnityEvent OnNext;
    public UnityEvent OnPrevious;
    public UnityEvent OnReload;
    public UnityEvent OnResetRotation;

    public enum InteractionMode { Zoom, Explode }
    [HideInInspector] public InteractionMode currentMode = InteractionMode.Zoom;
    [HideInInspector] public string currentGestureName = "None";

    private float currentDistanceValue = 0.5f;
    private float targetDistanceValue = 0.5f;
    private float previousHandSpread = -1f;

    private Vector2 previousWrist;
    private bool hasPreviousWrist = false;

    private float fistHeldFor = 0f;
    private bool fistModeToggled = false;

    private float openPalmHeldFor = 0f;
    private bool reloadFired = false;

    private string lastOneShot = "";

    private void Update()
    {
        currentDistanceValue = Mathf.Lerp(
            currentDistanceValue,
            targetDistanceValue,
            smoothingSpeed * Time.deltaTime
        );

        if (currentMode == InteractionMode.Zoom)
            zoomController.SetZoom(currentDistanceValue);
        else
            explodeView.SetExplodeAmount(currentDistanceValue);
    }

    public void ProcessGesture(
        string gestureName,
        Vector2 wristPos,
        bool isPinching,
        Vector2 secondWristPos,
        bool secondIsPinching,
        bool secondHandPresent)
    {
        currentGestureName = string.IsNullOrEmpty(gestureName) ? "None" : gestureName;

        if (currentGestureName == "None")
        {
            ResetHoldTimers();
            hasPreviousWrist = false;
            previousHandSpread = -1f;
            return;
        }

        HandleHeldGestures(currentGestureName);
        HandleOneShotGestures(currentGestureName);

        // Pointing_Up: wrist Y position directly controls zoom level
        if (currentGestureName == "Pointing_Up" && !secondHandPresent)
        {
            // wristPos.y is 0 at top, 1 at bottom — invert so raising hand zooms in
            targetDistanceValue = Mathf.Clamp01(1f - wristPos.y);
            return;
        }

        // Rotation: one hand, pinch active
        if (!secondHandPresent)
        {
            UpdateRotation(wristPos, isPinching);
        }

        // Zoom / Explode: two hands, both pinching
        if (secondHandPresent && isPinching && secondIsPinching)
        {
            float spread = Vector2.Distance(wristPos, secondWristPos);
            UpdateSpread(spread);
        }
        else
        {
            previousHandSpread = -1f;
        }
    }

    private void UpdateRotation(Vector2 wrist, bool isPinching)
    {
        if (!isPinching)
        {
            hasPreviousWrist = false;
            return;
        }

        if (hasPreviousWrist)
        {
            Vector2 delta = wrist - previousWrist;
            rotateModel.SetRotationInput(delta * rotationSensitivity);
        }

        previousWrist = wrist;
        hasPreviousWrist = true;
    }

    private void UpdateSpread(float spread)
    {
        if (previousHandSpread < 0f)
        {
            previousHandSpread = spread;
            return;
        }

        float delta = spread - previousHandSpread;
        previousHandSpread = spread;

        if (Mathf.Abs(delta) < 0.001f)
            return;

        targetDistanceValue = Mathf.Clamp01(targetDistanceValue + delta * distanceSensitivity);
    }

    private void HandleHeldGestures(string gesture)
    {
        if (gesture == "Closed_Fist")
        {
            fistHeldFor += Time.deltaTime;
            if (fistHeldFor >= fistHoldTime && !fistModeToggled)
            {
                ToggleMode();
                fistModeToggled = true;
            }
        }
        else
        {
            fistHeldFor = 0f;
            fistModeToggled = false;
        }

        if (gesture == "Open_Palm")
        {
            openPalmHeldFor += Time.deltaTime;
            if (openPalmHeldFor >= openPalmHoldTime && !reloadFired)
            {
                OnReload?.Invoke();
                Debug.Log("[GestureController] Reload");
                reloadFired = true;
            }
        }
        else
        {
            openPalmHeldFor = 0f;
            reloadFired = false;
        }
    }

    private void HandleOneShotGestures(string gesture)
    {
        if (gesture == lastOneShot)
            return;

        lastOneShot = gesture;

        switch (gesture)
        {
            case "Thumb_Up":
                OnNext?.Invoke();
                Debug.Log("[GestureController] Next");
                break;

            case "Thumb_Down":
                OnPrevious?.Invoke();
                Debug.Log("[GestureController] Previous");
                break;

            case "Victory":
            case "ILoveYou":
                ResetModelRotation();
                break;
        }
    }

    private void ToggleMode()
    {
        currentMode = currentMode == InteractionMode.Zoom
            ? InteractionMode.Explode
            : InteractionMode.Zoom;

        Debug.Log("[GestureController] Mode → " + currentMode);
    }

    private void ResetModelRotation()
    {
        if (rotateModel != null)
            rotateModel.transform.rotation = Quaternion.identity;

        OnResetRotation?.Invoke();
        Debug.Log("[GestureController] Reset rotation");
    }

    private void ResetHoldTimers()
    {
        fistHeldFor = 0f;
        fistModeToggled = false;
        openPalmHeldFor = 0f;
        reloadFired = false;
        lastOneShot = "";
    }
}