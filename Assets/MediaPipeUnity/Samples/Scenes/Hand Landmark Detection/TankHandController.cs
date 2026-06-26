using UnityEngine;

public class TankHandController : MonoBehaviour
{
    private float currentDistanceValue = 0.5f;

    [SerializeField]
    private float distanceSensitivity = 3f;
    public DragRotateModel rotateModel;
    public CameraZoomController zoomController;
    public ExplodeView explodeView;

    public enum InteractionMode
    {
        Zoom,
        Explode
    }

    [Header("Current Mode")]
    public InteractionMode currentMode = InteractionMode.Zoom;

    private Vector2 previousWrist;
    private bool hasPreviousWrist = false;

    private bool fistPreviouslyDetected = false;

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
        currentDistanceValue += delta * distanceSensitivity;

        currentDistanceValue =
            Mathf.Clamp01(currentDistanceValue);

        if (currentMode == InteractionMode.Zoom)
        {
            zoomController.SetZoom(currentDistanceValue);
        }
        else
        {
            explodeView.SetExplodeAmount(currentDistanceValue);
        }
    }

    public void UpdateFist(bool fistDetected)
    {
        if (fistDetected && !fistPreviouslyDetected)
        {
            currentMode =
                currentMode == InteractionMode.Zoom
                ? InteractionMode.Explode
                : InteractionMode.Zoom;

            Debug.Log("Switched Mode To: " + currentMode);
        }

        fistPreviouslyDetected = fistDetected;
    }
}