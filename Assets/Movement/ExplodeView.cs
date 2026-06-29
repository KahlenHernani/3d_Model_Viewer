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
        public Vector3 groupLocalOffset;
    }

    private class GroupData
    {
        public string name;
        public int index;
        public Vector3 originalCenter;
        public readonly List<PartData> parts = new List<PartData>();
    }

    private readonly List<PartData> parts = new List<PartData>();
    private readonly Dictionary<string, GroupData> groupDict = new Dictionary<string, GroupData>();
    private readonly List<GroupData> orderedGroups = new List<GroupData>();

    [Header("Group Carousel")]
    public int selectedGroupIndex = 0;
    public float groupSpacing = 0.25f;
    public float selectedGroupScale = 1.4f;
    public float unselectedGroupScale = 0.75f;
    public float groupGlobeRadius = 0.04f;
    public float selectedGroupForwardOffset = -0.08f;

    void Start()
    {
        parts.Clear();
        groupDict.Clear();
        orderedGroups.Clear();

        foreach (Transform child in transform)
        {
            string groupName = child.name.Split('_')[0];
            PartData data = new PartData();
            data.part = child;
            data.originalLocalPosition = child.localPosition;
            if (!groupDict.ContainsKey(groupName))
            {
                groupDict[groupName] = new GroupData { name = groupName };
            }
            groupDict[groupName].parts.Add(data);

            parts.Add(data);
        }

        foreach (KeyValuePair<string, GroupData> entry in groupDict)
        {
            GroupData groupData = entry.Value;
            Vector3 groupCenter = Vector3.zero;

            for (int i = 0; i < groupData.parts.Count; i++)
            {
                groupCenter += groupData.parts[i].originalLocalPosition;
            }

            if (groupData.parts.Count > 0)
            {
                groupCenter /= groupData.parts.Count;
            }

            groupData.originalCenter = groupCenter;

            for (int i = 0; i < groupData.parts.Count; i++)
            {
                PartData partData = groupData.parts[i];
                Vector3 direction = partData.originalLocalPosition - groupCenter;
                partData.groupLocalOffset = Random.onUnitSphere * groupGlobeRadius;

                if (direction == Vector3.zero)
                {
                    direction = Random.onUnitSphere;
                }
                partData.direction = direction.normalized;
            }

            orderedGroups.Add(groupData);
        }

        orderedGroups.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
        for (int i = 0; i < orderedGroups.Count; i++)
        {
            orderedGroups[i].index = i;
        }

        ApplyExplosion();
    }
    // For group mode
    public void NextGroup()
    {
        if (orderedGroups.Count == 0)
        {
            return;
        }

        // Cycle through the groups in a circular manner
        selectedGroupIndex = (selectedGroupIndex + 1) % orderedGroups.Count;
        ApplyExplosion();
    }

    public void PreviousGroup()
    {
        if (orderedGroups.Count == 0)
        {
            return;
        }

        selectedGroupIndex = (selectedGroupIndex - 1 + orderedGroups.Count) % orderedGroups.Count;
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
        if (currentMode == TankHandController.InteractionMode.Zoom)
        {
            foreach (PartData data in parts)
            {
                data.part.localPosition = data.originalLocalPosition;
            }
            return;
        }

        if (currentMode == TankHandController.InteractionMode.Explode)
        {
            foreach (PartData data in parts)
            {
                data.part.localPosition =
                data.originalLocalPosition +
                data.direction * explodeAmount * explodeDistance;
            }
            return;
        }

        if (currentMode == TankHandController.InteractionMode.Group)
        {
            int groupIndex = 0;
            foreach (GroupData groupData in orderedGroups)
            {
                int offsetFromSelected = groupIndex - selectedGroupIndex;

                if (offsetFromSelected > orderedGroups.Count / 2)
                {
                    offsetFromSelected -= orderedGroups.Count;
                }
                else if (offsetFromSelected < -orderedGroups.Count / 2)
                {
                    offsetFromSelected += orderedGroups.Count;
                }

                float scale = groupIndex == selectedGroupIndex
                    ? selectedGroupScale
                    : unselectedGroupScale;

                Vector3 groupCenter = new Vector3(
                    offsetFromSelected * groupSpacing,
                    0f,
                    groupIndex == selectedGroupIndex ? selectedGroupForwardOffset : 0f
                );

                foreach (PartData data in groupData.parts)
                {
                    data.part.localPosition = groupCenter + data.groupLocalOffset * scale;
                }

                groupIndex++;
            }
        }
    }
}
