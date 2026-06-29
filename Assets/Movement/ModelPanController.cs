using UnityEngine;

public class ModelPanController : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The model (or parent pivot) to pan. Usually the same object DragRotateModel is on.")]
    [SerializeField] private Transform target;
    [Tooltip("Defaults to Camera.main.")]
    [SerializeField] private Camera viewCamera;

    [Header("Pan Settings")]
    [SerializeField] private float panSpeed = 4f;
    [SerializeField, Range(0f, 0.95f)] private float smoothing = 0.12f;

    private Vector3 _targetPos;

    void Start()
    {
        if (viewCamera == null) viewCamera = Camera.main;
        if (target != null) _targetPos = target.position;
    }

    /// <summary>
    /// Call with the normalized image-space midpoint delta between two hands.
    /// Image x grows right, image y grows down.
    /// </summary>
    public void SetPanInput(Vector2 imageDelta)
    {
        if (viewCamera == null || target == null) return;

        Vector3 right = viewCamera.transform.right;
        Vector3 up    = viewCamera.transform.up;
        _targetPos += (right * imageDelta.x - up * imageDelta.y) * panSpeed;
    }

    void Update()
    {
        if (target == null) return;
        target.position = Vector3.Lerp(target.position, _targetPos, 1f - smoothing);
    }
}
