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

    private Vector3 startOffsetDirection;

    private void Start()
    {
        if (target == null)
            return;

        Vector3 offset = transform.position - target.position;
        startOffsetDirection = offset.normalized;
    }

    private void Update()
    {
        if (Mouse.current != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;

            if (Mathf.Abs(scroll) > 0.01f)
            {
                SetZoom(zoomValue + scroll * scrollSensitivity * 0.01f);
            }
        }

        UpdateCameraPosition();
    }

    public void SetZoom(float normalizedValue)
    {
        zoomValue = Mathf.Clamp01(normalizedValue);
    }

    private void UpdateCameraPosition()
    {
        if (target == null)
            return;

        float distance = Mathf.Lerp(maxDistance, minDistance, zoomValue);
        transform.position = target.position + startOffsetDirection * distance;
    }
}