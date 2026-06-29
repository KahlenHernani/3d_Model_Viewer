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

    public enum GroupViewState
    {
        OrbitOverview,
        FocusedGroup
    }

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
        public Vector3 displayedCenter;
        public float displayedScale = 1f;
        public readonly List<PartData> parts = new List<PartData>();
    }

    private readonly List<PartData> parts = new List<PartData>();
    private readonly Dictionary<string, GroupData> groupDict = new Dictionary<string, GroupData>();
    private readonly List<GroupData> orderedGroups = new List<GroupData>();

    [Header("Group Carousel")]
    public int selectedGroupIndex = 0;
    public float selectedGroupScale = 0.85f;
    public float unselectedGroupScale = 0.35f;
    public float groupTransitionSpeed = 8f;
    public Camera groupModeCamera;
    public float selectedGroupPullDistance = 0.55f;
    public float satelliteRingRadius = 3f;
    public float satelliteVerticalRadius = 1.45f;
    public float satelliteBackOffset = 0.7f;
    public float beltSpacingMultiplier = 10f;
    public float leftSideSpacingMultiplier = 1.35f;
    public string SelectedGroupLabel { get; private set; } = "";

    private Vector3 modelLocalCenter = Vector3.zero;
    private bool hasGroupLayoutBasis = false;
    private Vector3 groupLayoutWorldCenter;
    private Vector3 groupLayoutCameraDirection;
    private Vector3 groupLayoutRight;
    private Vector3 groupLayoutUp;

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

        modelLocalCenter = Vector3.zero;
        for (int i = 0; i < parts.Count; i++)
        {
            modelLocalCenter += parts[i].originalLocalPosition;
        }

        if (parts.Count > 0)
        {
            modelLocalCenter /= parts.Count;
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
            groupData.displayedCenter = groupCenter;
            groupData.displayedScale = 1f;

            for (int i = 0; i < groupData.parts.Count; i++)
            {
                PartData partData = groupData.parts[i];
                Vector3 direction = partData.originalLocalPosition - groupCenter;
                if (direction == Vector3.zero)
                {
                    direction = Random.onUnitSphere;
                }

                partData.groupLocalOffset = partData.originalLocalPosition - groupCenter;
                partData.direction = direction.normalized;
            }

            orderedGroups.Add(groupData);
        }

        orderedGroups.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
        for (int i = 0; i < orderedGroups.Count; i++)
        {
            orderedGroups[i].index = i;
        }
        UpdateSelectedGroupLabel();

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
        UpdateSelectedGroupLabel();
        ApplyExplosion();
    }

    public void PreviousGroup()
    {
        if (orderedGroups.Count == 0)
        {
            return;
        }

        selectedGroupIndex = (selectedGroupIndex - 1 + orderedGroups.Count) % orderedGroups.Count;
        UpdateSelectedGroupLabel();
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
            float transitionAmount = Application.isPlaying
                ? 1f - Mathf.Exp(-groupTransitionSpeed * Time.deltaTime)
                : 1f;

            for (int groupIndex = 0; groupIndex < orderedGroups.Count; groupIndex++)
            {
                GroupData groupData = orderedGroups[groupIndex];
                bool isSelected = groupIndex == selectedGroupIndex;
                float targetScale = isSelected
                    ? selectedGroupScale
                    : unselectedGroupScale;

                Vector3 targetCenter = GetDisplayedGroupCenter(groupIndex);

                if (isSelected)
                {
                    groupData.displayedCenter = Vector3.Lerp(
                        groupData.displayedCenter,
                        targetCenter,
                        transitionAmount
                    );
                    groupData.displayedScale = Mathf.Lerp(
                        groupData.displayedScale,
                        targetScale,
                        transitionAmount
                    );
                }
                else
                {
                    groupData.displayedCenter = targetCenter;
                    groupData.displayedScale = targetScale;
                }

                foreach (PartData data in groupData.parts)
                {
                    data.part.localPosition =
                        groupData.displayedCenter +
                        data.groupLocalOffset * groupData.displayedScale;
                }
            }
        }
    }

    private Vector3 GetGroupOverviewCenter(int groupIndex)
    {
        int satelliteCount = orderedGroups.Count;
        if (satelliteCount <= 0)
        {
            return Vector3.zero;
        }

        float angle = (groupIndex / (float)satelliteCount) * Mathf.PI * 2f;
        float spacingMultiplier = Mathf.Max(1f, beltSpacingMultiplier);
        float horizontalOffset = Mathf.Cos(angle) * satelliteRingRadius;
        if (horizontalOffset < 0f)
        {
            horizontalOffset *= Mathf.Max(1f, leftSideSpacingMultiplier);
        }

        return GetCameraFacingLocalPoint(
            horizontalOffset * spacingMultiplier,
            Mathf.Sin(angle) * satelliteVerticalRadius * spacingMultiplier,
            -satelliteBackOffset * spacingMultiplier
        );
    }

    private void UpdateSelectedGroupLabel()
    {
        if (orderedGroups.Count == 0)
        {
            SelectedGroupLabel = "";
            return;
        }

        selectedGroupIndex = Mathf.Clamp(selectedGroupIndex, 0, orderedGroups.Count - 1);
        GroupData selectedGroup = orderedGroups[selectedGroupIndex];
        SelectedGroupLabel = $"Group {selectedGroup.index + 1}/{orderedGroups.Count}: {selectedGroup.name}";
        Debug.Log("Selected " + SelectedGroupLabel);
    }

    public Vector3 GetGroupCenter(int index)
    {
        if (orderedGroups.Count == 0)
        {
            return Vector3.zero;
        }

        int safeIndex = Mathf.Clamp(index, 0, orderedGroups.Count - 1);

        return transform.TransformPoint(GetDisplayedGroupCenter(safeIndex));

    }
    public string GetGroupLabel(int index)
    {
        if (orderedGroups.Count == 0)
        {
            return "";
        }

        int safeIndex = Mathf.Clamp(index, 0, orderedGroups.Count - 1);
        GroupData group = orderedGroups[safeIndex];

        return $"Group {group.index + 1}/{orderedGroups.Count}: {group.name}";
    }
    public int SelectedGroupIndex
    {
        get
        {
            return selectedGroupIndex;
        }
    }
    public int GroupCount
    {
        get
        {
            return orderedGroups.Count;
        }
    }

    public void ResetGroupSelection()
    {
        selectedGroupIndex = 0;
        beltSpacingMultiplier = Mathf.Max(10f, beltSpacingMultiplier);
        CaptureGroupLayoutBasis();
        ResetDisplayedGroupLayout();
        UpdateSelectedGroupLabel();
        ApplyExplosion();
    }

    public Vector3 GetDisplayedGroupCenter(int groupIndex)
    {
        if (groupIndex == selectedGroupIndex)
        {
            return GetSelectedGroupCenter();
        }

        return GetGroupOverviewCenter(groupIndex);
    }

    public void SetGroupCamera(Camera camera)
    {
        groupModeCamera = camera;
    }

    private Vector3 GetSelectedGroupCenter()
    {
        return GetCameraFacingLocalPoint(0f, 0f, selectedGroupPullDistance);
    }

    private void ResetDisplayedGroupLayout()
    {
        for (int i = 0; i < orderedGroups.Count; i++)
        {
            orderedGroups[i].displayedCenter = orderedGroups[i].originalCenter;
            orderedGroups[i].displayedScale = 1f;
        }
    }

    private void CaptureGroupLayoutBasis()
    {
        groupLayoutWorldCenter = transform.TransformPoint(modelLocalCenter);

        if (groupModeCamera == null)
        {
            groupLayoutCameraDirection = transform.TransformDirection(Vector3.back);
            groupLayoutRight = transform.TransformDirection(Vector3.right);
            groupLayoutUp = transform.TransformDirection(Vector3.up);
            hasGroupLayoutBasis = true;
            return;
        }

        groupLayoutCameraDirection = groupModeCamera.transform.position - groupLayoutWorldCenter;
        if (groupLayoutCameraDirection == Vector3.zero)
        {
            groupLayoutCameraDirection = -groupModeCamera.transform.forward;
        }

        groupLayoutCameraDirection.Normalize();
        groupLayoutRight = groupModeCamera.transform.right;
        groupLayoutUp = groupModeCamera.transform.up;
        hasGroupLayoutBasis = true;
    }

    private Vector3 GetCameraFacingLocalPoint(float rightOffset, float upOffset, float cameraDirectionOffset)
    {
        if (!hasGroupLayoutBasis)
        {
            CaptureGroupLayoutBasis();
        }

        Vector3 worldPoint =
            groupLayoutWorldCenter +
            groupLayoutCameraDirection * cameraDirectionOffset +
            groupLayoutRight * rightOffset +
            groupLayoutUp * upOffset;

        return transform.InverseTransformPoint(worldPoint);
    }
}
