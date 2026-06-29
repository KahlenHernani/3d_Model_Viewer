using UnityEngine;

public class TankHandController : MonoBehaviour
{
    public DragRotateModel rotateModel;
    public CameraZoomController zoomController;
    public ExplodeView explodeView;
    public ModelPanController panController;

    [Header("Distance Controls")]
    [SerializeField] private float distanceSensitivity = 60f;
    [SerializeField] private float smoothingSpeed = 15f;

    private float currentDistanceValue = 0.5f;
    private float targetDistanceValue = 0.5f;

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

    private void Update()
    {
        currentDistanceValue = Mathf.Lerp(
            currentDistanceValue,
            targetDistanceValue,
            smoothingSpeed * Time.deltaTime
        );

        if (currentMode == InteractionMode.Zoom)
        {
            zoomController.SetZoom(currentDistanceValue);
        }
        else
        {
            explodeView.SetExplodeAmount(currentDistanceValue);
        }
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

    public void UpdatePan(Vector2 delta)
    {
        panController?.SetPanInput(delta);
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