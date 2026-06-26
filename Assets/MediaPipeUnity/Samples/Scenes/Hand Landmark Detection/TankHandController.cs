using UnityEngine;

public class TankHandController : MonoBehaviour
{
    public DragRotateModel rotateModel;
    public CameraZoomController zoomController;
    public ExplodeView explodeView;

    [Header("Distance Controls")]
    [SerializeField] private float distanceSensitivity = 3f;
    [SerializeField] private float damping = 5f;

    private float currentDistanceValue = 0.5f;
    private float distanceVelocity = 0f;

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
        // Apply velocity
        currentDistanceValue += distanceVelocity * Time.deltaTime;

        currentDistanceValue = Mathf.Clamp01(currentDistanceValue);

        // Smooth deceleration
        distanceVelocity = Mathf.Lerp(
            distanceVelocity,
            0f,
            damping * Time.deltaTime
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
        distanceVelocity += delta * distanceSensitivity;
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