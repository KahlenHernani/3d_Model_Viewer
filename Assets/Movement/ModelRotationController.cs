using UnityEngine;
using UnityEngine.InputSystem;

public class DragRotateModel : MonoBehaviour
{
    [Header("Rotation")]
    [SerializeField] private float rotationSpeed = 100f;
    [SerializeField] private float rotationDamping = 4f;
    [SerializeField] private bool useLeftMouseButton = true;

    private Vector2 rotationalVelocity;

    public void SetRotationInput(Vector2 input)
    {
        rotationalVelocity += input * 3f;
    }

    private void Update()
    {
        // Apply inertia every frame
        transform.Rotate(
            Vector3.up,
            -rotationalVelocity.x * rotationSpeed * Time.deltaTime,
            Space.World
        );

        transform.Rotate(
            Vector3.right,
            rotationalVelocity.y * rotationSpeed * Time.deltaTime,
            Space.World
        );

        // Smooth deceleration
        rotationalVelocity = Vector2.Lerp(
            rotationalVelocity,
            Vector2.zero,
            rotationDamping * Time.deltaTime
        );

        // Mouse fallback
        if (Mouse.current == null)
            return;

        bool dragging = useLeftMouseButton
            ? Mouse.current.leftButton.isPressed
            : Mouse.current.rightButton.isPressed;

        if (!dragging)
            return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        rotationalVelocity += mouseDelta * 0.5f;
    }
}