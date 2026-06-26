using UnityEngine;

public class TankHandController : MonoBehaviour
{
    public DragRotateModel rotateModel;
    public CameraZoomController zoomController;
    public ExplodeView explodeView;

    private Vector2 previousWrist;
    private bool hasPreviousWrist = false;

    public void UpdateRotation(Vector2 wrist)
    {
        if (hasPreviousWrist)
        {
            Vector2 delta = wrist - previousWrist;
            rotateModel.SetRotationInput(delta * 10f);
        }

        previousWrist = wrist;
        hasPreviousWrist = true;
    }

    public void UpdateZoom(float zoom)
    {
        zoomController.SetZoom(zoom);
    }

    public void UpdateExplode(float explode)
    {
        explodeView.SetExplodeAmount(explode);
    }
}