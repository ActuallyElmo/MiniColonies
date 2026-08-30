using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public enum PlayerMode
{
    None,          
    BuildRoad,
    UpgradeRoad,
    DeleteRoad,
    PlaceBuilding,
    DemolishBuilding
}

public class PlayerActionManager : MonoBehaviour
{
    public static PlayerActionManager Instance { get; private set; }

    [Header("Current State")]
    public PlayerMode currentMode = PlayerMode.None;

    [Header("Building Selection")]
    [SerializeField] private Camera buildingSelectionCamera;
    [SerializeField] private LayerMask buildingSelectionMask = ~0;
    [SerializeField, Min(0.01f)] private float buildingSelectionDistance = 1000f;

    [Header("Player Options")]
    public List<RoadType> roadTypes;
    public List<BuildingData> buildingTypes;
    int currentRoadType = 0;
    int currentBuildingType = 0;

    public event Action<PlayerMode> ModeChanged;
    public event Action<Building> BuildingSelectionChanged;

    public Building SelectedBuilding { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        currentRoadType = FindNextSelectableRoadTypeIndex(currentRoadType);
        UpdateGridShaderMode();
    }

    private void OnDestroy()
    {
        if (Instance != this) return;

        Shader.SetGlobalFloat("_ShowColoredGrid", 0f);
        Instance = null;
    }

    private void Update()
    {
        HandleBuildingSelectionInput();

        if (!Input.GetMouseButtonDown(1)) return;

        // Vehicle dragging gets first refusal so right-click cancels only the
        // drag preview and leaves the currently inspected building selected.
        if (PopTransportVehicleDragSource.TryCancelActiveDrag())
        {
            return;
        }

        if (currentMode == PlayerMode.None)
        {
            if (SelectedBuilding != null)
            {
                SelectedBuilding = null;
                BuildingSelectionChanged?.Invoke(null);
            }

            return;
        }

        // An in-progress road transaction gets first refusal so this click only
        // rolls back the preview and leaves the selected road tool active.
        if (RoadBuilderInput.Instance != null &&
            RoadBuilderInput.Instance.TryCancelActivePlacement())
        {
            return;
        }

        if (currentMode != PlayerMode.None)
        {
            SetModeNone();
        }
    }

    private void HandleBuildingSelectionInput()
    {
        if (currentMode != PlayerMode.None || !Input.GetMouseButtonDown(0))
        {
            return;
        }

        if (EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        Camera selectionCamera =
            buildingSelectionCamera != null ? buildingSelectionCamera : Camera.main;
        if (selectionCamera == null)
        {
            return;
        }

        Ray selectionRay =
            selectionCamera.ScreenPointToRay(Input.mousePosition);

        Building selectedBuilding = null;
        float closestBuildingDistance = float.MaxValue;
        RaycastHit[] hits = Physics.RaycastAll(
                selectionRay,
                buildingSelectionDistance,
                buildingSelectionMask,
                QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            Building candidate =
                hits[i].collider.GetComponentInParent<Building>();
            if (candidate != null &&
                hits[i].distance < closestBuildingDistance)
            {
                selectedBuilding = candidate;
                closestBuildingDistance = hits[i].distance;
            }
        }

        // Publish every valid world click so a panel that was manually closed
        // can be reopened by clicking the same building again.
        SelectedBuilding = selectedBuilding;
        BuildingSelectionChanged?.Invoke(SelectedBuilding);
    }

    private void UpdateGridShaderMode()
    {
        // Define which modes should trigger the colorful terrain analysis grid
        bool isBuildingMode = currentMode == PlayerMode.PlaceBuilding || 
                              currentMode == PlayerMode.BuildRoad ||
                              currentMode == PlayerMode.UpgradeRoad;
                              
        // 1 = True (Colorful Map), 0 = False (Standard GridColor)
        Shader.SetGlobalFloat("_ShowColoredGrid", isBuildingMode ? 1f : 0f);
    }

    public void ChangeCurrentRoadType()
    {
        if (roadTypes == null || roadTypes.Count == 0)
        {
            Debug.Log("No road types are configured.");
            return;
        }

        int nextRoadType = FindNextSelectableRoadTypeIndex(currentRoadType + 1);
        if (nextRoadType >= 0)
        {
            currentRoadType = nextRoadType;
            Debug.Log($"Selected Road Type changed to: {SelectedRoadType.roadName}");
        }
        else
        {
            Debug.Log("No enabled road types are configured.");
        }
    }

    public void ChangeCurrentBuildingType()
    {
        if (buildingTypes == null || buildingTypes.Count == 0)
        {
            currentBuildingType = 0;
            return;
        }

        if (currentMode == PlayerMode.PlaceBuilding)
        {
            SetModeNone();
        }
        
        currentBuildingType++;
        if (currentBuildingType >= buildingTypes.Count)
            currentBuildingType = 0;
    }
    
    public void SetModeNone()
    {
        SetMode(PlayerMode.None, "Mode set to: None (Selection)");
    }

    public void SetModeBuildRoad()
    {
        SetMode(PlayerMode.BuildRoad, "Mode set to: Build Road");
    }

    public void SetModeDelete()
    {
        SetMode(PlayerMode.DeleteRoad, "Mode set to: Delete (Bulldozer)");
    }

    public void SetModePlaceBuilding()
    {
        SetMode(PlayerMode.PlaceBuilding, "Mode set to: Place Building");
    }

    public void SetModeDemolishBuilding() 
    { 
        SetMode(PlayerMode.DemolishBuilding, "Mode: Demolish Building");
    }

    public void SetModeUpgradeRoad()
    {
        SetMode(PlayerMode.UpgradeRoad, "Mode set to: Upgrade Road");
    }

    private void SetMode(PlayerMode newMode, string logMessage)
    {
        bool changed = currentMode != newMode;
        currentMode = newMode;
        Debug.Log(logMessage);
        UpdateGridShaderMode();

        if (changed)
        {
            if (currentMode != PlayerMode.None && SelectedBuilding != null)
            {
                SelectedBuilding = null;
                BuildingSelectionChanged?.Invoke(null);
            }

            ModeChanged?.Invoke(currentMode);
        }
    }

    public RoadType SelectedRoadType 
    {
        get
        {
            int selectableIndex = FindNextSelectableRoadTypeIndex(currentRoadType);
            if (selectableIndex < 0) return null;

            currentRoadType = selectableIndex;
            return roadTypes[currentRoadType];
        }
    }

    public BuildingData SelectedBuildingType 
    {
        get
        {
            if (buildingTypes == null || buildingTypes.Count == 0) return null;

            currentBuildingType = Mathf.Clamp(
                currentBuildingType,
                0,
                buildingTypes.Count - 1);
            return buildingTypes[currentBuildingType];
        }
    }

    private int FindNextSelectableRoadTypeIndex(int startIndex)
    {
        if (roadTypes == null || roadTypes.Count == 0) return -1;

        int count = roadTypes.Count;
        int normalizedStart = startIndex % count;
        if (normalizedStart < 0) normalizedStart += count;

        for (int offset = 0; offset < count; offset++)
        {
            int candidate = (normalizedStart + offset) % count;
            if (IsRoadTypeSelectable(roadTypes[candidate]))
            {
                return candidate;
            }
        }

        return -1;
    }

    private static bool IsRoadTypeSelectable(RoadType roadType)
    {
        return roadType != null;
    }
}
