using UnityEngine;

/// <summary>
/// Base component for a building information panel.
/// Derive a housing, industrial, or other panel from this class and use
/// OnBuildingInitialized to populate its controls.
/// </summary>
public class BuildingInfoPanel : MonoBehaviour
{
    public Building CurrentBuilding { get; private set; }
    public BuildingData CurrentBuildingData =>
        CurrentBuilding != null ? CurrentBuilding.data : null;

    public void Initialize(Building building)
    {
        CurrentBuilding = building;
        OnBuildingInitialized(building);
    }

    public void Clear()
    {
        Building previousBuilding = CurrentBuilding;
        CurrentBuilding = null;
        OnBuildingCleared(previousBuilding);
    }

    public T GetBuildingData<T>() where T : BuildingData
    {
        return CurrentBuildingData as T;
    }

    protected virtual void OnBuildingInitialized(Building building)
    {
    }

    protected virtual void OnBuildingCleared(Building previousBuilding)
    {
    }
}
