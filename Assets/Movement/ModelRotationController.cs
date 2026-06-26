using UnityEngine;
using UnityEngine.InputSystem;

public class DragRotateModel : MonoBehaviour
{
    [Header("Drag Rotation")]
    [SerializeField] private float rotationSpeed = 0.2f;
    [SerializeField] private bool useLeftMouseButton = true;

    private Vector2 rotationInput;

    public void SetRotationInput(Vector2 input)
    {
        rotationInput = input;
    }

    private void Update()
    {
        // MediaPipe input
        if (rotationInput != Vector2.zero)
        {
            float yaw = -rotationInput.x * rotationSpeed * 100f;
            float pitch = -rotationInput.y * rotationSpeed * 100f;

            transform.Rotate(Vector3.up, yaw, Space.World);
            transform.Rotate(Vector3.right, pitch, Space.World);

            rotationInput = Vector2.zero;
            return;
        }

        // Mouse fallback
        if (Mouse.current == null)
            return;

        bool dragging = useLeftMouseButton
            ? Mouse.current.leftButton.isPressed
            : Mouse.current.rightButton.isPressed;

        if (!dragging)
            return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        float mouseYaw = -mouseDelta.x * rotationSpeed;
        float mousePitch = -mouseDelta.y * rotationSpeed;

        transform.Rotate(Vector3.up, mouseYaw, Space.World);
        transform.Rotate(Vector3.right, mousePitch, Space.World);
    }
}