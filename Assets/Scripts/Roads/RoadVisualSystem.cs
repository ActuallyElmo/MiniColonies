using System.Collections.Generic;
using UnityEngine;

public struct MeshKey
{
    public RoadType type;
    public bool isPreview;

    public MeshKey(RoadType type, bool isPreview)
    {
        this.type = type;
        this.isPreview = isPreview;
    }
    
    public override bool Equals(object obj)
    {
        return obj is MeshKey other && type == other.type && isPreview == other.isPreview;
    }

    public override int GetHashCode()
    {
        return (type != null ? type.GetHashCode() : 0) ^ isPreview.GetHashCode();
    }
}

public class MeshGroup
{
    public GameObject parentObj;
    public Mesh roadMesh, curbMesh, shadowMesh;
    
    public List<Vector3> rVerts = new List<Vector3>(), cVerts = new List<Vector3>(), sVerts = new List<Vector3>();
    public List<int> rTris = new List<int>(), cTris = new List<int>(), sTris = new List<int>();
    public List<Vector2> rUVs = new List<Vector2>(), cUVs = new List<Vector2>(), sUVs = new List<Vector2>();
    public List<Vector3> rNorms = new List<Vector3>(), cNorms = new List<Vector3>(), sNorms = new List<Vector3>();

    public void Clear()
    {
        rVerts.Clear(); rTris.Clear(); rUVs.Clear(); rNorms.Clear();
        cVerts.Clear(); cTris.Clear(); cUVs.Clear(); cNorms.Clear();
        sVerts.Clear(); sTris.Clear(); sUVs.Clear(); sNorms.Clear();
    }

    public void Apply()
    {
        roadMesh.Clear(); roadMesh.vertices = rVerts.ToArray(); roadMesh.triangles = rTris.ToArray(); roadMesh.uv = rUVs.ToArray(); roadMesh.normals = rNorms.ToArray();
        curbMesh.Clear(); curbMesh.vertices = cVerts.ToArray(); curbMesh.triangles = cTris.ToArray(); curbMesh.uv = cUVs.ToArray(); curbMesh.normals = cNorms.ToArray();
        shadowMesh.Clear(); shadowMesh.vertices = sVerts.ToArray(); shadowMesh.triangles = sTris.ToArray(); shadowMesh.uv = sUVs.ToArray(); shadowMesh.normals = sNorms.ToArray();
    }
}

public class RoadVisualSystem : MonoBehaviour
{
    public static RoadVisualSystem Instance { get; private set; }

    [Header("Visual Settings")]
    public float yOffset = 0.05f;
    [Range(0, 5)] public int smoothingIterations = 3;
    public Material shadowMaterial;
    public Vector3 shadowOffset = new Vector3(0.15f, -0.02f, -0.15f);

    [Header("Preview Settings")]
    public Material previewRoadMaterial;
    public Material previewCurbMaterial;

    // Use the custom struct key so temporary meshes get isolated
    public Dictionary<Vector2Int, Dictionary<MeshKey, MeshGroup>> ChunkGroups { get; private set; } = new Dictionary<Vector2Int, Dictionary<MeshKey, MeshGroup>>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        if (RoadSystemBackend.Instance != null)
            RoadSystemBackend.Instance.OnChunksDirty += HandleDirtyChunks;
    }

    private void OnDestroy()
    {
        if (RoadSystemBackend.Instance != null)
            RoadSystemBackend.Instance.OnChunksDirty -= HandleDirtyChunks;
    }

    private void HandleDirtyChunks(HashSet<Vector2Int> dirtyChunks)
    {
        RoadMeshGenerationTask task = new RoadMeshGenerationTask(dirtyChunks, RoadSystemBackend.Instance.Roads);
        SimulationTaskManager.Instance.EnqueueTask(task);
    }

    public MeshGroup GetOrCreateMeshGroup(RoadType type, Vector2Int chunkPos, bool isPreview = false)
    {
        if (!ChunkGroups.ContainsKey(chunkPos)) ChunkGroups[chunkPos] = new Dictionary<MeshKey, MeshGroup>();
        
        MeshKey key = new MeshKey(type, isPreview);
        if (ChunkGroups[chunkPos].TryGetValue(key, out MeshGroup group)) return group;

        group = new MeshGroup();
        string suffix = isPreview ? "_Preview" : "";
        group.parentObj = new GameObject($"RoadGroup_Chunk{chunkPos.x}_{chunkPos.y}_{type.roadName}{suffix}");
        group.parentObj.transform.SetParent(this.transform);

        // Setup Unity Components
        MeshFilter rF = group.parentObj.AddComponent<MeshFilter>();
        MeshRenderer rR = group.parentObj.AddComponent<MeshRenderer>();
        rR.sharedMaterial = (isPreview && previewRoadMaterial != null) ? previewRoadMaterial : type.roadMaterial;
        group.roadMesh = new Mesh(); rF.sharedMesh = group.roadMesh;

        GameObject curbObj = new GameObject("Curb"); curbObj.transform.SetParent(group.parentObj.transform);
        MeshFilter cF = curbObj.AddComponent<MeshFilter>();
        MeshRenderer cR = curbObj.AddComponent<MeshRenderer>();
        cR.sharedMaterial = (isPreview && previewCurbMaterial != null) ? previewCurbMaterial : type.curbMaterial;
        group.curbMesh = new Mesh(); cF.sharedMesh = group.curbMesh;

        GameObject shadowObj = new GameObject("Shadow"); shadowObj.transform.SetParent(group.parentObj.transform);
        MeshFilter sF = shadowObj.AddComponent<MeshFilter>();
        MeshRenderer sR = shadowObj.AddComponent<MeshRenderer>();
        sR.sharedMaterial = shadowMaterial;
        group.shadowMesh = new Mesh(); sF.sharedMesh = group.shadowMesh;

        ChunkGroups[chunkPos].Add(key, group);
        return group;
    }
}