using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class BuildingPlacementInput : MonoBehaviour
{
    [Header("Dependencies")]
    public WorldManager worldManager;
    
    [Header("Visual Preview Settings")]
    public Material basePreviewMaterial; 
    public Color validCellColor = new Color(0f, 1f, 0f, 0.5f); 
    public Color invalidCellColor = new Color(1f, 0f, 0f, 0.5f); 

    [Header("Port Materials")]
    public Material entryPortMaterial;
    public Material exitPortMaterial;
    public Material bothPortMaterial;

    // Preview State
    private GameObject _previewObj;
    private int _currentRotationSteps = 0;
    private BuildingData _lastSelectedBuilding;
    private PlayerMode? _lastKnownMode = null; 

    private class PreviewTile
    {
        public MeshRenderer renderer;
        public Color baseColor; 
    }
    private List<PreviewTile> _previewTiles = new List<PreviewTile>();

    private void Update()
    {
        if (PlayerActionManager.Instance == null || BuildingSystemBackend.Instance == null) return;

        PlayerMode currentMode = PlayerActionManager.Instance.currentMode;
        HandleGlobalPortVisibility(currentMode);

        if (currentMode == PlayerMode.PlaceBuilding)
        {
            HandlePreviewAndPlacement();
        }
        else
        {
            if (_previewObj != null) DestroyPreview();

            if (currentMode == PlayerMode.DemolishBuilding && Input.GetMouseButtonDown(0))
            {
                HandleDemolishInput();
            }
        }
    }

    private void HandleGlobalPortVisibility(PlayerMode currentMode)
    {
        if (_lastKnownMode == null || _lastKnownMode.Value != currentMode)
        {
            _lastKnownMode = currentMode;
            bool showPorts = currentMode == PlayerMode.PlaceBuilding || 
                             currentMode == PlayerMode.BuildRoad || 
                             currentMode == PlayerMode.UpgradeRoad || 
                             currentMode == PlayerMode.DemolishBuilding || 
                             currentMode == PlayerMode.Delete;
            
            foreach (Building b in BuildingSystemBackend.Instance.GetActiveBuildings())
            {
                if (b != null) b.SetPortIconsVisibility(showPorts);
            }
        }
    }

    private void HandleDemolishInput()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        Vector2Int? targetCell = GetCellUnderMouse();
        if (targetCell.HasValue)
        {
            BuildingSystemBackend.Instance.TryDemolishBuilding(targetCell.Value);
        }
    }

    private void HandlePreviewAndPlacement()
    {
        BuildingData selectedBuilding = PlayerActionManager.Instance.SelectedBuildingType;
        
        if (selectedBuilding == null)
        {
            if (_previewObj != null) DestroyPreview();
            return;
        }

        if (_previewObj == null || _lastSelectedBuilding != selectedBuilding)
        {
            CreatePreview(selectedBuilding);
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            _currentRotationSteps = (_currentRotationSteps + 1) % 4;
            _previewObj.transform.rotation = Quaternion.Euler(0, _currentRotationSteps * 90f, 0); 
        }

        Vector2Int? targetCell = GetCellUnderMouse();
        
        if (targetCell.HasValue)
        {
            if (!_previewObj.activeSelf) _previewObj.SetActive(true);

            float x = targetCell.Value.x * worldManager.cellSize + (worldManager.cellSize * 0.5f);
            float z = targetCell.Value.y * worldManager.cellSize + (worldManager.cellSize * 0.5f);
            float y = worldManager.GetPhysicalHeight(targetCell.Value.x + 0.5f, targetCell.Value.y + 0.5f) * worldManager.heightStep;
            
            _previewObj.transform.position = new Vector3(x, y, z);

            List<BuildingTile> rotatedFootprint = selectedBuilding.GetRotatedFootprint(_currentRotationSteps);
            bool isValid = CanPlaceBuilding(targetCell.Value, rotatedFootprint, out int requiredLayer);

            foreach (PreviewTile pt in _previewTiles)
            {
                pt.renderer.sharedMaterial.color = isValid ? pt.baseColor : invalidCellColor;
            }

            if (Input.GetMouseButtonDown(0) && isValid)
            {
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
                InstantiateAndRegisterBuilding(targetCell.Value, requiredLayer, rotatedFootprint, selectedBuilding);
            }
        }
        else
        {
            if (_previewObj.activeSelf) _previewObj.SetActive(false);
        }
    }

    private void InstantiateAndRegisterBuilding(Vector2Int origin, int requiredLayer, List<BuildingTile> footprint, BuildingData data)
    {
        GameObject newObj = Instantiate(data.prefab, transform);
        
        float x = origin.x * worldManager.cellSize + (worldManager.cellSize * 0.5f);
        float z = origin.y * worldManager.cellSize + (worldManager.cellSize * 0.5f);
        float y = worldManager.GetPhysicalHeight(origin.x + 0.5f, origin.y + 0.5f) * worldManager.heightStep;
        
        newObj.transform.position = new Vector3(x, y, z);
        newObj.transform.rotation = Quaternion.Euler(0, _currentRotationSteps * 90f, 0);

        Building building = newObj.AddComponent<Building>();
        building.Initialize(data, origin, requiredLayer, footprint);

        // Render specific port indicators visually on the object
        foreach (BuildingTile tile in data.footprint)
        {
            if (tile.isPort)
            {
                GameObject portIcon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(portIcon.GetComponent<Collider>());
                portIcon.transform.SetParent(newObj.transform);
                
                float lx = tile.localPosition.x * worldManager.cellSize;
                float lz = tile.localPosition.y * worldManager.cellSize;
                portIcon.transform.localPosition = new Vector3(lx, 0.2f, lz); 
                portIcon.transform.localScale = new Vector3(worldManager.cellSize * 0.6f, worldManager.cellSize * 0.6f, worldManager.cellSize * 0.6f);

                Material iconMat = tile.portType == PortType.Entry ? entryPortMaterial : 
                                   (tile.portType == PortType.Exit ? exitPortMaterial : bothPortMaterial);
                portIcon.GetComponent<MeshRenderer>().sharedMaterial = iconMat;

                building.RegisterPortIcon(portIcon);
            }
        }

        // Pass to pure data layer
        BuildingSystemBackend.Instance.RegisterBuilding(building, footprint);

        bool showPorts = PlayerActionManager.Instance.currentMode == PlayerMode.PlaceBuilding || PlayerActionManager.Instance.currentMode == PlayerMode.BuildRoad;
        building.SetPortIconsVisibility(showPorts);
    }

    private bool CanPlaceBuilding(Vector2Int origin, List<BuildingTile> footprint, out int requiredLayer)
    {
        requiredLayer = worldManager.GetCellLayer(origin.x, origin.y);
        if (!worldManager.IsLayerBuildable(requiredLayer)) return false;

        foreach (var tile in footprint)
        {
            Vector2Int checkPos = origin + tile.localPosition;

            if (worldManager.IsExtrusionCell(checkPos.x, checkPos.y)) return false;
            if (worldManager.IsTransition(checkPos.x, checkPos.y)) return false;
            if (worldManager.GetCellLayer(checkPos.x, checkPos.y) != requiredLayer) return false;
            
            // Query the Backends
            if (BuildingSystemBackend.Instance.IsCellOccupied(checkPos)) return false;
            if (RoadSystemBackend.Instance != null && RoadSystemBackend.Instance.Roads.ContainsKey(checkPos)) return false;
        }

        return true;
    }

    private void CreatePreview(BuildingData data)
    {
        if (_previewObj != null) DestroyPreview();

        _lastSelectedBuilding = data;
        _currentRotationSteps = 0; 
        _previewTiles.Clear();

        _previewObj = new GameObject("PreviewGrid_" + data.buildingName);

        foreach (BuildingTile tile in data.footprint)
        {
            GameObject tileObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(tileObj.GetComponent<Collider>()); 
            
            tileObj.transform.SetParent(_previewObj.transform);
            tileObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            
            float lx = tile.localPosition.x * worldManager.cellSize;
            float lz = tile.localPosition.y * worldManager.cellSize;
            tileObj.transform.localPosition = new Vector3(lx, 0.1f, lz); 
            tileObj.transform.localScale = new Vector3(worldManager.cellSize * 0.9f, worldManager.cellSize * 0.9f, 1f);

            MeshRenderer rend = tileObj.GetComponent<MeshRenderer>();
            Material matInstance = new Material(basePreviewMaterial);
            rend.sharedMaterial = matInstance;

            _previewTiles.Add(new PreviewTile { renderer = rend, baseColor = validCellColor });

            if (tile.isPort)
            {
                GameObject portIcon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(portIcon.GetComponent<Collider>());
                portIcon.transform.SetParent(_previewObj.transform);
                portIcon.transform.localScale = new Vector3(worldManager.cellSize * 0.6f, worldManager.cellSize * 0.6f, worldManager.cellSize * 0.6f);
                portIcon.transform.localPosition = new Vector3(lx, 0.2f, lz); 

                Material iconMat = tile.portType == PortType.Entry ? entryPortMaterial : 
                                   (tile.portType == PortType.Exit ? exitPortMaterial : bothPortMaterial);
                portIcon.GetComponent<MeshRenderer>().sharedMaterial = iconMat;
            }
        }
    }

    private void DestroyPreview()
    {
        if (_previewObj != null) Destroy(_previewObj);
        foreach (var pt in _previewTiles)
        {
            if (pt.renderer != null && pt.renderer.sharedMaterial != null) Destroy(pt.renderer.sharedMaterial);
        }
        _previewTiles.Clear();
        _lastSelectedBuilding = null;
    }

    private Vector2Int? GetCellUnderMouse()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, 1 << LayerMask.NameToLayer("Terrain")))
        {
            return new Vector2Int(
                Mathf.FloorToInt(hit.point.x / worldManager.cellSize),
                Mathf.FloorToInt(hit.point.z / worldManager.cellSize)
            );
        }
        return null;
    }
}