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

    public bool useKeyboardControls = true;
    private TankHandController.InteractionMode currentMode = TankHandController.InteractionMode.Explode;

    private class PartData
    {
        public Transform part;
        public Vector3 originalLocalPosition;
        public Vector3 direction;
    }

    private readonly List<PartData> parts = new List<PartData>();
    private readonly Dictionary<string, List<PartData>> groupDict = new Dictionary<string, List<PartData>>();

    void Start()
    {
        parts.Clear();

        foreach (Transform child in transform)
        {

            string groupName = child.name.Split('_')[0];
            PartData data = new PartData();
            data.part = child;
            data.originalLocalPosition = child.localPosition;

            Vector3 dir = child.localPosition;

            if (dir == Vector3.zero)
            {
                dir = Random.onUnitSphere;
            }

            data.direction = dir.normalized;
            if (!groupDict.ContainsKey(groupName))
            {
                groupDict[groupName] = new List<PartData>();
            }
            groupDict[groupName].Add(data);

            parts.Add(data);
        }

        ApplyExplosion();
    }

    void Update()
    {
        if (!useKeyboardControls)
            return;

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

    public void SetExplodeAmount(float value, TankHandController.InteractionMode mode)
    {
        explodeAmount = Mathf.Clamp01(value);
        currentMode = mode;
        ApplyExplosion();
    }

    private void ApplyExplosion()
    {
        if(currentMode == TankHandController.InteractionMode.Explode)
        {
            foreach (PartData data in parts)
            {
                data.part.localPosition =
                data.originalLocalPosition +
                data.direction * explodeAmount * explodeDistance;
            }
            
        }
        else if(currentMode == TankHandController.InteractionMode.Group)
        {
            int groupIndex = 0;
            foreach (var groupEntry in groupDict)
            {
                float angle = (groupIndex / (float)groupDict.Count) * Mathf.PI * 2f;
                Vector3 groupCenter = new Vector3(Mathf.Cos(angle) * explodeAmount * explodeDistance, 0, Mathf.Sin(angle)*explodeAmount * explodeDistance);

                int partIndex = 0;

                foreach (PartData data in groupEntry.Value)
                {
                    float partAngle = (partIndex / (float)groupEntry.Value.Count) * Mathf.PI*2f;
                    Vector3 offset = new Vector3(Mathf.Cos(partAngle) * explodeAmount*0.5f, 0, Mathf.Sin(partAngle)*explodeAmount * 0.5f);

                    data.part.localPosition = data.originalLocalPosition + groupCenter + offset;
                    partIndex++;
                }
                groupIndex++;
            }
            
        }
    }
}