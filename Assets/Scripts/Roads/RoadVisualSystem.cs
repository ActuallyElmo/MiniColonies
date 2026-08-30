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
    public Mesh roadMesh, curbMesh, shadowMesh, markingMesh;

    public void Dispose()
    {
        DestroyRuntimeMesh(roadMesh);
        DestroyRuntimeMesh(curbMesh);
        DestroyRuntimeMesh(shadowMesh);
        DestroyRuntimeMesh(markingMesh);
        roadMesh = null;
        curbMesh = null;
        shadowMesh = null;
        markingMesh = null;
    }

    private static void DestroyRuntimeMesh(Mesh mesh)
    {
        if (mesh == null) return;

        if (Application.isPlaying) Object.Destroy(mesh);
        else Object.DestroyImmediate(mesh);
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

    public Dictionary<Vector2Int, Dictionary<MeshKey, MeshGroup>> ChunkGroups { get; private set; } = new Dictionary<Vector2Int, Dictionary<MeshKey, MeshGroup>>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
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

        if (Instance != this) return;

        foreach (Dictionary<MeshKey, MeshGroup> groupsByType in ChunkGroups.Values)
        {
            foreach (MeshGroup group in groupsByType.Values)
            {
                group?.Dispose();
            }
        }

        ChunkGroups.Clear();
        Instance = null;
    }

    private void HandleDirtyChunks(HashSet<Vector2Int> dirtyChunks)
    {
        if (RoadSystemBackend.Instance == null ||
            SimulationTaskManager.Instance == null)
        {
            return;
        }

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

        GameObject markingObj = new GameObject("RoadMarkings"); markingObj.transform.SetParent(group.parentObj.transform);
        MeshFilter mF = markingObj.AddComponent<MeshFilter>();
        MeshRenderer mR = markingObj.AddComponent<MeshRenderer>();
        mR.sharedMaterial = GetRoadMarkingMaterial(type, isPreview);
        group.markingMesh = new Mesh(); mF.sharedMesh = group.markingMesh;

        ChunkGroups[chunkPos].Add(key, group);
        return group;
    }

    private Material GetRoadMarkingMaterial(RoadType type, bool isPreview)
    {
        if (isPreview && previewCurbMaterial != null) return previewCurbMaterial;
        if (type == null) return null;
        if (type.roadMarkingMaterial != null) return type.roadMarkingMaterial;
        if (type.curbMaterial != null) return type.curbMaterial;
        return type.roadMaterial;
    }
}
