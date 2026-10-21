using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using TMPro;

/// <summary>Traffic-view input and visuals. All confirmed edits go to the road backend.</summary>
[DisallowMultipleComponent]
public sealed class IntersectionBuilderTool : MonoBehaviour
{
    private const int WorkPerFrame = 256;
    private IntersectionBuilderStyle appearance = IntersectionBuilderStyle.Default;
    private readonly IntersectionBuilderSession session = new IntersectionBuilderSession();
    private readonly List<Marker> markers = new List<Marker>();
    private readonly List<MovementRecord> paths = new List<MovementRecord>();
    private readonly List<Mesh> meshes = new List<Mesh>();
    private readonly List<Vector3> vertices = new List<Vector3>(2048);
    private readonly List<int> triangles = new List<int>(3072);
    private readonly List<Color> colors = new List<Color>(2048);
    private readonly List<int> priorityDirections = new List<int>(8);
    private PlayerViewModeManager views;
    private TrafficSystemBackend traffic;
    private CameraController cameraController;
    private TrafficNodeViewOverlay overviewOverlay;
    private TrafficGraphSnapshot graph;
    private RoadNetworkSnapshot roads;
    private Material material;
    private Sprite fallbackIndexSprite;
    private Texture2D fallbackIndexTexture;
    private Coroutine rebuild;
    private GameObject visualRoot;
    private GameObject retiringRoot;
    private readonly List<Mesh> retiringMeshes = new List<Mesh>();
    private readonly List<Vector3> previewSamples = new List<Vector3>(TrafficMovementCurve.SampleCount);
    private readonly List<Vector3> snappedPreviewSamples = new List<Vector3>(TrafficMovementCurve.SampleCount);
    private LaneId cachedPreviewEntry;
    private LaneId cachedPreviewExit;
    private Vector3 previewStart;
    private Vector3 previewDirection;
    private Mesh previewMesh;
    private GameObject previewObject;
    private Mesh hoverMesh;
    private GameObject hoverObject;
    private MovementRecord hoveredPath;
    private int hoveredPriorityDirection;
    private int hoveredMarker = -1;
    private bool geometryReady;
    private bool choosingPriorityRoad;
    private Vector3 intersectionCenter;
    public IntersectionBuilderStage Stage => session.Stage;
    public bool IsChoosingPriorityRoad => choosingPriorityRoad;
    public bool CanEditFocusedConnections =>
        RoadSystemBackend.Instance != null &&
        TryGetFocusedIntersection(out _, out _) && roads != null &&
        roads.RoadAuthoringRevision == graph.SourceRoadRevision &&
        roads.BuildingAuthoringRevision == graph.SourceBuildingRevision &&
        roads.MatchesCurrentSources(RoadSystemBackend.Instance, BuildingSystemBackend.Instance);
    public event System.Action FocusedIntersectionChanged;

    public void ApplyAppearance(IntersectionBuilderStyle style)
    {
        style = style.Validated();
        // Gradient keys can be edited in place. An explicit Inspector refresh
        // must rebuild even when the struct still holds the same Gradient object.
        appearance = style;
        RefreshVisuals();
    }

    private struct Marker
    {
        public Vector3 Position;
        public ControlledNodeId Intersection;
        public LaneId Lane;
        public bool IsExit;
        public bool IsSignalIndex;
        public int SignalIndex;
        public float Radius;
        public int DirectionBit;
        public Color BaseColor;
        public SpriteRenderer Renderer;
    }

    public void Initialize(PlayerViewModeManager viewManager, TrafficSystemBackend backend,
        CameraController controller, TrafficNodeViewOverlay overlay)
    {
        Unsubscribe();
        views = viewManager;
        traffic = backend;
        cameraController = controller;
        overviewOverlay = overlay;
        if (isActiveAndEnabled) Subscribe();
    }

    private void OnEnable() => Subscribe();

    private void Subscribe()
    {
        Unsubscribe();
        if (views != null) views.ViewModeChanged += HandleViewMode;
        if (traffic != null) traffic.OnTrafficNetworkReady += HandleGraphPublished;
        HandleGraphPublished();
    }

    private void Unsubscribe()
    {
        if (views != null) views.ViewModeChanged -= HandleViewMode;
        if (traffic != null) traffic.OnTrafficNetworkReady -= HandleGraphPublished;
    }

    private bool IsTrafficView => views != null && views.CurrentViewMode == PlayerViewMode.Traffic;

    private void HandleViewMode(PlayerViewMode mode)
    {
        if (!IsTrafficView)
        {
            session.Reset();
            choosingPriorityRoad = false;
        }
        ApplyFocus();
        RefreshVisuals();
        FocusedIntersectionChanged?.Invoke();
    }

    private void HandleGraphPublished()
    {
        bool resumePrioritySelection = choosingPriorityRoad;
        graph = traffic != null ? traffic.CurrentTrafficGraphSnapshot : null;
        roads = traffic != null ? traffic.CurrentRoadSnapshot : null;
        session.Reconcile(graph);
        choosingPriorityRoad = resumePrioritySelection &&
            session.Stage != IntersectionBuilderStage.Overview;
        ApplyFocus();
        RefreshVisuals();
        if (choosingPriorityRoad)
            SetHint("Click an incoming road to give it priority\nRight click: cancel");
        FocusedIntersectionChanged?.Invoke();
    }

    private void ApplyFocus()
    {
        bool focused = IsTrafficView && session.Stage != IntersectionBuilderStage.Overview;
        if (overviewOverlay != null) overviewOverlay.SetIntersectionFocused(focused);
        if (!focused && cameraController != null) cameraController.ReleaseIntersectionFocus();
    }

    public bool FocusIntersection(ControlledNodeId intersection)
    {
        if (!IsTrafficView || graph == null || roads == null || cameraController == null ||
            !graph.TryGetControlledNode(intersection, out var node) ||
            !roads.TryGetCell(node.GridPosition, out var cell) ||
            !cameraController.FocusIntersection(cell.WorldCenter) || !session.Focus(graph, intersection)) return false;
        intersectionCenter = cell.WorldCenter;
        // This is an actual toggle change, including its button state on return.
        views.SetTrafficNodeView(false);
        ApplyFocus();
        RefreshVisuals();
        FocusedIntersectionChanged?.Invoke();
        return true;
    }

    public bool TryGetFocusedIntersection(
        out ControlledNodeRecord node,
        out Vector3 worldCenter)
    {
        worldCenter = intersectionCenter;
        node = null;
        return IsTrafficView &&
               session.Stage != IntersectionBuilderStage.Overview &&
               graph != null &&
               graph.TryGetControlledNode(session.Intersection, out node);
    }

    public bool ClearAllConnections() => ResetConnections(false);

    public bool RestoreDefaultConnections() => ResetConnections(true);

    private bool ResetConnections(bool restoreDefaults)
    {
        var backend = RoadSystemBackend.Instance;
        if (backend == null || !TryGetFocusedIntersection(out var node, out _)) return false;
        bool changed = restoreDefaults
            ? backend.TryRestoreDefaultIntersectionConnections(roads, graph, node.Id)
            : backend.TryClearIntersectionConnections(roads, graph, node.Id);
        if (!changed)
        {
            SetHint("Waiting for traffic to rebuild...\nRight click elsewhere: back");
            return false;
        }
        session.Focus(graph, node.Id);
        choosingPriorityRoad = false;
        if (previewObject != null) previewObject.SetActive(false);
        hoveredPath = null;
        hoveredMarker = -1;
        hoveredPriorityDirection = 0;
        if (hoverObject != null) hoverObject.SetActive(false);
        bool graphCurrent = CanEditFocusedConnections;
        if (graphCurrent) RefreshVisuals();
        string status = restoreDefaults
            ? (graphCurrent ? "Default connections restored." : "Restoring default connections...")
            : (graphCurrent ? "All connections deleted." : "Deleting all connections...");
        SetHint(status + "\nRight click elsewhere: back");
        FocusedIntersectionChanged?.Invoke();
        return true;
    }

    public bool BeginPriorityRoadSelection()
    {
        if (!TryGetFocusedIntersection(out _, out _)) return false;
        session.Focus(graph, session.Intersection);
        choosingPriorityRoad = true;
        RefreshVisuals();
        SetHint("Click an incoming road to give it priority\nRight click: cancel");
        FocusedIntersectionChanged?.Invoke();
        return true;
    }

    public void CancelPriorityRoadSelection()
    {
        if (!choosingPriorityRoad) return;
        choosingPriorityRoad = false;
        RefreshVisuals();
        FocusedIntersectionChanged?.Invoke();
    }

    private void Update()
    {
        if (!IsTrafficView || cameraController == null || cameraController.cam == null) return;
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        Vector2 mouse = Input.mousePosition;
        int picked = geometryReady && !overUI ? PickMarker(mouse) : -1;
        // Numbered circles own both buttons; right click elsewhere still navigates back.
        if (picked >= 0 && markers[picked].IsSignalIndex &&
            (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)))
        {
            EditSignalIndex(markers[picked], Input.GetMouseButtonDown(0) ? 1 : -1);
            return;
        }
        if (Input.GetMouseButtonDown(1) && !overUI)
        {
            if (session.Stage == IntersectionBuilderStage.Overview) return;
            if (choosingPriorityRoad)
            {
                choosingPriorityRoad = false;
                RefreshVisuals();
                FocusedIntersectionChanged?.Invoke();
                return;
            }
            session.Back();
            ApplyFocus();
            RefreshVisuals();
            FocusedIntersectionChanged?.Invoke();
            return;
        }
        if (!geometryReady) return;
        bool clicked = Input.GetMouseButtonDown(0);
        MovementRecord removalTarget = null;
        if (!overUI && session.Stage == IntersectionBuilderStage.Entry)
        {
            // Markers take precedence over paths for both hover and click.
            removalTarget = picked >= 0
                ? (markers[picked].IsExit ? session.FindEditableConnection(graph, markers[picked].Lane) : null)
                : PickPath(mouse);
        }
        int priorityHoverDirection = choosingPriorityRoad && picked >= 0 &&
            !markers[picked].IsExit && !markers[picked].IsSignalIndex ? markers[picked].DirectionBit : 0;
        UpdateHoverTargets(picked, removalTarget, priorityHoverDirection);
        if (session.Stage == IntersectionBuilderStage.Drawing) UpdatePreview(!overUI, picked);
        if (overUI || !clicked) return;
        if (removalTarget != null)
        {
            EditConnection(removalTarget.TargetLaneId, false);
            return;
        }
        if (picked >= 0)
        {
            Marker marker = markers[picked];
            if (choosingPriorityRoad && !marker.IsExit &&
                TrySetPriorityRoad(marker.DirectionBit)) return;
            if (session.Stage == IntersectionBuilderStage.Overview)
            {
                FocusIntersection(marker.Intersection);
            }
            else if (session.Stage == IntersectionBuilderStage.Drawing)
            {
                if (marker.IsExit) EditConnection(marker.Lane, true);
            }
            else if (!marker.IsExit && session.SelectEntry(graph, marker.Lane)) RefreshVisuals();
            return;
        }
    }

    private void EditSignalIndex(Marker marker, int delta)
    {
        int index = Mathf.Clamp(marker.SignalIndex + delta, 0, 9);
        if (index == marker.SignalIndex) return;
        var backend = RoadSystemBackend.Instance;
        if (backend != null && backend.TrySetIntersectionTrafficLightIndex(roads, graph,
                session.Intersection, marker.Lane, index))
            SetHint("Updating lights...\nLeft click index: increase | Right click index: decrease");
        else
            SetHint("Waiting for traffic to rebuild...\nRight click elsewhere: back");
    }

    private bool TrySetPriorityRoad(int selectedDirection)
    {
        if (!TryGetFocusedIntersection(out var node, out _) ||
            !IntersectionControlUtility.IsDirectionBit(selectedDirection)) return false;
        priorityDirections.Clear();
        for (int i = 0; i < node.IncomingLaneIds.Count; i++)
        {
            if (!graph.TryGetLane(node.IncomingLaneIds[i], out var lane) ||
                priorityDirections.Contains(lane.EndLegDirectionBit)) continue;
            priorityDirections.Add(lane.EndLegDirectionBit);
        }
        int opposite = IntersectionControlUtility.FindMostOppositeDirection(
            selectedDirection,
            priorityDirections);
        var backend = RoadSystemBackend.Instance;
        if (opposite == 0 || backend == null ||
            !backend.SetIntersectionPriorityDirections(
                node.GridPosition,
                selectedDirection,
                opposite)) return false;
        choosingPriorityRoad = false;
        RefreshVisuals();
        FocusedIntersectionChanged?.Invoke();
        return true;
    }

    private void EditConnection(LaneId exit, bool connected)
    {
        var backend = RoadSystemBackend.Instance;
        if (backend != null && backend.TrySetIntersectionConnection(roads, graph,
                session.Intersection, session.Entry, exit, connected))
        {
            session.CompleteConnection();
            // Keep the exact snapped curve visible while its graph replacement compiles.
            if (!connected && previewObject != null) previewObject.SetActive(false);
            SetHint("Updating connections…  •  Right click: back");
        }
        else
        {
            bool stale = backend != null && roads != null &&
                !roads.MatchesCurrentSources(backend, BuildingSystemBackend.Instance);
            SetHint(stale ? "Waiting for traffic to rebuild…  •  Right click: back" :
                "Connection already exists or this exit is unavailable.  •  Right click: cancel");
        }
    }

    private void RefreshVisuals()
    {
        if (!isActiveAndEnabled || !IsTrafficView || graph == null || roads == null)
        {
            ClearGeometry();
            TooltipManager.Hide(this);
            return;
        }
        PrepareRebuild();
        string hint = string.Empty;
        switch (session.Stage)
        {
            case IntersectionBuilderStage.Overview:
                hint = "Click an intersection icon to edit its connections"; break;
            case IntersectionBuilderStage.Intersection:
                hint = "Click an entry icon\nRight click elsewhere: traffic overview"; break;
            case IntersectionBuilderStage.Entry:
                hint = "Click a path or connected exit to delete\nClick the selected entry to connect\nRight click elsewhere: intersection"; break;
            case IntersectionBuilderStage.Drawing:
                hint = "Click an available exit to connect\nRight click elsewhere: cancel"; break;
        }
        if (!choosingPriorityRoad && session.Stage != IntersectionBuilderStage.Overview &&
            graph.TryGetControlledNode(session.Intersection, out var focused) &&
            IntersectionControlUtility.UsesTrafficLights(focused.RuleType))
            hint = "Left click index: increase | Right click index: decrease\n" +
                (focused.RuleType == IntersectionRuleType.SmartTrafficLight
                    ? "Same index = green together; longest eligible wait goes first\n"
                    : "Same index = green together; indices run lowest to highest\n") + hint;
        SetHint(hint);
        if (material == null)
        {
            var shader = Resources.Load<Shader>("TrafficLaneOverlay");
            if (shader == null)
            {
                Debug.LogError("Intersection builder requires the TrafficLaneOverlay shader.", this);
                return;
            }
            material = new Material(shader) { name = "Intersection builder (runtime)" };
        }
        rebuild = StartCoroutine(Build());
    }

    private IEnumerator Build()
    {
        // Always yield once so the coroutine handle is assigned before completion.
        yield return null;
        visualRoot = new GameObject("Intersection builder visuals");
        // PlayerViewModeManager can live below a screen-space Canvas. World
        // geometry must not inherit that UI transform or nested-canvas rules.
        visualRoot.SetActive(false);
        int work = 0;
        using (var builder = AppendVisuals())
        {
            while (builder.MoveNext())
            {
                if (++work < WorkPerFrame) continue;
                PublishChunk();
                work = 0;
                yield return null;
            }
        }
        PublishChunk();
        CompleteRebuild();
        rebuild = null;
    }

    private void CompleteRebuild()
    {
        visualRoot.SetActive(true);
        ReleaseRetiringGeometry();
        geometryReady = true;
    }

    private void PrepareRebuild()
    {
        if (rebuild != null) StopCoroutine(rebuild);
        rebuild = null;

        UpdateHoveredMarker(-1);
        if (visualRoot != null)
        {
            if (geometryReady)
            {
                ReleaseRetiringGeometry();
                retiringRoot = visualRoot;
                retiringMeshes.AddRange(meshes);
                meshes.Clear();
            }
            else
            {
                visualRoot.SetActive(false);
                Release(visualRoot);
                foreach (var mesh in meshes) Release(mesh);
                meshes.Clear();
            }
            visualRoot = null;
        }

        geometryReady = false;
        markers.Clear();
        paths.Clear();
        vertices.Clear();
        triangles.Clear();
        colors.Clear();
        if (previewObject != null) previewObject.SetActive(false);
        if (previewMesh != null) Release(previewMesh);
        previewMesh = null;
        previewObject = null;
        if (hoverObject != null) hoverObject.SetActive(false);
        if (hoverMesh != null) Release(hoverMesh);
        hoverMesh = null;
        hoverObject = null;
        hoveredPath = null;
        hoveredPriorityDirection = 0;
        hoveredMarker = -1;
        previewSamples.Clear();
        snappedPreviewSamples.Clear();
        cachedPreviewEntry = cachedPreviewExit = LaneId.Invalid;
    }

    private void ReleaseRetiringGeometry()
    {
        if (retiringRoot != null)
        {
            retiringRoot.SetActive(false);
            Release(retiringRoot);
            retiringRoot = null;
        }
        foreach (var mesh in retiringMeshes) Release(mesh);
        retiringMeshes.Clear();
    }

    private IEnumerator<int> AppendVisuals()
    {
        if (session.Stage == IntersectionBuilderStage.Overview)
        {
            foreach (var node in graph.ControlledNodes)
            {
                if (node.NodeKind == RoadNodeKind.Intersection && roads.TryGetCell(node.GridPosition, out var cell))
                {
                    var marker = new Marker { Position = cell.WorldCenter, Intersection = node.Id,
                        Radius = appearance.intersectionCircleRadius };
                    AppendMarkerImage(ref marker, appearance.intersectionSprite, appearance.intersectionIconColor);
                    markers.Add(marker);
                    AppendMarkerDecoration(
                        marker,
                        appearance.GetRuleTypeSprite(node.RuleType),
                        appearance.intersectionTypeIconColor,
                        appearance.intersectionTypeIconScale);
                }
                yield return 1;
            }
            yield break;
        }
        if (!graph.TryGetControlledNode(session.Intersection, out var focused)) yield break;
        int incomingDirectionMask = 0;
        for (int i = 0; i < focused.IncomingLaneIds.Count; i++)
            if (graph.TryGetLane(focused.IncomingLaneIds[i], out var incomingLane))
                incomingDirectionMask |= incomingLane.EndLegDirectionBit;
        for (int direction = 0; direction < 2; direction++)
        {
            var lanes = direction == 0 ? focused.IncomingLaneIds : focused.OutgoingLaneIds;
            foreach (var id in lanes)
            {
                yield return 1;
                if (!graph.TryGetLane(id, out var lane) || !graph.TryGetGeometry(lane.GeometryId, out var geometry) ||
                    geometry.Samples.Count < 2) continue;
                Color laneColor = direction == 0 ? appearance.incomingLaneColor : appearance.outgoingLaneColor;
                for (int i = 1; i < geometry.Samples.Count; i++)
                {
                    AppendLine(geometry.Samples[i - 1], geometry.Samples[i], laneColor,
                        appearance.laneLineWidth, i == geometry.Samples.Count / 2);
                    yield return 1;
                }
                if (direction == 1 && !session.Entry.IsValid) continue;
                Vector3 position = direction == 0 ? geometry.Samples[geometry.Samples.Count - 1] : geometry.Samples[0];
                var marker = new Marker { Position = position, Lane = id, IsExit = direction == 1,
                    Radius = direction == 0 ? appearance.laneCircleRadius : appearance.exitIconRadius,
                    DirectionBit = direction == 0 ? lane.EndLegDirectionBit : lane.StartLegDirectionBit };
                Color color = id == session.Entry ? appearance.selectedColor : appearance.entryColor;
                if (direction == 0 && !session.Entry.IsValid)
                {
                    if (choosingPriorityRoad) color = appearance.availableExitColor;
                    else if (IsPriorityDirection(marker.DirectionBit)) color = appearance.selectedColor;
                }
                Sprite sprite = direction == 0 ? appearance.entrySprite : appearance.exitSprite;
                if (direction == 1)
                {
                    bool connected = IsConnected(id);
                    color = connected ? appearance.connectedColor :
                        IntersectionConnectionEditing.CanConnect(roads, graph, session.Intersection, session.Entry, id)
                            ? appearance.availableExitColor : appearance.unavailableColor;
                    if (connected && appearance.connectedExitSprite != null)
                        sprite = appearance.connectedExitSprite;
                }
                AppendMarkerImage(ref marker, sprite, color);
                markers.Add(marker);
                if (direction == 0 && IntersectionControlUtility.UsesTrafficLights(focused.RuleType) &&
                    !choosingPriorityRoad)
                    AppendSignalIndexMarker(marker, lane, focused, incomingDirectionMask);
            }
        }
        // Keep the whole intersection readable; only the selected entry's paths
        // are interactive and receive the full-width foreground treatment.
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var id in focused.MovementIds)
            {
                yield return 1;
                if (!graph.TryGetMovement(id, out var movement) || movement.Kind != TrafficMovementKind.Intersection ||
                    !graph.TryGetGeometry(movement.GeometryId, out var geometry)) continue;
                bool selected = movement.SourceLaneId == session.Entry;
                if (selected != (pass == 1)) continue;
                if (selected) paths.Add(movement);
                Color pathColor = selected ? appearance.connectedColor :
                    IsPriorityDirection(movement.FromDirectionBit)
                        ? appearance.priorityRoadBackgroundPathColor : appearance.backgroundPathColor;
                float pathWidth = selected ? appearance.connectionLineWidth : appearance.backgroundPathWidth;
                for (int i = 1; i < geometry.Samples.Count; i++)
                {
                    AppendLine(geometry.Samples[i - 1], geometry.Samples[i], pathColor,
                        pathWidth, selected && appearance.showIntersectionArrows && i == geometry.Samples.Count / 2);
                    yield return 1;
                }
            }
        }
    }

    private void AppendSignalIndexMarker(Marker entry, LaneRecord lane,
        ControlledNodeRecord node, int incomingDirectionMask)
    {
        Vector2 outward = IntersectionControlUtility.DirectionVector(lane.EndLegDirectionBit);
        // Move along this lane so adjacent lanes keep their own separate controls.
        float offset = entry.Radius + appearance.trafficLightIconRadius * 2f +
            appearance.trafficLightIconOutwardOffset + 0.04f;
        var marker = new Marker
        {
            Position = entry.Position + new Vector3(outward.x, 0f, outward.y) * offset,
            Lane = lane.Id,
            IsSignalIndex = true,
            Radius = entry.Radius,
            SignalIndex = IntersectionControlUtility.GetTrafficLightIndex(lane.EndLegDirectionBit,
                lane.LaneOrdinal, incomingDirectionMask, node.TrafficLightIndices)
        };
        Vector3 center = MarkerWorldPosition(marker);
        marker.BaseColor = appearance.GetTrafficLightIndexColor(marker.SignalIndex);
        marker.Renderer = AppendWorldSprite("Traffic light index background", center,
            marker.Radius, GetIndexBackgroundSprite(), marker.BaseColor, 4);
        var labelObject = new GameObject("Traffic light index", typeof(TextMeshPro));
        labelObject.transform.SetParent(visualRoot.transform, true);
        labelObject.transform.position = center;
        labelObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var label = labelObject.GetComponent<TextMeshPro>();
        if (appearance.trafficLightIndexFont != null) label.font = appearance.trafficLightIndexFont;
        label.rectTransform.sizeDelta = Vector2.one * (marker.Radius * 2f);
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 7f;
        label.text = marker.SignalIndex.ToString();
        label.color = appearance.trafficLightIndexTextColor;
        label.GetComponent<MeshRenderer>().sortingOrder = 7;
        CenterIndexLabel(label, marker.Renderer, center, marker.Radius);
        markers.Add(marker);
    }

    private static void CenterIndexLabel(TextMeshPro label, SpriteRenderer background,
        Vector3 center, float radius)
    {
        // A custom sprite pivot must not move the background away from its hit target.
        if (background.sprite != null)
            background.transform.position = center -
                background.transform.TransformVector(background.sprite.bounds.center);
        label.ForceMeshUpdate(true);
        float size = Mathf.Max(label.preferredWidth, label.preferredHeight);
        if (size > 0.0001f) label.transform.localScale = Vector3.one * (radius * 1.4f / size);
        if (label.textInfo.characterCount == 0 || !label.textInfo.characterInfo[0].isVisible) return;
        TMP_CharacterInfo digit = label.textInfo.characterInfo[0];
        Vector3 glyphCenter = (digit.bottomLeft + digit.topRight) * 0.5f;
        // Font line height and advance include empty space around the visible digit.
        // Center its actual quad instead, in the sprite's plane, to avoid parallax.
        label.transform.position = center - label.transform.TransformVector(glyphCenter);
    }

    private Sprite GetIndexBackgroundSprite()
    {
        if (appearance.trafficLightIndexBackgroundSprite != null)
            return appearance.trafficLightIndexBackgroundSprite;
        if (appearance.intersectionSprite != null) return appearance.intersectionSprite;
        if (fallbackIndexSprite != null) return fallbackIndexSprite;
        const int size = 64;
        fallbackIndexTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Traffic light index circle (runtime)",
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f),
                    Vector2.one * (size * 0.5f));
                byte alpha = (byte)(Mathf.Clamp01(size * 0.5f - distance) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        fallbackIndexTexture.SetPixels32(pixels);
        fallbackIndexTexture.Apply(false, true);
        fallbackIndexSprite = Sprite.Create(fallbackIndexTexture,
            new Rect(0f, 0f, size, size), Vector2.one * 0.5f, size);
        fallbackIndexSprite.name = "Traffic light index circle (runtime)";
        return fallbackIndexSprite;
    }

    private bool IsConnected(LaneId exit)
    {
        if (!graph.TryGetLane(session.Entry, out var source)) return false;
        foreach (var id in source.OutgoingMovementIds)
            if (graph.TryGetMovement(id, out var movement) && movement.Kind == TrafficMovementKind.Intersection &&
                movement.TargetLaneId == exit) return true;
        return false;
    }

    private bool IsPriorityDirection(int directionBit)
    {
        if (!TryGetFocusedIntersection(out var node, out _) ||
            RoadSystemBackend.Instance == null ||
            !RoadSystemBackend.Instance.Intersections.TryGetValue(node.GridPosition, out var data) ||
            data.RuleType != IntersectionRuleType.PriorityRoad) return false;
        return directionBit == data.PriorityDirectionBitA ||
               directionBit == data.PriorityDirectionBitB;
    }

    private void AppendLine(Vector3 from, Vector3 to, Color color, float width, bool arrow)
    {
        var style = TrafficNodeViewStyle.Default;
        style.lineWidth = width;
        style.lineColor = style.arrowColor = color;
        style.heightOffset = appearance.heightOffset;
        style.arrowLength = appearance.arrowLength;
        style.arrowWidth = appearance.arrowWidth;
        TrafficNodeViewOverlay.AppendSegment(vertices, triangles, from, to, arrow, style, colors);
    }

    private void AppendMarkerImage(ref Marker marker, Sprite sprite, Color color)
    {
        SpriteRenderer renderer = AppendWorldSprite(
            marker.Intersection.IsValid ? "Intersection icon" :
            marker.IsExit ? "Exit icon" : "Entry icon",
            MarkerWorldPosition(marker),
            marker.Radius,
            sprite,
            color,
            2);
        marker.BaseColor = color;
        marker.Renderer = renderer;
    }

    private void AppendMarkerDecoration(
        Marker marker,
        Sprite sprite,
        Color color,
        float scale)
    {
        if (sprite == null) return;
        AppendWorldSprite(
            "Intersection control type icon",
            MarkerWorldPosition(marker) + Vector3.up * 0.003f,
            marker.Radius * scale,
            sprite,
            color,
            3);
    }

    private SpriteRenderer AppendWorldSprite(
        string objectName,
        Vector3 position,
        float radius,
        Sprite sprite,
        Color color,
        int sortingOrder)
    {
        var imageObject = new GameObject(objectName, typeof(SpriteRenderer));
        imageObject.transform.SetParent(visualRoot != null ? visualRoot.transform : transform, true);
        imageObject.transform.position = position;
        imageObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        var renderer = imageObject.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        if (sprite == null) return renderer;
        float sourceSize = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
        if (sourceSize > 0.0001f)
            imageObject.transform.localScale = Vector3.one * (radius * 2f / sourceSize);
        return renderer;
    }

    private int PickMarker(Vector2 mouse)
    {
        int picked = -1;
        float nearest = float.PositiveInfinity;
        var cam = cameraController.cam;
        for (int i = 0; i < markers.Count; i++)
        {
            Marker marker = markers[i];
            Vector3 worldPosition = MarkerWorldPosition(marker);
            Vector3 center = cam.WorldToScreenPoint(worldPosition);
            if (center.z <= 0f) continue;
            float distance = Vector2.Distance(mouse, center);
            Vector3 edgeX = cam.WorldToScreenPoint(worldPosition + Vector3.right * marker.Radius);
            Vector3 edgeZ = cam.WorldToScreenPoint(worldPosition + Vector3.forward * marker.Radius);
            float screenRadius = Mathf.Max(Vector2.Distance(center, edgeX), Vector2.Distance(center, edgeZ));
            if (distance <= Mathf.Max(8f, screenRadius) && distance < nearest)
            {
                nearest = distance;
                picked = i;
            }
        }
        return picked;
    }

    private Vector3 MarkerWorldPosition(Marker marker) =>
        marker.Position + Vector3.up * (appearance.heightOffset + 0.025f);

    private void UpdateHoveredMarker(int markerIndex)
    {
        if (hoveredMarker == markerIndex) return;
        if (hoveredMarker >= 0 && hoveredMarker < markers.Count)
        {
            Marker previous = markers[hoveredMarker];
            if (previous.Renderer != null) previous.Renderer.color = previous.BaseColor;
        }
        hoveredMarker = markerIndex;
        if (hoveredMarker >= 0 && hoveredMarker < markers.Count)
        {
            Marker current = markers[hoveredMarker];
            if (current.Renderer != null) current.Renderer.color = current.IsSignalIndex
                ? appearance.GetTrafficLightIndexColor(current.SignalIndex, true)
                : appearance.hoveredMarkerColor;
        }
    }

    private void UpdateHoverTargets(
        int pickedMarker,
        MovementRecord path,
        int priorityDirection)
    {
        int markerIndex = pickedMarker >= 0 ? pickedMarker :
            path != null ? FindExitMarker(path.TargetLaneId) : -1;
        UpdateHoveredMarker(markerIndex);
        if (priorityDirection != 0)
            UpdatePriorityPathHover(priorityDirection);
        else
            UpdateHoveredPath(path);
    }

    private int FindExitMarker(LaneId lane)
    {
        for (int i = 0; i < markers.Count; i++)
            if (markers[i].IsExit && markers[i].Lane == lane) return i;
        return -1;
    }

    private MovementRecord PickPath(Vector2 mouse)
    {
        MovementRecord picked = null;
        var cam = cameraController.cam;
        float halfWidthPixels = cam.orthographic
            ? appearance.connectionLineWidth * cam.pixelHeight / Mathf.Max(0.01f, cam.orthographicSize * 4f)
            : 0f;
        float nearest = Mathf.Max(7f, halfWidthPixels + 3f);
        foreach (var path in paths)
        {
            if (!graph.TryGetGeometry(path.GeometryId, out var geometry)) continue;
            for (int i = 1; i < geometry.Samples.Count; i++)
            {
                Vector3 from = cam.WorldToScreenPoint(geometry.Samples[i - 1] + Vector3.up * appearance.heightOffset);
                Vector3 to = cam.WorldToScreenPoint(geometry.Samples[i] + Vector3.up * appearance.heightOffset);
                if (from.z <= 0f || to.z <= 0f) continue;
                float distance = DistanceToSegment(mouse, from, to);
                if (distance < nearest) { nearest = distance; picked = path; }
            }
        }
        return picked;
    }

    public static float DistanceToSegment(Vector2 point, Vector2 from, Vector2 to)
    {
        Vector2 delta = to - from;
        float t = delta.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector2.Dot(point - from, delta) / delta.sqrMagnitude) : 0f;
        return Vector2.Distance(point, from + delta * t);
    }

    private void UpdateHoveredPath(MovementRecord path)
    {
        if (hoveredPriorityDirection == 0 && hoveredPath == path) return;
        hoveredPriorityDirection = 0;
        hoveredPath = path;
        if (path == null)
        {
            if (hoverObject != null) hoverObject.SetActive(false);
            return;
        }
        if (!graph.TryGetGeometry(path.GeometryId, out var geometry)) return;
        if (hoverMesh == null)
        {
            hoverMesh = new Mesh { name = "Intersection path hover" };
            hoverMesh.MarkDynamic();
            hoverObject = CreateMeshObject("Hovered connection", hoverMesh);
            hoverObject.GetComponent<MeshRenderer>().sortingOrder = 1;
        }
        vertices.Clear(); triangles.Clear(); colors.Clear();
        for (int i = 1; i < geometry.Samples.Count; i++)
            AppendLine(geometry.Samples[i - 1] + Vector3.up * 0.008f,
                geometry.Samples[i] + Vector3.up * 0.008f, appearance.hoveredPathColor,
                appearance.connectionLineWidth, appearance.showIntersectionArrows && i == geometry.Samples.Count / 2);
        FillMesh(hoverMesh);
        hoverObject.SetActive(true);
    }

    private void UpdatePriorityPathHover(int selectedDirection)
    {
        if (hoveredPriorityDirection == selectedDirection) return;
        hoveredPriorityDirection = selectedDirection;
        hoveredPath = null;
        if (!TryGetFocusedIntersection(out ControlledNodeRecord node, out _))
        {
            if (hoverObject != null) hoverObject.SetActive(false);
            return;
        }

        priorityDirections.Clear();
        for (int i = 0; i < node.IncomingLaneIds.Count; i++)
        {
            if (!graph.TryGetLane(node.IncomingLaneIds[i], out LaneRecord lane) ||
                priorityDirections.Contains(lane.EndLegDirectionBit)) continue;
            priorityDirections.Add(lane.EndLegDirectionBit);
        }
        int oppositeDirection = IntersectionControlUtility.FindMostOppositeDirection(
            selectedDirection,
            priorityDirections);

        if (hoverMesh == null)
        {
            hoverMesh = new Mesh { name = "Priority road hover" };
            hoverMesh.MarkDynamic();
            hoverObject = CreateMeshObject("Priority road preview", hoverMesh);
            hoverObject.GetComponent<MeshRenderer>().sortingOrder = 1;
        }

        vertices.Clear();
        triangles.Clear();
        colors.Clear();
        for (int movementIndex = 0;
             movementIndex < node.MovementIds.Count;
             movementIndex++)
        {
            if (!graph.TryGetMovement(
                    node.MovementIds[movementIndex], out MovementRecord movement) ||
                movement.Kind != TrafficMovementKind.Intersection ||
                (movement.FromDirectionBit != selectedDirection &&
                 movement.FromDirectionBit != oppositeDirection) ||
                !graph.TryGetGeometry(
                    movement.GeometryId, out TrafficGeometryRecord geometry)) continue;
            for (int sampleIndex = 1;
                 sampleIndex < geometry.Samples.Count;
                 sampleIndex++)
            {
                AppendLine(
                    geometry.Samples[sampleIndex - 1] + Vector3.up * 0.008f,
                    geometry.Samples[sampleIndex] + Vector3.up * 0.008f,
                    appearance.hoveredPathColor,
                    appearance.connectionLineWidth,
                    appearance.showIntersectionArrows &&
                    sampleIndex == geometry.Samples.Count / 2);
            }
        }

        if (vertices.Count == 0)
        {
            hoverMesh.Clear();
            hoverObject.SetActive(false);
            return;
        }
        FillMesh(hoverMesh);
        hoverObject.SetActive(true);
    }

    private void UpdatePreview(bool visible, int pickedMarker)
    {
        if (previewMesh == null)
        {
            previewMesh = new Mesh { name = "Intersection connection preview" };
            previewMesh.MarkDynamic();
            previewObject = CreateMeshObject("Connection preview", previewMesh);
        }
        var ray = cameraController.cam.ScreenPointToRay(Input.mousePosition);
        var plane = new Plane(Vector3.up, intersectionCenter);
        if (!visible || !plane.Raycast(ray, out float distance) ||
            !graph.TryGetLane(session.Entry, out var lane) || !graph.TryGetGeometry(lane.GeometryId, out var geometry) ||
            geometry.Samples.Count < 2)
        {
            previewObject.SetActive(false);
            return;
        }
        previewObject.SetActive(true);
        vertices.Clear(); triangles.Clear(); colors.Clear();
        if (cachedPreviewEntry != session.Entry)
        {
            previewStart = TrafficMovementCurve.SampleAtDistance(geometry.Samples, geometry.LengthUnits, out previewDirection);
            cachedPreviewEntry = session.Entry;
        }
        Vector3 end = ray.GetPoint(distance);
        previewSamples.Clear();
        LaneId desiredExit = LaneId.Invalid;
        if (pickedMarker >= 0 && markers[pickedMarker].IsExit &&
            IntersectionConnectionEditing.CanConnect(roads, graph, session.Intersection,
                session.Entry, markers[pickedMarker].Lane))
            desiredExit = markers[pickedMarker].Lane;
        if (cachedPreviewExit != desiredExit)
        {
            cachedPreviewExit = desiredExit;
            snappedPreviewSamples.Clear();
            if (desiredExit.IsValid) TryAppendConnectionPreview(graph, session.Entry, desiredExit, snappedPreviewSamples);
        }
        previewSamples.AddRange(snappedPreviewSamples);
        if (previewSamples.Count == 0)
        {
            Vector3 exitDirection = (end - intersectionCenter).normalized;
            if (exitDirection.sqrMagnitude < 0.0001f) exitDirection = previewDirection;
            TrafficMovementCurve.AppendSamples(previewSamples, previewStart, end, previewDirection, exitDirection);
        }
        for (int i = 1; i < previewSamples.Count; i++)
            AppendLine(previewSamples[i - 1], previewSamples[i], appearance.selectedColor, appearance.previewLineWidth,
                appearance.showIntersectionArrows && i == previewSamples.Count / 2);
        FillMesh(previewMesh);
    }

    public static bool TryAppendConnectionPreview(TrafficGraphSnapshot snapshot, LaneId entry, LaneId exit,
        List<Vector3> destination)
    {
        if (snapshot == null || destination == null || !snapshot.TryGetLane(entry, out var source) ||
            !snapshot.TryGetLane(exit, out var target) || source.EndAnchorCell != target.StartAnchorCell ||
            !snapshot.TryGetGeometry(source.GeometryId, out var sourceGeometry) || sourceGeometry.Samples.Count < 2 ||
            !snapshot.TryGetGeometry(target.GeometryId, out var targetGeometry) || targetGeometry.Samples.Count < 2) return false;
        Vector3 start = TrafficMovementCurve.SampleAtDistance(sourceGeometry.Samples, sourceGeometry.LengthUnits, out var sourceDirection);
        Vector3 end = TrafficMovementCurve.SampleAtDistance(targetGeometry.Samples, 0f, out var targetDirection);
        TrafficMovementCurve.AppendSamples(destination, start, end, sourceDirection, targetDirection);
        return true;
    }

    private void PublishChunk()
    {
        if (vertices.Count == 0) return;
        var mesh = new Mesh { name = "Intersection builder chunk" };
        FillMesh(mesh);
        meshes.Add(mesh);
        CreateMeshObject("Intersection chunk", mesh);
    }

    private void FillMesh(Mesh mesh)
    {
        for (int i = 0; i < vertices.Count; i++) vertices[i] = visualRoot.transform.InverseTransformPoint(vertices[i]);
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        vertices.Clear(); triangles.Clear(); colors.Clear();
    }

    private GameObject CreateMeshObject(string objectName, Mesh mesh)
    {
        var child = new GameObject(objectName, typeof(MeshFilter), typeof(MeshRenderer));
        child.transform.SetParent(visualRoot.transform, false);
        child.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = child.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return child;
    }

    private void SetHint(string text)
    {
        TooltipManager.ShowForView(text, this, PlayerViewMode.Traffic);
    }

    private void ClearGeometry()
    {
        if (rebuild != null) StopCoroutine(rebuild);
        rebuild = null;
        geometryReady = false;
        if (visualRoot != null) { visualRoot.SetActive(false); Release(visualRoot); }
        visualRoot = null;
        foreach (var mesh in meshes) Release(mesh);
        meshes.Clear(); markers.Clear(); paths.Clear();
        ReleaseRetiringGeometry();
        vertices.Clear(); triangles.Clear(); colors.Clear();
        if (previewMesh != null) Release(previewMesh);
        previewMesh = null;
        previewObject = null;
        if (hoverMesh != null) Release(hoverMesh);
        hoverMesh = null;
        hoverObject = null;
        hoveredPath = null;
        hoveredPriorityDirection = 0;
        hoveredMarker = -1;
        previewSamples.Clear();
        snappedPreviewSamples.Clear();
        cachedPreviewEntry = cachedPreviewExit = LaneId.Invalid;
    }

    private void OnDisable()
    {
        Unsubscribe();
        session.Reset();
        ApplyFocus();
        ClearGeometry();
        TooltipManager.Hide(this);
        if (material != null) Release(material);
        material = null;
        if (fallbackIndexSprite != null) Release(fallbackIndexSprite);
        if (fallbackIndexTexture != null) Release(fallbackIndexTexture);
        fallbackIndexSprite = null;
        fallbackIndexTexture = null;
        FocusedIntersectionChanged?.Invoke();
    }

    private static void Release(Object value)
    {
        if (Application.isPlaying) Destroy(value);
        else DestroyImmediate(value);
    }
}
