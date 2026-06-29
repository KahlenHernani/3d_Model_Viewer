using UnityEngine;

public class TankHandController : MonoBehaviour
{
    public DragRotateModel rotateModel;
    public CameraZoomController zoomController;
    public ExplodeView explodeView;
    public GroupView groupView;

    [Header("Distance Controls")]
    [SerializeField] private float distanceSensitivity = 60f;
    [SerializeField] private float smoothingSpeed = 15f;

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

    private bool fistPreviouslyDetected = false;
    // Added for the exploded, grouped mode.
    private bool peaceDetected = false;
    private bool peacePreviouslyDetected = false;

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
        else if(currentMode == InteractionMode.Explode)
        {
            explodeView.SetExplodeAmount(currentDistanceValue, currentMode);
        } 
        else if(currentMode == InteractionMode.Group)
        {
            // Needs to subtly group everything in their own, name respective groups
            // Where they can be zoomed in on even more
            explodeView.setExplodeAmount(currentDistanceValue, currentMode);
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

    public void UpdateFist(bool fistDetected)
    {
        if (fistDetected && !fistPreviouslyDetected)
        {
            currentMode =
                currentMode == InteractionMode.Zoom
                ? InteractionMode.Explode
                : InteractionMode.Zoom;

                // Fixes bug where entering the exploded view did not keep the model intact until pulled apart with other hand symbols
                if(currentMode == InteractionMode.Explode)
            {
                currentDistanceValue = 0f;
                targetDistanceValue = 0f;
            }

            Debug.Log("Switched Mode To: " + currentMode);
        }

        fistPreviouslyDetected = fistDetected;
    }

    public void UpdatePeaceDetected(bool peaceDetected)
    {
        if(peaceDetected && !peacePreviouslyDetected)
        {
            currentMode = currentMode == InteractionMode.Zoom
            ? InteractionMode.Group
            : InteractionMode.Zoom;

            if(currentMode == InteractionMode.Group)
            {
                currentDistanceValue = 0f;
                targetDistanceValue= 0f;
            }
            Debug.Log("Switched Mode To: "+currentMode);
        }
        peacePreviouslyDetected = peaceDetected;
    }
}