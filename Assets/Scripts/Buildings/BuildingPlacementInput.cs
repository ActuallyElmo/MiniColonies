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

    [Header("Port Icons")]
    [Tooltip("Icon used by ports that accept population transport vehicles only.")]
    public Sprite popVehiclePortSprite;
    [Tooltip("Icon used by ports that accept resource transport vehicles only.")]
    public Sprite resourceVehiclePortSprite;
    [Tooltip("Icon used by ports that accept both population and resource transport vehicles.")]
    public Sprite allVehiclesPortSprite;
    [Tooltip("Vertical clearance above the building's highest renderer, measured in grid cells.")]
    [Min(0f)] public float portIconHeightPaddingInCells = 0.1f;

    [Header("Port Flow Colors")]
    public Color entryPortColor = new Color(0.2f, 0.75f, 1f, 1f);
    public Color exitPortColor = new Color(1f, 0.45f, 0.15f, 1f);
    public Color bothPortColor = new Color(0.7f, 0.35f, 1f, 1f);

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
                             currentMode == PlayerMode.DeleteRoad;
            
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
        float portIconLocalHeight = GetPortIconLocalHeight(newObj.transform);
        foreach (BuildingTile tile in data.footprint)
        {
            if (tile.isPort)
            {
                float lx = tile.localPosition.x * worldManager.cellSize;
                float lz = tile.localPosition.y * worldManager.cellSize;
                GameObject portIcon = CreatePortIcon(
                    newObj.transform,
                    tile.portType,
                    lx,
                    portIconLocalHeight,
                    lz);

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
                CreatePortIcon(_previewObj.transform, tile.portType, lx, 0.2f, lz);
            }
        }
    }

    private float GetPortIconLocalHeight(Transform buildingRoot)
    {
        Renderer[] buildingRenderers = buildingRoot.GetComponentsInChildren<Renderer>();
        float highestWorldY = float.NegativeInfinity;

        foreach (Renderer buildingRenderer in buildingRenderers)
        {
            if (!buildingRenderer.enabled) continue;
            highestWorldY = Mathf.Max(highestWorldY, buildingRenderer.bounds.max.y);
        }

        if (float.IsNegativeInfinity(highestWorldY)) return 0.2f;

        float clearance = worldManager.cellSize * portIconHeightPaddingInCells;
        Vector3 pointAboveBuilding = new Vector3(
            buildingRoot.position.x,
            highestWorldY + clearance,
            buildingRoot.position.z);
        return buildingRoot.InverseTransformPoint(pointAboveBuilding).y;
    }

    private GameObject CreatePortIcon(
        Transform parent,
        PortType portType,
        float localX,
        float localY,
        float localZ)
    {
        GameObject portIcon = new GameObject($"PortIcon_{portType}");
        portIcon.transform.SetParent(parent, false);
        portIcon.transform.localPosition = new Vector3(localX, localY, localZ);
        portIcon.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        SpriteRenderer renderer = portIcon.AddComponent<SpriteRenderer>();
        renderer.sprite = GetPortSprite(portType);
        renderer.color = GetPortColor(portType);
        renderer.sortingOrder = 10;

        if (renderer.sprite != null)
        {
            Vector2 spriteSize = renderer.sprite.bounds.size;
            float largestDimension = Mathf.Max(spriteSize.x, spriteSize.y);
            if (largestDimension > 0f)
            {
                float scale = worldManager.cellSize * 0.6f / largestDimension;
                portIcon.transform.localScale = new Vector3(scale, scale, 1f);
            }
        }

        return portIcon;
    }

    private Sprite GetPortSprite(PortType portType)
    {
        switch (portType)
        {
            case PortType.PopEntry:
            case PortType.PopExit:
            case PortType.PopBoth:
                return popVehiclePortSprite;
            case PortType.ResourceEntry:
            case PortType.ResourceExit:
            case PortType.ResourceBoth:
                return resourceVehiclePortSprite;
            default:
                return allVehiclesPortSprite;
        }
    }

    private Color GetPortColor(PortType portType)
    {
        switch (portType)
        {
            case PortType.Entry:
            case PortType.PopEntry:
            case PortType.ResourceEntry:
                return entryPortColor;
            case PortType.Exit:
            case PortType.PopExit:
            case PortType.ResourceExit:
                return exitPortColor;
            default:
                return bothPortColor;
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
