using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class BuildingPlacementInput : MonoBehaviour
{
    [Header("Dependencies")]
    public WorldManager worldManager;
    
    [Header("Visual Preview Settings")]
    [HideInInspector] public Material basePreviewMaterial; // Retained for serialized compatibility.
    [Tooltip("Diagonal hatch tile from BuildingPlacement.png.")]
    public Sprite placementHatchSprite;
    [Tooltip("Top outline edge tile from BuildingPlacement.png.")]
    public Sprite placementOutlineSprite;
    [Tooltip("Full-cell filler with a center gap, used for optional ground permissions.")]
    public Sprite optionalGroundFillSprite;
    [Tooltip("Full-cell filler with a center gap, used for required ground permissions.")]
    public Sprite requiredGroundFillSprite;
    [Tooltip("Seamless square of pixels inside the hatch sprite (the supplied pattern repeats every 6 pixels).")]
    [Min(1)] public int hatchRepeatPixels = 12;
    public Color outlineColor = Color.white;
    public Color validCellColor = new Color(1f, 1f, 1f, 0.5f);
    public Color invalidCellColor = new Color(1f, 0f, 0f, 0.5f); 

    [Header("Terrain Tile Icons (shared by all footprints)")]
    public Sprite flatGroundTileIcon;
    public Sprite edgeTileIcon;
    public Sprite waterLevelTileIcon;

    [Header("Building Range Preview")]
    public Material rangeHighlightMaterial;
    public Sprite rangeInnerCornerSprite;
    [HideInInspector] public Color invalidRangeColor = new Color(1f, 0.2f, 0.2f, 1f); // Legacy serialized setting.
    [Min(0f)] public float rangeOutlineHeightOffset = 0.04f;

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
        public Mesh mesh;
        public Color hatchColor;
        public Color outlineColor;
    }
    private List<PreviewTile> _previewTiles = new List<PreviewTile>();
    private readonly List<Mesh> _groundIconMeshes = new List<Mesh>();
    private List<BuildingTile> _rotatedFootprint;
    private bool[] _cellValidity;
    private Material _previewMaterial;
    private MaterialPropertyBlock _previewColors;
    private static readonly int HatchColorId = Shader.PropertyToID("_Color");
    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    private readonly BuildingRangeDisplay _rangeDisplay = new BuildingRangeDisplay();

    private void OnDisable() => CancelPlacementPreview();

    private void OnValidate()
    {
        hatchRepeatPixels = Mathf.Max(1, hatchRepeatPixels);
        portIconHeightPaddingInCells = Mathf.Max(0f, portIconHeightPaddingInCells);
        rangeOutlineHeightOffset = Mathf.Max(0f, rangeOutlineHeightOffset);
    }

    private void Update()
    {
        if (!PlayerViewModeManager.AllowsGameplayActions)
        {
            CancelPlacementPreview();
            return;
        }

        if (PlayerActionManager.Instance == null || BuildingSystemBackend.Instance == null || worldManager == null)
        {
            CancelPlacementPreview();
            return;
        }

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

    public void CancelPlacementPreview()
    {
        if (_previewObj != null) DestroyPreview();
        if (BuildingSystemBackend.Instance != null)
            HandleGlobalPortVisibility(PlayerMode.None);
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
            if (_previewObj == null) return;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            _currentRotationSteps = (_currentRotationSteps + 1) % 4;
            _previewObj.transform.rotation = Quaternion.Euler(0, _currentRotationSteps * 90f, 0); 
            _rotatedFootprint = selectedBuilding.GetRotatedFootprint(_currentRotationSteps);
        }

        Vector2Int? targetCell = GetCellUnderMouse();
        
        if (targetCell.HasValue)
        {
            if (!_previewObj.activeSelf) _previewObj.SetActive(true);

            float x = targetCell.Value.x * worldManager.cellSize + (worldManager.cellSize * 0.5f);
            float z = targetCell.Value.y * worldManager.cellSize + (worldManager.cellSize * 0.5f);
            _previewObj.transform.position = new Vector3(x, _previewObj.transform.position.y, z);

            bool isValid = UpdatePreviewValidity(targetCell.Value, out int requiredLayer);

            if (Input.GetMouseButtonDown(0) && isValid)
            {
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
                InstantiateAndRegisterBuilding(targetCell.Value, requiredLayer, _rotatedFootprint, selectedBuilding);
            }
        }
        else
        {
            if (_previewObj.activeSelf) _previewObj.SetActive(false);
            ClearRangePreview();
        }
    }

    private bool UpdatePreviewValidity(Vector2Int origin, out int requiredLayer)
    {
        RefreshRangePreview(origin);
        bool isValid = BuildingSystemBackend.Instance.CanPlaceBuildingWithRanges(
            worldManager, _lastSelectedBuilding, origin, _rotatedFootprint, _cellValidity,
            _rangeDisplay.Evaluation, out requiredLayer);
        bool rangesValid = BuildingSystemBackend.Instance.AreRangeRequirementsSatisfied(
            worldManager, _lastSelectedBuilding, origin, _rotatedFootprint, _rangeDisplay.Evaluation);
        Vector3 previewPosition = _previewObj.transform.position;
        previewPosition.y = requiredLayer * worldManager.heightStep;
        _previewObj.transform.position = previewPosition;
        for (int i = 0; i < _previewTiles.Count; i++)
        {
            PreviewTile pt = _previewTiles[i];
            bool cellValid = _cellValidity[i] && rangesValid;
            Color hatch = cellValid ? validCellColor : invalidCellColor;
            Color outline = cellValid ? outlineColor :
                new Color(invalidCellColor.r, invalidCellColor.g, invalidCellColor.b, outlineColor.a);
            if (pt.hatchColor == hatch && pt.outlineColor == outline) continue;
            _previewColors.SetColor(HatchColorId, hatch);
            _previewColors.SetColor(OutlineColorId, outline);
            pt.renderer.SetPropertyBlock(_previewColors);
            pt.hatchColor = hatch;
            pt.outlineColor = outline;
        }
        return isValid;
    }

    private void RefreshRangePreview(Vector2Int origin)
    {
        if (worldManager.IsGenerating || _lastSelectedBuilding.ranges == null || _lastSelectedBuilding.ranges.Count == 0)
        {
            ClearRangePreview();
            return;
        }
        BuildingRangeEvaluation current = _rangeDisplay.Evaluation;
        if (current != null && current.MatchesPlacement(worldManager, _lastSelectedBuilding, origin, _rotatedFootprint)) return;
        ClearRangePreview();
        BuildingRangeEvaluation evaluation = BuildingSystemBackend.Instance.RequestPlacementRanges(
            worldManager, _lastSelectedBuilding, origin, _rotatedFootprint);
        _rangeDisplay.Show(evaluation, worldManager.GetComponent<WorldPlacementHighlight>(), rangeHighlightMaterial,
            placementOutlineSprite, rangeInnerCornerSprite, rangeOutlineHeightOffset);
        evaluation.Finished += HandleRangeFinished;
        if (Application.isPlaying)
            foreach (BuildingRange range in _lastSelectedBuilding.ranges)
                if (range != null && range.RequiresMatchingTile) { TooltipManager.Show("Checking building range...", this); break; }
    }

    private void HandleRangeFinished(BuildingRangeEvaluation evaluation)
    {
        if (evaluation != _rangeDisplay.Evaluation || evaluation.IsCancelled || !Application.isPlaying) return;
        foreach (BuildingRangeResult result in evaluation.Results)
        {
            if (!result.ValidConfiguration)
            {
                TooltipManager.Show("Invalid building range: " + result.Name, this);
                return;
            }
            if (!result.RequirementMet)
            {
                var names = new string[result.required.Length];
                for (int i = 0; i < names.Length; i++) names[i] = result.required[i].ToString();
                TooltipManager.Show(result.Name + ": requires " + string.Join(" or ", names) + " within " + result.Radius + " tiles of the footprint.", this);
                return;
            }
        }
        TooltipManager.Hide(this);
    }

    private void ClearRangePreview()
    {
        if (_rangeDisplay.Evaluation != null) _rangeDisplay.Evaluation.Finished -= HandleRangeFinished;
        _rangeDisplay.Clear();
        TooltipManager.Hide(this);
    }

    private void InstantiateAndRegisterBuilding(Vector2Int origin, int requiredLayer, List<BuildingTile> footprint, BuildingData data)
    {
        GameObject newObj = Instantiate(data.prefab, transform);
        
        float x = origin.x * worldManager.cellSize + (worldManager.cellSize * 0.5f);
        float z = origin.y * worldManager.cellSize + (worldManager.cellSize * 0.5f);
        float y = requiredLayer * worldManager.heightStep;
        
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

    private void CreatePreview(BuildingData data)
    {
        if (_previewObj != null) DestroyPreview();

        if (data.footprint == null || data.footprint.Count == 0 ||
            float.IsNaN(worldManager.cellSize) || float.IsInfinity(worldManager.cellSize) ||
            worldManager.cellSize <= 0f) return;
        foreach (BuildingTile tile in data.footprint)
            if (tile == null) return;

        Shader shader = Resources.Load<Shader>("BuildingPlacementPreview");
        if (shader == null || placementHatchSprite == null || placementOutlineSprite == null ||
            placementHatchSprite.texture != placementOutlineSprite.texture)
        {
            Debug.LogError("Building placement needs the hatch and outline sprites from the same sheet and the preview shader.", this);
            return;
        }

        _lastSelectedBuilding = data;
        _currentRotationSteps = 0; 
        _previewTiles.Clear();

        _rotatedFootprint = data.GetRotatedFootprint(0);
        _cellValidity = new bool[data.footprint.Count];
        var footprintCells = new HashSet<Vector2Int>();
        foreach (BuildingTile tile in data.footprint) footprintCells.Add(tile.localPosition);

        _previewMaterial = new Material(shader);
        _previewMaterial.mainTexture = placementHatchSprite.texture;
        Rect hatchRect = placementHatchSprite.rect;
        float repeat = Mathf.Clamp(hatchRepeatPixels, 1f, Mathf.Min(hatchRect.width, hatchRect.height));
        _previewMaterial.SetVector("_HatchRect", new Vector4(
            hatchRect.x / placementHatchSprite.texture.width,
            hatchRect.y / placementHatchSprite.texture.height,
            repeat / placementHatchSprite.texture.width,
            repeat / placementHatchSprite.texture.height));
        _previewMaterial.SetFloat("_HatchTiling", hatchRect.width / repeat);
        _previewColors = new MaterialPropertyBlock();
        _previewColors.SetColor(HatchColorId, validCellColor);
        _previewColors.SetColor(OutlineColorId, outlineColor);

        _previewObj = new GameObject("BuildingPreview_" + data.buildingName);
        _previewObj.transform.SetParent(transform, false);

        foreach (BuildingTile tile in data.footprint)
        {
            GameObject tileObj = new GameObject("PreviewCell_" + tile.localPosition);
            tileObj.transform.SetParent(_previewObj.transform, false);
            
            float lx = tile.localPosition.x * worldManager.cellSize;
            float lz = tile.localPosition.y * worldManager.cellSize;
            Mesh mesh = BuildingPlacementPreviewGeometry.CreateCellMesh(
                tile.localPosition, footprintCells, worldManager.cellSize, placementOutlineSprite,
                GetGroundFillSprite(tile));
            tileObj.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer rend = tileObj.AddComponent<MeshRenderer>();
            rend.sharedMaterial = _previewMaterial;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.SetPropertyBlock(_previewColors);
            _previewTiles.Add(new PreviewTile
            {
                renderer = rend, mesh = mesh, hatchColor = validCellColor, outlineColor = outlineColor
            });

            CreateGroundIcons(tile, lx, lz);
            if (tile.isPort)
            {
                // Ground permissions occupy the center; keep combined port cells readable.
                float offset = HasGroundRule(tile) ? worldManager.cellSize * 0.35f : 0f;
                GameObject port = CreatePortIcon(_previewObj.transform, tile.portType, lx + offset, 0.2f, lz + offset);
                if (HasGroundRule(tile)) port.transform.localScale *= 0.5f;
            }
        }
    }

    private static bool HasGroundRule(BuildingTile tile) => tile.footprintIconOverride != null || tile.RequiresTileMatch || tile.HasTileRules ||
        (tile.allowedGrounds != null && tile.allowedGrounds.Count > 0);

    private Sprite GetGroundFillSprite(BuildingTile tile)
    {
        if (!HasGroundRule(tile)) return null;
        Sprite fill = tile.RequiresTileMatch ? requiredGroundFillSprite : optionalGroundFillSprite;
        // The authored fill and outline share the placement atlas. Missing or
        // incompatible overrides safely retain the normal hatch.
        return fill != null && fill.texture == placementHatchSprite.texture ? fill : null;
    }

    private void CreateGroundIcons(BuildingTile tile, float localX, float localZ)
    {
        bool typed = tile.HasTileRules;
        bool overridden = tile.footprintIconOverride != null;
        int entries = overridden ? 1 : typed ? tile.TileTypeCount : tile.allowedGrounds != null ? tile.allowedGrounds.Count : 0;
        int count = 0;
        for (int i = 0; i < entries; i++)
            if (GetGroundIcon(tile, typed, i) != null) count++;
        if (count == 0) return;
        float diameter = worldManager.cellSize * 0.45f / count;
        int index = 0;
        for (int i = 0; i < entries; i++)
        {
            Sprite sprite = GetGroundIcon(tile, typed, i);
            if (sprite == null) continue;
            string groundName = overridden ? "Override" : typed ? tile.GetTileType(i).ToString() : tile.allowedGrounds[i].name;
            var icon = new GameObject("GroundIcon_" + groundName);
            icon.transform.SetParent(_previewObj.transform, false);
            icon.transform.localPosition = new Vector3(
                localX + (index++ - (count - 1) * 0.5f) * diameter, 0.12f, localZ);
            Mesh mesh = BuildingPlacementPreviewGeometry.CreateIconMesh(sprite, diameter);
            _groundIconMeshes.Add(mesh);
            icon.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = icon.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _previewMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var properties = new MaterialPropertyBlock();
            properties.SetTexture("_MainTex", sprite.texture);
            properties.SetColor(HatchColorId, Color.white);
            renderer.SetPropertyBlock(properties);
        }
    }

    private Sprite GetGroundIcon(BuildingTile tile, bool typed, int index)
    {
        if (tile.footprintIconOverride != null) return tile.footprintIconOverride;
        if (!typed) return tile.allowedGrounds[index] != null ? tile.allowedGrounds[index].Icon : null;
        WorldTileType type = tile.GetTileType(index);
        switch (type.kind)
        {
            case WorldTileKind.FlatGround: return flatGroundTileIcon;
            case WorldTileKind.Edge: return edgeTileIcon;
            case WorldTileKind.WaterLevel: return waterLevelTileIcon;
            case WorldTileKind.ResourceNode:
            case WorldTileKind.ResourceDeposit: return ResourceCatalog.GetWorldTileIcon(type.resource);
            default: return null;
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
        ClearRangePreview();
        if (_previewObj != null)
        {
            _previewObj.SetActive(false);
            ReleasePreviewObject(_previewObj);
            _previewObj = null;
        }
        foreach (var pt in _previewTiles)
        {
            if (pt.mesh != null) ReleasePreviewObject(pt.mesh);
        }
        foreach (Mesh mesh in _groundIconMeshes)
            if (mesh != null) ReleasePreviewObject(mesh);
        _groundIconMeshes.Clear();
        if (_previewMaterial != null) ReleasePreviewObject(_previewMaterial);
        _previewMaterial = null;
        _previewColors = null;
        _rotatedFootprint = null;
        _cellValidity = null;
        _previewTiles.Clear();
        _lastSelectedBuilding = null;
    }

    private static void ReleasePreviewObject(Object previewObject)
    {
        if (Application.isPlaying) Destroy(previewObject);
        else DestroyImmediate(previewObject);
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
