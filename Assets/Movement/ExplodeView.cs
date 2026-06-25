using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class ExplodeView : MonoBehaviour
{
    [Header("Explosion Control")]
    [Range(0f, 1f)]
    public float explodeAmount = 0f;

    public float explodeDistance = 2f;
    public float explodeSpeed = 0.5f;

    private class PartData
    {
        public Transform part;
        public Vector3 originalLocalPosition;
        public Vector3 direction;
    }

    private readonly List<PartData> parts = new List<PartData>();

    void Start()
    {
        parts.Clear();

        foreach (Transform child in transform)
        {
            PartData data = new PartData();
            data.part = child;
            data.originalLocalPosition = child.localPosition;

            Vector3 dir = child.localPosition;

            if (dir == Vector3.zero)
            {
                dir = Random.onUnitSphere;
            }

            data.direction = dir.normalized;
            parts.Add(data);
        }

        ApplyExplosion();
    }

    void Update()
    {
        if (Keyboard.current == null)
            return;

        if (Keyboard.current.rightArrowKey.isPressed || Keyboard.current.upArrowKey.isPressed)
        {
            explodeAmount += explodeSpeed * Time.deltaTime;
        }

        if (Keyboard.current.leftArrowKey.isPressed || Keyboard.current.downArrowKey.isPressed)
        {
            explodeAmount -= explodeSpeed * Time.deltaTime;
        }

        explodeAmount = Mathf.Clamp01(explodeAmount);

        ApplyExplosion();
    }

    private void ApplyExplosion()
    {
        foreach (PartData data in parts)
        {
            data.part.localPosition =
                data.originalLocalPosition + data.direction * explodeAmount * explodeDistance;
        }
    }
}