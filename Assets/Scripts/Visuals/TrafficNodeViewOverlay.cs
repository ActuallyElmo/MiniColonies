using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Read-only lane visualization of the last validated traffic snapshot.</summary>
[DisallowMultipleComponent]
public sealed class TrafficNodeViewOverlay : MonoBehaviour
{
    private const int SamplesPerFrame = 256;
    private const float InnerLayerOffset = 0.002f;
    private const float ArrowLayerOffset = 0.004f;
    private PlayerViewModeManager views;
    private TrafficSystemBackend traffic;
    private Coroutine rebuild;
    private GameObject meshRoot;
    private Material material;
    private bool intersectionFocused;

    public void SetIntersectionFocused(bool focused)
    {
        if (intersectionFocused == focused) return;
        intersectionFocused = focused;
        Refresh();
    }
    private TrafficNodeViewStyle appearance = TrafficNodeViewStyle.Default;
    private readonly List<Mesh> meshes = new List<Mesh>();

    public void Initialize(PlayerViewModeManager viewManager, TrafficSystemBackend backend,
        TrafficNodeViewStyle style)
    {
        Unsubscribe();
        views = viewManager;
        traffic = backend;
        appearance = style.Validated();
        if (isActiveAndEnabled) Subscribe();
    }

    public void ApplyAppearance(TrafficNodeViewStyle style)
    {
        appearance = style.Validated();
        Refresh();
    }

    private void OnEnable() => Subscribe();

    private void Subscribe()
    {
        Unsubscribe();
        if (views != null) views.TrafficNodeViewChanged += HandleVisibility;
        if (traffic != null) traffic.OnTrafficNetworkReady += Refresh;
        Refresh();
    }

    private void Unsubscribe()
    {
        if (views != null) views.TrafficNodeViewChanged -= HandleVisibility;
        if (traffic != null) traffic.OnTrafficNetworkReady -= Refresh;
    }

    private void HandleVisibility(bool enabled) => Refresh();

    private void Refresh()
    {
        ClearGeometry();
        if (!isActiveAndEnabled || intersectionFocused || views == null || !views.TrafficNodeViewEnabled || traffic == null)
            return;
        var snapshot = traffic.CurrentTrafficGraphSnapshot;
        if (snapshot == null || snapshot.Lanes.Count == 0) return;
        if (material == null)
        {
            var shader = Resources.Load<Shader>("TrafficLaneOverlay");
            if (shader == null)
            {
                Debug.LogError("Traffic lane overlay shader is missing.", this);
                return;
            }
            material = new Material(shader) { name = "Traffic lane overlay (runtime)" };
        }
        rebuild = StartCoroutine(Build(snapshot));
    }

    private IEnumerator Build(TrafficGraphSnapshot snapshot)
    {
        meshRoot = new GameObject("Traffic lanes");
        meshRoot.transform.SetParent(transform, false);
        meshRoot.SetActive(false);
        var vertices = new List<Vector3>(SamplesPerFrame * 7);
        var triangles = new List<int>(SamplesPerFrame * 9);
        var colors = new List<Color>(SamplesPerFrame * 7);
        int work = 0;
        foreach (var lane in snapshot.Lanes)
        {
            // Count empty lanes too, so missing geometry cannot monopolize a frame.
            if (++work >= SamplesPerFrame)
            {
                PublishChunk(vertices, triangles, colors);
                work = 0;
                yield return null;
            }
            if (!snapshot.TryGetGeometry(lane.GeometryId, out var geometry)) continue;
            Color branchColor = GetBranchColor(
                appearance, lane.SectionId, lane.FlowDirection);
            IEnumerator<int> geometryBuild = AppendGeometry(
                geometry,
                branchColor,
                branchColor,
                false,
                vertices,
                triangles,
                colors);
            while (geometryBuild.MoveNext())
            {
                work += geometryBuild.Current;
                if (work < SamplesPerFrame) continue;
                PublishChunk(vertices, triangles, colors);
                work = 0;
                yield return null;
            }
        }

        foreach (var movement in snapshot.Movements)
        {
            if (!ShouldDisplayMovement(movement.Kind) ||
                !snapshot.TryGetGeometry(movement.GeometryId, out var geometry) ||
                !snapshot.TryGetLane(movement.SourceLaneId, out var sourceLane) ||
                !snapshot.TryGetLane(movement.TargetLaneId, out var targetLane))
            {
                continue;
            }

            Color sourceColor = GetBranchColor(
                appearance, sourceLane.SectionId, sourceLane.FlowDirection);
            Color targetColor = GetBranchColor(
                appearance, targetLane.SectionId, targetLane.FlowDirection);
            IEnumerator<int> geometryBuild = AppendGeometry(
                geometry,
                sourceColor,
                targetColor,
                true,
                vertices,
                triangles,
                colors);
            while (geometryBuild.MoveNext())
            {
                work += geometryBuild.Current;
                if (work < SamplesPerFrame) continue;
                PublishChunk(vertices, triangles, colors);
                work = 0;
                yield return null;
            }
        }
        PublishChunk(vertices, triangles, colors);
        meshRoot.SetActive(true);
        rebuild = null;
    }

    // Snapshot samples are already ordered in the direction of travel.
    public static void AppendSegment(List<Vector3> vertices, List<int> triangles,
        Vector3 from, Vector3 to, bool arrow)
        => AppendSegment(vertices, triangles, from, to, arrow, TrafficNodeViewStyle.Default);

    public static void AppendSegment(List<Vector3> vertices, List<int> triangles,
        Vector3 from, Vector3 to, bool arrow, TrafficNodeViewStyle style, List<Color> colors = null)
    {
        if (!IsFinite(from) || !IsFinite(to)) return;
        Vector3 direction = to - from;
        Vector3 side = Vector3.Cross(Vector3.up, direction);
        if (side.sqrMagnitude < 0.000001f) return;
        side.Normalize();
        direction.Normalize();
        style = style.Validated();
        Vector3 lift = Vector3.up * style.heightOffset;
        float halfWidth = style.lineWidth * 0.5f;
        int start = vertices.Count;
        vertices.Add(from + lift - side * halfWidth);
        vertices.Add(from + lift + side * halfWidth);
        vertices.Add(to + lift + side * halfWidth);
        vertices.Add(to + lift - side * halfWidth);
        for (int i = 0; i < 4; i++) colors?.Add(style.lineColor);
        triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
        if (arrow && style.showArrows)
            AppendArrow(vertices, triangles, colors, (from + to) * 0.5f, direction, style);
    }

    public static Color GetBranchColor(
        TrafficNodeViewStyle style,
        RoadSectionId sectionId,
        TrafficLaneFlowDirection direction)
    {
        // Lane ordinal is deliberately excluded: parallel lanes travelling the
        // same directed section belong to one branch. Both ways reuse the same
        // branch variation around their independently authored base colors.
        style = style.Validated();
        Color baseColor = direction == TrafficLaneFlowDirection.SectionStartToEnd
            ? style.startToEndInnerColor
            : style.endToStartInnerColor;
        Color.RGBToHSV(
            baseColor,
            out float baseHue,
            out float baseSaturation,
            out float baseValue);
        ulong mixed = sectionId.Value;
        mixed ^= mixed >> 30;
        mixed *= 0xbf58476d1ce4e5b9UL;
        mixed ^= mixed >> 27;
        mixed *= 0x94d049bb133111ebUL;
        mixed ^= mixed >> 31;
        float hueNoise = ToSignedUnit(mixed & 0xffffUL);
        float saturationNoise = ToSignedUnit((mixed >> 16) & 0xffffUL);
        float valueNoise = ToSignedUnit((mixed >> 32) & 0xffffUL);
        float variation = style.branchColorVariation;
        float hue = Mathf.Repeat(baseHue + hueNoise * 0.12f * variation, 1f);
        float saturation = Mathf.Clamp01(
            baseSaturation + saturationNoise * 0.12f * variation);
        float value = Mathf.Clamp01(
            baseValue + valueNoise * 0.1f * variation);
        Color result = Color.HSVToRGB(hue, saturation, value);
        result.a = baseColor.a;
        return result;
    }

    private static float ToSignedUnit(ulong value) =>
        value / 65535f * 2f - 1f;

    public static bool ShouldDisplayMovement(TrafficMovementKind kind) =>
        kind == TrafficMovementKind.Intersection ||
        kind == TrafficMovementKind.MandatoryMerge ||
        kind == TrafficMovementKind.LaneExpansion ||
        kind == TrafficMovementKind.RoadEndUTurn;

    private IEnumerator<int> AppendGeometry(
        TrafficGeometryRecord geometry,
        Color startBranchColor,
        Color endBranchColor,
        bool forceDirectionArrow,
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors)
    {
        if (geometry == null || geometry.Samples.Count < 2) yield break;

        float totalLength = Mathf.Max(geometry.LengthUnits, 0.0001f);
        float traversed = 0f;
        float nextArrowDistance = forceDirectionArrow
            ? Mathf.Min(appearance.arrowSpacing * 0.5f, totalLength * 0.5f)
            : appearance.arrowSpacing * 0.5f;
        for (int i = 1; i < geometry.Samples.Count; i++)
        {
            Vector3 from = geometry.Samples[i - 1];
            Vector3 to = geometry.Samples[i];
            float length = IsFinite(from) && IsFinite(to)
                ? Vector3.Distance(from, to)
                : 0f;
            float startT = Mathf.Clamp01(traversed / totalLength);
            float endT = Mathf.Clamp01((traversed + length) / totalLength);
            AppendTrafficSegment(
                vertices,
                triangles,
                colors,
                from,
                to,
                Color.Lerp(startBranchColor, endBranchColor, startT),
                Color.Lerp(startBranchColor, endBranchColor, endT),
                appearance);
            yield return 1;

            if (appearance.showArrows && length > 0.0001f)
            {
                while (nextArrowDistance <= traversed + length)
                {
                    float segmentDistance = nextArrowDistance - traversed;
                    AppendArrow(
                        vertices,
                        triangles,
                        colors,
                        Vector3.Lerp(from, to, segmentDistance / length),
                        (to - from).normalized,
                        appearance);
                    nextArrowDistance += appearance.arrowSpacing;
                    yield return 1;
                }
            }
            traversed += length;
        }
    }

    internal static void AppendTrafficSegment(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Vector3 from,
        Vector3 to,
        Color innerStartColor,
        Color innerEndColor,
        TrafficNodeViewStyle style)
    {
        if (!IsFinite(from) || !IsFinite(to)) return;
        Vector3 direction = to - from;
        Vector3 side = Vector3.Cross(Vector3.up, direction);
        if (side.sqrMagnitude < 0.000001f) return;
        side.Normalize();
        AppendRibbon(vertices, triangles, colors, from, to, side,
            style.lineWidth, style.heightOffset, style.lineColor, style.lineColor);
        AppendRibbon(vertices, triangles, colors, from, to, side,
            style.innerLineWidth,
            style.heightOffset + InnerLayerOffset,
            innerStartColor,
            innerEndColor);
    }

    private static void AppendRibbon(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Vector3 from,
        Vector3 to,
        Vector3 side,
        float width,
        float height,
        Color startColor,
        Color endColor)
    {
        float halfWidth = width * 0.5f;
        Vector3 lift = Vector3.up * height;
        int start = vertices.Count;
        vertices.Add(from + lift - side * halfWidth);
        vertices.Add(from + lift + side * halfWidth);
        vertices.Add(to + lift + side * halfWidth);
        vertices.Add(to + lift - side * halfWidth);
        colors.Add(startColor);
        colors.Add(startColor);
        colors.Add(endColor);
        colors.Add(endColor);
        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    private static void AppendArrow(List<Vector3> vertices, List<int> triangles,
        List<Color> colors, Vector3 center, Vector3 direction, TrafficNodeViewStyle style)
    {
        Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
        if (side.sqrMagnitude < 0.000001f) return;
        // Slightly above the ribbon so arrows retain their independent color.
        center += Vector3.up * (style.heightOffset + ArrowLayerOffset);
        int start = vertices.Count;
        vertices.Add(center + direction * (style.arrowLength * 0.5f));
        vertices.Add(center - direction * (style.arrowLength * 0.5f) + side * (style.arrowWidth * 0.5f));
        vertices.Add(center - direction * (style.arrowLength * 0.5f) - side * (style.arrowWidth * 0.5f));
        for (int i = 0; i < 3; i++) colors?.Add(style.arrowColor);
        triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
    }

    private static bool IsFinite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
        !float.IsNaN(value.z) && !float.IsInfinity(value.z);

    private void PublishChunk(List<Vector3> vertices, List<int> triangles, List<Color> colors)
    {
        if (vertices.Count == 0) return;
        var mesh = new Mesh { name = "Traffic lane overlay chunk" };
        // The snapshot is in world space; allow the view manager to be transformed.
        for (int i = 0; i < vertices.Count; i++)
            vertices[i] = meshRoot.transform.InverseTransformPoint(vertices[i]);
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        meshes.Add(mesh);
        var chunk = new GameObject("Lane chunk", typeof(MeshFilter), typeof(MeshRenderer));
        chunk.transform.SetParent(meshRoot.transform, false);
        chunk.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = chunk.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        vertices.Clear();
        triangles.Clear();
        colors.Clear();
    }

    private void ClearGeometry()
    {
        if (rebuild != null) StopCoroutine(rebuild);
        rebuild = null;
        if (meshRoot != null)
        {
            meshRoot.SetActive(false);
            Release(meshRoot);
            meshRoot = null;
        }
        foreach (var mesh in meshes) Release(mesh);
        meshes.Clear();
    }

    private void OnDisable()
    {
        Unsubscribe();
        ClearGeometry();
        if (material != null) Release(material);
        material = null;
    }

    private static void Release(Object value)
    {
        if (Application.isPlaying) Destroy(value);
        else DestroyImmediate(value);
    }
}
