using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Reusable data-derived world overlay. Work uses real frames, including during pause.
[DisallowMultipleComponent, RequireComponent(typeof(WorldManager))]
public sealed class WorldPlacementHighlight : MonoBehaviour
{
    private WorldManager world;
    private sealed class Outline
    {
        public GameObject root;
        public Coroutine work;
        public readonly List<Mesh> meshes = new List<Mesh>();
    }

    private readonly Outline hovered = new Outline();
    private readonly Outline selected = new Outline();
    private readonly Dictionary<object, Outline> ranges = new Dictionary<object, Outline>();
    private readonly System.Diagnostics.Stopwatch workTimer = new System.Diagnostics.Stopwatch();
    private int workFrame = -1;
    private static readonly int AnimationTimeId = Shader.PropertyToID("_WorldPlacementHighlightTime");
    private static readonly int PixelDensityId = Shader.PropertyToID("_PixelsPerWorldUnit");
    private static readonly int SolidOutlineId = Shader.PropertyToID("_SolidOutline");

    private void OnEnable()
    {
        world = GetComponent<WorldManager>();
        world.ResourcesChanged += ClearAll;
    }

    private void OnDisable()
    {
        if (world != null) world.ResourcesChanged -= ClearAll;
        ClearAll();
    }

    private void Update()
    {
        // One allocation-free shader update, independent of footprint/batch size.
        // Animation changes only the mask; published meshes never rebuild for it.
        if (hovered.root != null || ranges.Count > 0) Shader.SetGlobalFloat(AnimationTimeId, Time.unscaledTime);
    }

    // Call only when the footprint changes; Clear cancels pending work as well as visible meshes.
    public void Show(IEnumerable<Vector2Int> cells, Material material, Sprite edge, Sprite innerCorner,
        Color color, float heightOffset = 0.025f)
    {
        ShowOutline(hovered, false, cells, material, edge, innerCorner, color, heightOffset);
    }

    public void ShowSelection(IEnumerable<Vector2Int> cells, Material material, Sprite edge, Sprite innerCorner,
        Color color, float heightOffset = 0.025f)
    {
        ShowOutline(selected, true, cells, material, edge, innerCorner, color, heightOffset);
    }

    // Independent caller-owned channels let preview, hover and selection share
    // the same animated perimeter renderer without clearing each other's range.
    public void ShowRange(object key, IEnumerable<Vector2Int> cells, Material material, Sprite edge, Sprite innerCorner,
        Color color, float heightOffset = 0.04f)
    {
        if (key == null || !isActiveAndEnabled) return;
        if (!ranges.TryGetValue(key, out Outline outline))
        {
            outline = new Outline();
            ranges.Add(key, outline);
        }
        ShowOutline(outline, false, cells, material, edge, innerCorner, color, heightOffset);
    }

    public void ClearRange(object key)
    {
        if (key == null || !ranges.TryGetValue(key, out Outline outline)) return;
        ClearOutline(outline);
        ranges.Remove(key);
    }

    private void ShowOutline(Outline outline, bool solid, IEnumerable<Vector2Int> cells, Material material,
        Sprite edge, Sprite innerCorner, Color color, float heightOffset)
    {
        ClearOutline(outline);
        if (!isActiveAndEnabled || world == null || cells == null || material == null || edge == null) return;
        // A property block binds one atlas; incompatible optional corners fall back safely.
        if (innerCorner != null && innerCorner.texture != edge.texture) innerCorner = null;
        heightOffset = IsFinite(heightOffset) ? Mathf.Max(0f, heightOffset) : 0.025f;
        outline.work = StartCoroutine(Build(outline, solid, cells, material, edge, innerCorner, color, heightOffset));
    }

    public void Clear() => ClearOutline(hovered);
    public void ClearSelection() => ClearOutline(selected);

    public void ClearAll()
    {
        Clear();
        ClearSelection();
        foreach (Outline outline in ranges.Values) ClearOutline(outline);
        ranges.Clear();
    }

    private void ClearOutline(Outline outline)
    {
        if (outline.work != null) StopCoroutine(outline.work);
        outline.work = null;
        if (outline.root != null)
        {
            outline.root.SetActive(false);
            Release(outline.root);
            outline.root = null;
        }
        foreach (Mesh mesh in outline.meshes) if (mesh != null) Release(mesh);
        outline.meshes.Clear();
    }

    private IEnumerator Build(Outline outline, bool solid, IEnumerable<Vector2Int> cells, Material material, Sprite edge,
        Sprite innerCorner, Color color, float heightOffset)
    {
        // Establish the handle before a small build can complete.
        yield return null;
        outline.root = new GameObject(solid ? "World Placement Selection" : "World Placement Highlight");
        outline.root.transform.SetParent(world.transform, true);
        Shader.SetGlobalFloat(AnimationTimeId, Time.unscaledTime);
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();
        BeginWorkFrame();
        int subdivisions = world.TerrainMeshSubdivisions;
        float budget = IsFinite(world.maxMillisecondsPerFrame) ? Mathf.Max(0.1f, world.maxMillisecondsPerFrame) : 1f;
        var properties = new MaterialPropertyBlock();
        properties.SetColor("_Color", color);
        properties.SetFloat(SolidOutlineId, solid ? 1f : 0f);
        properties.SetTexture("_MainTex", edge.texture);
        float cellSize = IsFinite(world.cellSize) && world.cellSize > 0f ? world.cellSize : 1f;
        properties.SetFloat(PixelDensityId, edge.rect.width / Mathf.Max(0.0001f, cellSize));
        var footprint = new HashSet<Vector2Int>();
        int visited = 0;
        if (workTimer.Elapsed.TotalMilliseconds >= budget)
        {
            yield return null;
            BeginWorkFrame();
        }
        foreach (Vector2Int cell in cells)
        {
            footprint.Add(cell);
            if (++visited % 64 == 0 && workTimer.Elapsed.TotalMilliseconds >= budget)
            {
                yield return null;
                BeginWorkFrame();
            }
        }
        visited = 0;
        foreach (Vector2Int cell in footprint)
        {
            int start = vertices.Count;
            WorldPlacementOutlineGeometry.AppendCell(cell, subdivisions, edge, vertices, uvs, indices, footprint, innerCorner);
            for (int i = start; i < vertices.Count; i++)
            {
                Vector3 point = vertices[i];
                vertices[i] = new Vector3(point.x * world.cellSize,
                    world.GetPhysicalHeight(point.x, point.z) * world.heightStep + heightOffset, point.z * world.cellSize);
            }
            if (++visited % 16 == 0 || visited == footprint.Count)
            {
                if (indices.Count > 0)
                {
                    var mesh = new Mesh { name = "World placement perimeter" };
                    mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
                    outline.meshes.Add(mesh);
                    var part = new GameObject("Perimeter", typeof(MeshFilter), typeof(MeshRenderer));
                    part.layer = 2; // Ignore Raycast; feedback has no collider.
                    part.transform.SetParent(outline.root.transform, false);
                    part.GetComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = part.GetComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.sortingOrder = solid ? 1 : 0;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.SetPropertyBlock(properties);
                }
                vertices.Clear(); uvs.Clear(); indices.Clear();
            }
            if (workTimer.Elapsed.TotalMilliseconds >= budget)
            {
                yield return null;
                BeginWorkFrame();
            }
        }
        outline.work = null;
    }

    private void BeginWorkFrame()
    {
        // Hover and selection share the same real-time build allowance.
        if (workFrame == Time.frameCount) return;
        workFrame = Time.frameCount;
        workTimer.Restart();
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static void Release(Object value)
    {
        if (Application.isPlaying) Destroy(value);
        else DestroyImmediate(value);
    }
}
