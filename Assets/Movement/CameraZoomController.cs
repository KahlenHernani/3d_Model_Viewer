using UnityEngine;
using UnityEngine.InputSystem;

public class CameraZoomController : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Zoom Settings")]
    [SerializeField] private float minDistance = 2f;
    [SerializeField] private float maxDistance = 10f;
    [SerializeField, Range(0f, 1f)] private float zoomValue = 0.5f;
    [SerializeField] private float scrollSensitivity = 0.1f;

    [Header("FOV Settings")]
    [SerializeField] private float normalFieldOfView = 60f;
    [SerializeField] private float groupFieldOfView = 85f;
    [SerializeField] private float fovSmoothingSpeed = 8f;

    private Vector3 startOffsetDirection;
    private Quaternion startRotation;
    private Camera controlledCamera;
    private float targetFieldOfView;
    private bool externalControlEnabled = false;

    private void Start()
    {
        startRotation = transform.rotation;
        controlledCamera = GetComponent<Camera>();
        if (controlledCamera != null)
        {
            normalFieldOfView = controlledCamera.fieldOfView;
            targetFieldOfView = normalFieldOfView;
        }

        if (target == null)
            return;

        Vector3 offset = transform.position - target.position;
        startOffsetDirection = offset.normalized;
    }

    private void Update()
    {
        if (externalControlEnabled)
        {
            return;
        }

        if (Mouse.current != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;

            if (Mathf.Abs(scroll) > 0.01f)
            {
                SetZoom(zoomValue + scroll * scrollSensitivity * 0.01f);
            }
        }

        UpdateCameraPosition();
        UpdateFieldOfView();
    }

    public void SetZoom(float normalizedValue)
    {
        zoomValue = Mathf.Clamp01(normalizedValue);
    }

    public void SetGroupFieldOfView(bool isGroupMode)
    {
        targetFieldOfView = isGroupMode ? groupFieldOfView : normalFieldOfView;
    }

    public void ResetToNormalFieldOfView(bool immediate)
    {
        targetFieldOfView = normalFieldOfView;

        if (immediate && controlledCamera != null)
        {
            controlledCamera.fieldOfView = normalFieldOfView;
        }
    }

    public void ResetToDefaultView(float normalizedZoom, bool immediate)
    {
        SetExternalControl(false);
        SetZoom(normalizedZoom);
        ResetToNormalFieldOfView(immediate);

        if (immediate)
        {
            UpdateCameraPosition();
            transform.rotation = startRotation;
        }
    }

    public void SetExternalControl(bool enabled)
    {
        externalControlEnabled = enabled;
    }

    private void UpdateCameraPosition()
    {
        if (target == null)
            return;

        float distance = Mathf.Lerp(maxDistance, minDistance, zoomValue);
        transform.position = target.position + startOffsetDirection * distance;
    }

    private void UpdateFieldOfView()
    {
        if (controlledCamera == null)
            return;

        controlledCamera.fieldOfView = Mathf.Lerp(
            controlledCamera.fieldOfView,
            targetFieldOfView,
            fovSmoothingSpeed * Time.deltaTime
        );
    }
}
