using System.Collections.Generic;
using UnityEngine;

public enum PlayerMode
{
    None,          
    BuildRoad,
    UpgradeRoad,
    Delete,
    PlaceBuilding,
    DemolishBuilding
}

public class PlayerActionManager : MonoBehaviour
{
    public static PlayerActionManager Instance { get; private set; }

    [Header("Current State")]
    public PlayerMode currentMode = PlayerMode.None;

    [Header("Player Options")]
    public List<RoadType> roadTypes;
    public List<BuildingData> buildingTypes;
    int currentRoadType = 0;
    int currentBuildingType = 0;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        UpdateGridShaderMode();
    }

    private void Update()
    {
        // Quality of Life: Right-click cancels the current tool and returns to 'None'
        //currently disabled because right-click also cancels preview placements on roads
        /*if (Input.GetMouseButtonDown(1) && currentMode != PlayerMode.None)
        {
            SetModeNone();
        }*/
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
        currentRoadType++;
        if(currentRoadType >= roadTypes.Count)
            currentRoadType = 0;
            
        Debug.Log($"Selected Road Type changed to: {SelectedRoadType.roadName}");
    }

    public void ChangeCurrentBuildingType()
    {
        if(currentMode == PlayerMode.PlaceBuilding)
            currentMode = PlayerMode.None;
        
        currentBuildingType++;
        if(currentBuildingType >= buildingTypes.Count)
            currentBuildingType = 0;
    }
    
    public void SetModeNone()
    {
        currentMode = PlayerMode.None;
        Debug.Log("Mode set to: None (Selection)");

        UpdateGridShaderMode();
    }

    public void SetModeBuildRoad()
    {
        currentMode = PlayerMode.BuildRoad;
        Debug.Log("Mode set to: Build Road");

        UpdateGridShaderMode();
    }

    public void SetModeDelete()
    {
        currentMode = PlayerMode.Delete;
        Debug.Log("Mode set to: Delete (Bulldozer)");

        UpdateGridShaderMode();
    }

    public void SetModePlaceBuilding()
    {
        currentMode = PlayerMode.PlaceBuilding;
        Debug.Log("Mode set to: Place Building");

        UpdateGridShaderMode();
    }

    public void SetModeDemolishBuilding() 
    { 
        currentMode = PlayerMode.DemolishBuilding; 
        Debug.Log("Mode: Demolish Building"); 

        UpdateGridShaderMode();
    }

    public void SetModeUpgradeRoad()
    {
        currentMode = PlayerMode.UpgradeRoad;
        Debug.Log("Mode set to: Upgrade Road");

        UpdateGridShaderMode();
    }

    public RoadType SelectedRoadType 
    {
        get { return roadTypes.Count > 0 ? roadTypes[currentRoadType] : null; }
    }

    public BuildingData SelectedBuildingType 
    {
        get { return buildingTypes.Count > 0 ? buildingTypes[currentBuildingType] : null; }
    }
}