using UnityEngine;

public class GroupCameraController : MonoBehaviour
{
    [Header("Camera")]
    public Camera targetCamera;
    public Transform lookTarget;

    [Header("Smoothing")]
    public float moveSpeed = 5f;
    public float rotateSpeed = 5f;
    public float fovSpeed = 6f;

    [Header("Overview")]
    public Vector3 overviewPosition = new Vector3(0f, 1.5f, -12f);
    public float overviewFieldOfView = 85f;

    [Header("Focus")]
    public float focusDistance = 3.25f;
    public float focusFieldOfView = 68f;
    public float closestFocusFieldOfView = 38f;
    public float farthestFocusFieldOfView = 76f;
    public float minimumFocusDistance = 3f;
    public float minimumFocusFieldOfView = 66f;
    public float maximumDefaultFocusDistance = 3.5f;
    public float maximumDefaultFocusFieldOfView = 70f;

    [Header("Orbit")]
    public float orbitSensitivity = 450f;
    public float minOrbitPitch = -25f;
    public float maxOrbitPitch = 65f;

    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private float targetFieldOfView;
    private bool isActive;
    private Vector3 focusPoint;
    private float orbitYaw;
    private float orbitPitch;
    private float orbitDistance;
    private bool hasFocusPoint;

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = GetComponent<Camera>();
        }

        Transform controlledTransform = GetControlledTransform();
        targetPosition = controlledTransform.position;
        targetRotation = controlledTransform.rotation;
        targetFieldOfView = targetCamera != null ? targetCamera.fieldOfView : focusFieldOfView;
    }

    private void Update()
    {
        if (!isActive)
        {
            return;
        }

        Transform controlledTransform = GetControlledTransform();

        controlledTransform.position = Vector3.Lerp(
            controlledTransform.position,
            targetPosition,
            moveSpeed * Time.deltaTime
        );

        controlledTransform.rotation = Quaternion.Slerp(
            controlledTransform.rotation,
            targetRotation,
            rotateSpeed * Time.deltaTime
        );

        if (targetCamera != null)
        {
            targetCamera.fieldOfView = Mathf.Lerp(
                targetCamera.fieldOfView,
                targetFieldOfView,
                fovSpeed * Time.deltaTime
            );
        }
    }

    public void Activate()
    {
        isActive = true;
    }

    public void Deactivate()
    {
        isActive = false;
        hasFocusPoint = false;
    }

    public void ShowOrbitOverview()
    {
        Activate();
        hasFocusPoint = false;
        Vector3 lookAtPoint = GetOverviewLookAtPoint();
        targetPosition = GetOverviewPosition();
        targetRotation = GetLookRotation(lookAtPoint, targetPosition);
        targetFieldOfView = overviewFieldOfView;
    }

    public void FocusGroup(Vector3 groupCenter)
    {
        Activate();
        SetFocusTarget(groupCenter);
        targetFieldOfView = Mathf.Clamp(
            focusFieldOfView,
            minimumFocusFieldOfView,
            maximumDefaultFocusFieldOfView
        );
    }

    public void SetFocusTarget(Vector3 groupCenter)
    {
        Vector3 overviewLookAtPoint = GetOverviewLookAtPoint();
        Vector3 viewDirection = GetControlledTransform().position - overviewLookAtPoint;
        if (viewDirection == Vector3.zero)
        {
            viewDirection = GetOverviewPosition() - overviewLookAtPoint;
        }

        if (viewDirection == Vector3.zero)
        {
            viewDirection = Vector3.back;
        }

        float safeFocusDistance = Mathf.Clamp(
            focusDistance,
            minimumFocusDistance,
            maximumDefaultFocusDistance
        );
        targetPosition = groupCenter + viewDirection.normalized * safeFocusDistance;
        targetRotation = GetLookRotation(groupCenter, targetPosition);
        SetOrbitFromTarget(groupCenter);
    }

    public void SetFocusZoom(float normalizedZoom)
    {
        targetFieldOfView = Mathf.Lerp(
            Mathf.Max(farthestFocusFieldOfView, minimumFocusFieldOfView),
            closestFocusFieldOfView,
            Mathf.Clamp01(normalizedZoom)
        );
    }

    public void OrbitAroundFocus(Vector2 orbitInput)
    {
        if (!hasFocusPoint)
        {
            return;
        }

        orbitYaw -= orbitInput.x * orbitSensitivity;
        orbitPitch += orbitInput.y * orbitSensitivity;
        orbitPitch = ClampOrbitPitch(orbitPitch);
        ApplyOrbitTarget();
    }

    private Quaternion GetLookRotation(Vector3 lookAtPoint, Vector3 fromPosition)
    {
        Vector3 direction = lookAtPoint - fromPosition;
        if (direction == Vector3.zero)
        {
            direction = GetControlledTransform().forward;
        }

        return Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private void SetOrbitFromTarget(Vector3 groupCenter)
    {
        focusPoint = groupCenter;
        Vector3 offset = targetPosition - focusPoint;
        orbitDistance = Mathf.Max(0.01f, offset.magnitude);
        Vector3 orbitDirection = offset.normalized;

        orbitYaw = Mathf.Atan2(orbitDirection.x, orbitDirection.z) * Mathf.Rad2Deg;
        orbitPitch = Mathf.Asin(Mathf.Clamp(orbitDirection.y, -1f, 1f)) * Mathf.Rad2Deg;
        orbitPitch = ClampOrbitPitch(orbitPitch);
        hasFocusPoint = true;
    }

    private void ApplyOrbitTarget()
    {
        float yawRadians = orbitYaw * Mathf.Deg2Rad;
        float pitchRadians = orbitPitch * Mathf.Deg2Rad;
        float horizontalRadius = Mathf.Cos(pitchRadians);
        Vector3 orbitDirection = new Vector3(
            Mathf.Sin(yawRadians) * horizontalRadius,
            Mathf.Sin(pitchRadians),
            Mathf.Cos(yawRadians) * horizontalRadius
        );

        targetPosition = focusPoint + orbitDirection.normalized * orbitDistance;
        targetRotation = GetLookRotation(focusPoint, targetPosition);
    }

    private float ClampOrbitPitch(float pitch)
    {
        float safeMinPitch = Mathf.Max(minOrbitPitch, -25f);
        float safeMaxPitch = Mathf.Min(maxOrbitPitch, 65f);
        return Mathf.Clamp(pitch, safeMinPitch, safeMaxPitch);
    }

    private Vector3 GetOverviewLookAtPoint()
    {
        return lookTarget != null ? lookTarget.position : Vector3.zero;
    }

    private Vector3 GetOverviewPosition()
    {
        return GetOverviewLookAtPoint() + overviewPosition;
    }

    private Transform GetControlledTransform()
    {
        return targetCamera != null ? targetCamera.transform : transform;
    }
}
