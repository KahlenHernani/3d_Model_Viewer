using UnityEngine;
using UnityEngine.InputSystem;

public class DragRotateModel : MonoBehaviour
{
    [Header("Drag Rotation")]
    [SerializeField] private float rotationSpeed = 0.2f;
    [SerializeField] private bool useLeftMouseButton = true;

    private void Update()
    {
        if (Mouse.current == null)
            return;

        bool dragging = useLeftMouseButton
            ? Mouse.current.leftButton.isPressed
            : Mouse.current.rightButton.isPressed;

        if (!dragging)
            return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        float yaw = -mouseDelta.x * rotationSpeed;
        float pitch = -mouseDelta.y * rotationSpeed;

        transform.Rotate(Vector3.up, yaw, Space.World);
        transform.Rotate(Vector3.right, pitch, Space.World);
    }
}