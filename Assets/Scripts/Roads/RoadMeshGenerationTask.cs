using UnityEngine;
using System.Diagnostics;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;

public struct EdgeHash : System.IEquatable<EdgeHash>
{
    public Vector2Int A, B;
    public EdgeHash(Vector2Int a, Vector2Int b)
    {
        if (a.x < b.x || (a.x == b.x && a.y < b.y)) { A = a; B = b; } else { A = b; B = a; }
    }
    public bool Equals(EdgeHash other) => A == other.A && B == other.B;
    public override int GetHashCode() => (A.GetHashCode() * 397) ^ B.GetHashCode();
}

public class RoadMeshGenerationTask : ISimulationTask, System.IDisposable
{
    private const float TrafficIntersectionStopLinePaddingUnits = 0.05f;
    private const float TrafficMaximumNodePullback = 0.95f;
    private const float TrafficMinimumBoundarySeparationUnits = 0.15f;

    private enum TaskState { Init, TraceAndSmooth, GroupSegments, ScheduleJobs, WaitForJobs, Complete }
    private TaskState _state = TaskState.Init;

    private HashSet<Vector2Int> _dirtyChunks;
    private Dictionary<Vector2Int, RoadCell> _roadData;
    private WorldManager _wm;
    private RoadVisualSystem _vis;

    private List<RoadCell> _roadsToProcess;
    private HashSet<EdgeHash> _visitedSegments = new HashSet<EdgeHash>();
    
    // Main Thread Path Data (NEW: Added isPreview flag)
    private struct LogicalPath
    {
        public List<PathNodeNative> nodes;
        public RoadType type;
        public bool isPreview;
        public bool startsAtIntersection;
        public bool endsAtIntersection;
        public bool useStandardNodeSmoothing;
        public float startIntersectionPullback;
        public float endIntersectionPullback;
        public float startMarkingPullback;
        public float endMarkingPullback;
    }
    private List<LogicalPath> _allSmoothedPaths = new List<LogicalPath>();
    
    // Grouped Data for Jobs (NEW: Grouped by MeshKey instead of RoadType)
    private Dictionary<Vector2Int, Dictionary<MeshKey, List<SegmentNative>>> _chunkedSegments = new Dictionary<Vector2Int, Dictionary<MeshKey, List<SegmentNative>>>();
    private Dictionary<Vector2Int, Dictionary<MeshKey, List<PathNodeNative>>> _chunkedCaps = new Dictionary<Vector2Int, Dictionary<MeshKey, List<PathNodeNative>>>();
    private Dictionary<Vector2Int, Dictionary<MeshKey, List<TransitionMarkingNative>>> _chunkedTransitionMarkings =
        new Dictionary<Vector2Int, Dictionary<MeshKey, List<TransitionMarkingNative>>>();

    // Job Management
    private JobHandle _masterJobHandle;
    private List<RoadChunkMeshJob> _scheduledJobs = new List<RoadChunkMeshJob>();
    private List<MeshGroup> _targetMeshGroups = new List<MeshGroup>();

    public struct PathNodeNative
    {
        public float3 position;
        public float roadWidth;
        public float curbWidth;
    }

    public struct SegmentNative
    {
        public float3 p0_pos, p0_fwd;
        public float p0_w, p0_c;
        public float3 p1_pos, p1_fwd;
        public float p1_w, p1_c;
        public float p0_marking_w;
        public float p1_marking_w;
        public int lanesPerWay;
        public int isTwoWay;
        public int showCenterDivider;
        public int showLaneDividers;
        public int showStopLinesAtIntersections;
        public float p0Distance;
        public float p1Distance;
        public float pathLength;
        public float startIntersectionPullback;
        public float endIntersectionPullback;
        public float startMarkingPullback;
        public float endMarkingPullback;
        public float centerDividerWidth;
        public float laneDividerWidth;
        public float laneDividerDashLength;
        public float laneDividerGapLength;
        public float stopLineWidth;
        public float markingSurfaceOffset;
    }

    public struct TransitionMarkingNative
    {
        public float3 p0_pos, p0_fwd;
        public float p0_w;
        public int p0_lanesPerWay;
        public int p0_isTwoWay;
        public float3 p1_pos, p1_fwd;
        public float p1_w;
        public int p1_lanesPerWay;
        public int p1_isTwoWay;
        public int showCenterDivider;
        public int showLaneDividers;
        public float centerDividerWidth;
        public float laneDividerWidth;
        public float laneDividerDashLength;
        public float laneDividerGapLength;
        public float markingSurfaceOffset;
    }

    public RoadMeshGenerationTask(
        HashSet<Vector2Int> chunks,
        IReadOnlyDictionary<Vector2Int, RoadCell> roads)
    {
        _dirtyChunks = ExpandDirtyChunks(chunks);
        _roadData = new Dictionary<Vector2Int, RoadCell>(roads.Count);
        foreach (KeyValuePair<Vector2Int, RoadCell> pair in roads)
        {
            _roadData.Add(pair.Key, pair.Value);
        }
        _wm = WorldManager.Instance;
        _vis = RoadVisualSystem.Instance;
        _roadsToProcess = new List<RoadCell>(_roadData.Values);
    }

    private static HashSet<Vector2Int> ExpandDirtyChunks(HashSet<Vector2Int> source)
    {
        var expanded = new HashSet<Vector2Int>();
        if (source == null) return expanded;

        foreach (Vector2Int chunk in source)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    expanded.Add(new Vector2Int(chunk.x + dx, chunk.y + dz));
                }
            }
        }

        return expanded;
    }

    public bool Process(Stopwatch timer, float maxMillisecondsPerFrame)
    {
        if (_state == TaskState.Init)
        {
            _state = TaskState.TraceAndSmooth;
        }

        if (_state == TaskState.TraceAndSmooth)
        {
            List<LogicalPath> rawPaths = new List<LogicalPath>();
            
            foreach (RoadCell cell in _roadsToProcess)
            {
                if (IsVisualBoundary(cell))
                {
                    for (int i = 0; i < 8; i++)
                    {
                        int bit = 1 << i;
                        if (cell.HasConnection(bit))
                        {
                            Vector2Int nPos = RoadSystemBackend.Instance.GetNeighborPosition(cell.gridPosition, bit);
                            EdgeHash hash = new EdgeHash(cell.gridPosition, nPos);
                            if (!_visitedSegments.Contains(hash)) rawPaths.Add(TracePath(cell, nPos));
                        }
                    }
                }
            }

            foreach (RoadCell cell in _roadsToProcess)
            {
                if (!IsVisualBoundary(cell))
                {
                    for (int i = 0; i < 8; i++)
                    {
                        int bit = 1 << i;
                        if (cell.HasConnection(bit))
                        {
                            Vector2Int nPos = RoadSystemBackend.Instance.GetNeighborPosition(cell.gridPosition, bit);
                            EdgeHash hash = new EdgeHash(cell.gridPosition, nPos);
                            if (!_visitedSegments.Contains(hash)) rawPaths.Add(TracePath(cell, nPos));
                        }
                    }
                }
            }

            foreach (var path in rawPaths)
            {
                bool useWideSmoothing =
                    path.type != null &&
                    path.type.IsWide &&
                    !path.useStandardNodeSmoothing;
                float cornerCutRatio = RoadGeometryRules.GetCornerCutRatio(
                    useWideSmoothing);
                int smoothingIterations =
                    RoadGeometryRules.GetSmoothingIterations(
                        _vis.smoothingIterations,
                        useWideSmoothing);
                var smoothedNodes = ApplyChaikin(
                    path.nodes,
                    smoothingIterations,
                    cornerCutRatio);

                // EXACT Terrain Height Mapping:
                // Instead of projecting onto linear segments, we sample the exact WorldManager terrain.
                // This guarantees the visual road mesh perfectly traces the ground geometry, including
                // diagonal cliffs and curved corners, completely eliminating ground clipping.
                for (int i = 0; i < smoothedNodes.Count; i++)
                {
                    PathNodeNative n = smoothedNodes[i];
                    float px = n.position.x / _wm.cellSize;
                    float pz = n.position.z / _wm.cellSize;
                    n.position.y = _wm.GetPhysicalHeight(px, pz) * _wm.heightStep;
                    smoothedNodes[i] = n;
                }

                _allSmoothedPaths.Add(new LogicalPath {
                    type = path.type,
                    isPreview = path.isPreview, 
                    startsAtIntersection = path.startsAtIntersection,
                    endsAtIntersection = path.endsAtIntersection,
                    useStandardNodeSmoothing = path.useStandardNodeSmoothing,
                    startIntersectionPullback = path.startIntersectionPullback,
                    endIntersectionPullback = path.endIntersectionPullback,
                    startMarkingPullback = path.startMarkingPullback,
                    endMarkingPullback = path.endMarkingPullback,
                    nodes = smoothedNodes
                });
            }

            _state = TaskState.GroupSegments;
        }

        if (_state == TaskState.GroupSegments)
        {
            float chunkWorldSize = _wm.chunkSize * _wm.cellSize;

            foreach (var path in _allSmoothedPaths)
            {
                bool isClosedLoop = Vector3.Distance(path.nodes[0].position, path.nodes[path.nodes.Count - 1].position) < 0.01f;
                MeshKey segKey = new MeshKey(path.type, path.isPreview); // Create isolated key
                float[] cumulativeDistances = BuildCumulativeDistances(path.nodes);
                float pathLength = cumulativeDistances.Length > 0
                    ? cumulativeDistances[cumulativeDistances.Length - 1]
                    : 0f;

                for (int i = 0; i < path.nodes.Count - 1; i++)
                {
                    float3 f0 = GetForward(path.nodes, i, isClosedLoop);
                    float3 f1 = GetForward(path.nodes, i + 1, isClosedLoop);

                    PathNodeNative n0 = path.nodes[i];
                    PathNodeNative n1 = path.nodes[i + 1];
                    RoadType type = path.type;

                    SegmentNative seg = new SegmentNative {
                        p0_pos = n0.position, p0_fwd = f0, p0_w = n0.roadWidth, p0_c = n0.curbWidth,
                        p1_pos = n1.position, p1_fwd = f1, p1_w = n1.roadWidth, p1_c = n1.curbWidth,
                        p0_marking_w = type != null ? type.roadWidth : n0.roadWidth,
                        p1_marking_w = type != null ? type.roadWidth : n1.roadWidth,
                        lanesPerWay = type != null ? Mathf.Max(1, type.lanesPerWay) : 1,
                        isTwoWay = type != null && type.UsesTwoWayTraffic ? 1 : 0,
                        showCenterDivider = type != null && type.showCenterDivider ? 1 : 0,
                        showLaneDividers = type != null && type.showLaneDividers ? 1 : 0,
                        showStopLinesAtIntersections = type != null && type.showStopLinesAtIntersections ? 1 : 0,
                        p0Distance = cumulativeDistances[i],
                        p1Distance = cumulativeDistances[i + 1],
                        pathLength = pathLength,
                        startIntersectionPullback = path.startsAtIntersection ? path.startIntersectionPullback : 0f,
                        endIntersectionPullback = path.endsAtIntersection ? path.endIntersectionPullback : 0f,
                        startMarkingPullback = path.startMarkingPullback,
                        endMarkingPullback = path.endMarkingPullback,
                        centerDividerWidth = type != null ? type.centerDividerWidth : 0.025f,
                        laneDividerWidth = type != null ? type.laneDividerWidth : 0.012f,
                        laneDividerDashLength = type != null ? type.laneDividerDashLength : 0.18f,
                        laneDividerGapLength = type != null ? type.laneDividerGapLength : 0.14f,
                        stopLineWidth = type != null ? type.stopLineWidth : 0.035f,
                        markingSurfaceOffset = type != null ? type.markingSurfaceOffset : 0.05f
                    };

                    Vector3 midPos = (n0.position + n1.position) * 0.5f;
                    Vector2Int cPos = new Vector2Int(Mathf.FloorToInt(midPos.x / chunkWorldSize), Mathf.FloorToInt(midPos.z / chunkWorldSize));

                    if (_dirtyChunks.Contains(cPos))
                    {
                        if (!_chunkedSegments.ContainsKey(cPos)) _chunkedSegments[cPos] = new Dictionary<MeshKey, List<SegmentNative>>();
                        if (!_chunkedSegments[cPos].ContainsKey(segKey)) _chunkedSegments[cPos][segKey] = new List<SegmentNative>();
                        _chunkedSegments[cPos][segKey].Add(seg);
                    }
                }
            }

            AddRoadTypeTransitionVisuals(chunkWorldSize);

            foreach (var cell in _roadsToProcess)
            {
                if (IsCapNode(cell))
                {
                    Vector2Int cPos = new Vector2Int(Mathf.FloorToInt((float)cell.gridPosition.x / _wm.chunkSize), Mathf.FloorToInt((float)cell.gridPosition.y / _wm.chunkSize));
                    if (!_dirtyChunks.Contains(cPos)) continue;

                    float capWidth = cell.roadType.roadWidth;
                    float capCurb = cell.roadType.curbWidth;
                    RoadNodeKind nodeKind = RoadSystemBackend.Instance.GetRoadNodeKind(cell);
                    if (nodeKind == RoadNodeKind.Intersection)
                    {
                        GetMaxIntersectionWidth(cell, out capWidth, out capCurb);
                        RoadType capStyleType = GetIntersectionCapStyleType(cell);
                        MeshKey intersectionCapKey = new MeshKey(capStyleType, cell.isPreview);
                        AddCap(cPos, intersectionCapKey, GetCellWorldCenter(cell.gridPosition), capWidth, capCurb);
                    }
                    else
                    {
                        MeshKey capKey = new MeshKey(cell.roadType, cell.isPreview); // Create isolated key

                        AddCap(cPos, capKey, GetCellWorldCenter(cell.gridPosition), capWidth, capCurb);
                    }
                }
            }

            _state = TaskState.ScheduleJobs;
        }

        if (_state == TaskState.ScheduleJobs)
        {
            NativeList<JobHandle> jobHandles = new NativeList<JobHandle>(Allocator.Temp);

            foreach (var chunkPos in _dirtyChunks)
            {
                HashSet<MeshKey> keysInChunk = new HashSet<MeshKey>();
                if (_chunkedSegments.ContainsKey(chunkPos)) keysInChunk.UnionWith(_chunkedSegments[chunkPos].Keys);
                if (_chunkedCaps.ContainsKey(chunkPos)) keysInChunk.UnionWith(_chunkedCaps[chunkPos].Keys);
                if (_chunkedTransitionMarkings.ContainsKey(chunkPos)) keysInChunk.UnionWith(_chunkedTransitionMarkings[chunkPos].Keys);

                foreach (MeshKey key in keysInChunk)
                {
                    // Pass the isolated parameters!
                    MeshGroup group = _vis.GetOrCreateMeshGroup(key.type, chunkPos, key.isPreview);
                    _targetMeshGroups.Add(group);

                    List<SegmentNative> segments = _chunkedSegments.ContainsKey(chunkPos) && _chunkedSegments[chunkPos].ContainsKey(key) 
                        ? _chunkedSegments[chunkPos][key] : new List<SegmentNative>();
                        
                    List<PathNodeNative> caps = _chunkedCaps.ContainsKey(chunkPos) && _chunkedCaps[chunkPos].ContainsKey(key) 
                        ? _chunkedCaps[chunkPos][key] : new List<PathNodeNative>();

                    List<TransitionMarkingNative> transitionMarkings =
                        _chunkedTransitionMarkings.ContainsKey(chunkPos) &&
                        _chunkedTransitionMarkings[chunkPos].ContainsKey(key)
                        ? _chunkedTransitionMarkings[chunkPos][key]
                        : new List<TransitionMarkingNative>();

                    var job = new RoadChunkMeshJob
                    {
                        segments = new NativeArray<SegmentNative>(segments.ToArray(), Allocator.TempJob),
                        caps = new NativeArray<PathNodeNative>(caps.ToArray(), Allocator.TempJob),
                        transitionMarkings = new NativeArray<TransitionMarkingNative>(transitionMarkings.ToArray(), Allocator.TempJob),
                        
                        yOffset = _vis.yOffset,
                        shadowPlanarOffset = new float2(_vis.shadowOffset.x, _vis.shadowOffset.z),
                        shadowYOffset = _vis.shadowOffset.y,

                        rVerts = new NativeList<float3>(Allocator.TempJob), rNorms = new NativeList<float3>(Allocator.TempJob), rUVs = new NativeList<float2>(Allocator.TempJob), rTris = new NativeList<int>(Allocator.TempJob),
                        cVerts = new NativeList<float3>(Allocator.TempJob), cNorms = new NativeList<float3>(Allocator.TempJob), cUVs = new NativeList<float2>(Allocator.TempJob), cTris = new NativeList<int>(Allocator.TempJob),
                        sVerts = new NativeList<float3>(Allocator.TempJob), sNorms = new NativeList<float3>(Allocator.TempJob), sUVs = new NativeList<float2>(Allocator.TempJob), sTris = new NativeList<int>(Allocator.TempJob),
                        mVerts = new NativeList<float3>(Allocator.TempJob), mNorms = new NativeList<float3>(Allocator.TempJob), mUVs = new NativeList<float2>(Allocator.TempJob), mTris = new NativeList<int>(Allocator.TempJob)
                    };

                    _scheduledJobs.Add(job);
                    jobHandles.Add(job.Schedule());
                }
            }

            if (jobHandles.Length > 0)
            {
                _masterJobHandle = JobHandle.CombineDependencies(jobHandles.AsArray());
                SimulationTaskManager.Instance.RegisterJob(_masterJobHandle);
            }
            jobHandles.Dispose();

            _state = TaskState.WaitForJobs;
        }

        if (_state == TaskState.WaitForJobs)
        {
            if (_scheduledJobs.Count > 0 && !_masterJobHandle.IsCompleted) return false;
            
            _masterJobHandle.Complete();

            foreach (Vector2Int chunk in _dirtyChunks)
            {
                if (_vis.ChunkGroups.TryGetValue(chunk, out var typeDict))
                {
                    foreach (var kvp in typeDict)
                    {
                        kvp.Value.roadMesh.Clear();
                        kvp.Value.curbMesh.Clear();
                        kvp.Value.shadowMesh.Clear();
                        kvp.Value.markingMesh.Clear();
                    }
                }
            }

            for (int i = 0; i < _scheduledJobs.Count; i++)
            {
                var job = _scheduledJobs[i];
                var group = _targetMeshGroups[i];

                group.roadMesh.SetVertices(job.rVerts.AsArray());
                group.roadMesh.SetNormals(job.rNorms.AsArray());
                group.roadMesh.SetUVs(0, job.rUVs.AsArray());
                group.roadMesh.SetIndices(job.rTris.AsArray(), MeshTopology.Triangles, 0);

                group.curbMesh.SetVertices(job.cVerts.AsArray());
                group.curbMesh.SetNormals(job.cNorms.AsArray());
                group.curbMesh.SetUVs(0, job.cUVs.AsArray());
                group.curbMesh.SetIndices(job.cTris.AsArray(), MeshTopology.Triangles, 0);

                group.shadowMesh.SetVertices(job.sVerts.AsArray());
                group.shadowMesh.SetNormals(job.sNorms.AsArray());
                group.shadowMesh.SetUVs(0, job.sUVs.AsArray());
                group.shadowMesh.SetIndices(job.sTris.AsArray(), MeshTopology.Triangles, 0);

                LiftMarkingVerticesAboveTerrain(job.mVerts);
                group.markingMesh.SetVertices(job.mVerts.AsArray());
                group.markingMesh.SetNormals(job.mNorms.AsArray());
                group.markingMesh.SetUVs(0, job.mUVs.AsArray());
                group.markingMesh.SetIndices(job.mTris.AsArray(), MeshTopology.Triangles, 0);

            }

            DisposeScheduledJobs();
            _state = TaskState.Complete;
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_scheduledJobs.Count > 0)
        {
            _masterJobHandle.Complete();
        }

        DisposeScheduledJobs();
    }

    private void DisposeScheduledJobs()
    {
        for (int i = 0; i < _scheduledJobs.Count; i++)
        {
            RoadChunkMeshJob job = _scheduledJobs[i];
            if (job.segments.IsCreated) job.segments.Dispose();
            if (job.caps.IsCreated) job.caps.Dispose();
            if (job.transitionMarkings.IsCreated)
                job.transitionMarkings.Dispose();
            if (job.rVerts.IsCreated) job.rVerts.Dispose();
            if (job.rNorms.IsCreated) job.rNorms.Dispose();
            if (job.rUVs.IsCreated) job.rUVs.Dispose();
            if (job.rTris.IsCreated) job.rTris.Dispose();
            if (job.cVerts.IsCreated) job.cVerts.Dispose();
            if (job.cNorms.IsCreated) job.cNorms.Dispose();
            if (job.cUVs.IsCreated) job.cUVs.Dispose();
            if (job.cTris.IsCreated) job.cTris.Dispose();
            if (job.sVerts.IsCreated) job.sVerts.Dispose();
            if (job.sNorms.IsCreated) job.sNorms.Dispose();
            if (job.sUVs.IsCreated) job.sUVs.Dispose();
            if (job.sTris.IsCreated) job.sTris.Dispose();
            if (job.mVerts.IsCreated) job.mVerts.Dispose();
            if (job.mNorms.IsCreated) job.mNorms.Dispose();
            if (job.mUVs.IsCreated) job.mUVs.Dispose();
            if (job.mTris.IsCreated) job.mTris.Dispose();
        }

        _scheduledJobs.Clear();
        _targetMeshGroups.Clear();
    }

    // --- MAIN THREAD HELPERS ---

    private void LiftMarkingVerticesAboveTerrain(NativeList<float3> verts)
    {
        if (_wm == null || _vis == null || !verts.IsCreated) return;

        float minimumClearance = _vis.yOffset + 0.02f;
        for (int i = 0; i < verts.Length; i++)
        {
            float3 vertex = verts[i];
            float px = vertex.x / _wm.cellSize;
            float pz = vertex.z / _wm.cellSize;
            float terrainY = _wm.GetPhysicalHeight(px, pz) * _wm.heightStep;
            float minimumY = terrainY + minimumClearance;
            if (vertex.y < minimumY)
            {
                vertex.y = minimumY;
                verts[i] = vertex;
            }
        }
    }

    private bool IsVisualBoundary(RoadCell cell)
    {
        if (cell == null) return false;
        if (RoadSystemBackend.Instance.GetRoadNodeKind(cell) !=
            RoadNodeKind.ThroughRoad)
        {
            return true;
        }

        return OwnsRoadTypeTransitionBoundary(cell);
    }

    private bool IsCapNode(RoadCell cell)
    {
        if (cell == null) return false;
        return RoadSystemBackend.Instance.GetConnectionCount(cell) != 2 ||
               RoadSystemBackend.Instance.GetRoadNodeKind(cell) ==
               RoadNodeKind.Intersection;
    }

    private bool OwnsRoadTypeTransitionBoundary(RoadCell cell)
    {
        if (cell == null || cell.roadType == null) return false;
        if (RoadSystemBackend.Instance.GetConnectionCount(cell) != 2) return false;

        for (int i = 0; i < 8; i++)
        {
            int bit = 1 << i;
            if (!cell.HasConnection(bit)) continue;

            Vector2Int neighborPos = RoadSystemBackend.Instance.GetNeighborPosition(cell.gridPosition, bit);
            if (!_roadData.TryGetValue(neighborPos, out RoadCell neighbor) ||
                neighbor == null ||
                neighbor.roadType == null ||
                neighbor.roadType == cell.roadType ||
                RoadSystemBackend.Instance.GetRoadNodeKind(neighbor) == RoadNodeKind.Intersection)
            {
                continue;
            }

            if (CompareCells(cell.gridPosition, neighborPos) < 0)
            {
                return true;
            }
        }

        return false;
    }

    private void AddRoadTypeTransitionVisuals(float chunkWorldSize)
    {
        foreach (RoadCell cell in _roadsToProcess)
        {
            if (cell == null || cell.roadType == null) continue;

            for (int i = 0; i < 8; i++)
            {
                int bit = 1 << i;
                if (!cell.HasConnection(bit)) continue;

                Vector2Int neighborPos = RoadSystemBackend.Instance.GetNeighborPosition(cell.gridPosition, bit);
                if (!_roadData.TryGetValue(neighborPos, out RoadCell neighbor) ||
                    neighbor == null ||
                    neighbor.roadType == null ||
                    neighbor.roadType == cell.roadType ||
                    neighbor.isPreview != cell.isPreview ||
                    RoadSystemBackend.Instance.GetRoadNodeKind(cell) == RoadNodeKind.Intersection ||
                    RoadSystemBackend.Instance.GetRoadNodeKind(neighbor) == RoadNodeKind.Intersection ||
                    CompareCells(cell.gridPosition, neighbor.gridPosition) >= 0)
                {
                    continue;
                }

                RoadType styleType = GetTransitionMarkingStyleType(cell.roadType, neighbor.roadType);
                if (styleType == null) continue;

                Vector3 center = GetCellWorldCenter(cell.gridPosition);
                Vector3 neighborCenter = GetCellWorldCenter(neighbor.gridPosition);
                Vector3 transitionDirection = neighborCenter - center;
                transitionDirection.y = 0f;
                if (transitionDirection.sqrMagnitude <= 0.000001f) continue;
                transitionDirection.Normalize();

                Vector3 ownerSideDirection = -transitionDirection;
                if (TryGetOtherConnectionBit(cell, bit, out int otherBit))
                {
                    Vector3 otherCenter = GetCellWorldCenter(
                        RoadSystemBackend.Instance.GetNeighborPosition(
                            cell.gridPosition,
                            otherBit));
                    ownerSideDirection = otherCenter - center;
                    ownerSideDirection.y = 0f;
                    if (ownerSideDirection.sqrMagnitude > 0.000001f)
                    {
                        ownerSideDirection.Normalize();
                    }
                    else
                    {
                        ownerSideDirection = -transitionDirection;
                    }
                }

                float pullback = GetBoundaryMarkingPullback(
                    cell,
                    bit,
                    neighbor.roadType,
                    center,
                    neighborCenter);
                Vector3 transitionP0 = WithTerrainHeight(center + ownerSideDirection * pullback);
                Vector3 transitionP1 = WithTerrainHeight(center + transitionDirection * pullback);
                Vector3 p0Forward = -ownerSideDirection;
                Vector3 p1Forward = transitionDirection;

                Vector3 midPos = (transitionP0 + transitionP1) * 0.5f;
                Vector2Int cPos = new Vector2Int(
                    Mathf.FloorToInt(midPos.x / chunkWorldSize),
                    Mathf.FloorToInt(midPos.z / chunkWorldSize));
                if (!_dirtyChunks.Contains(cPos)) continue;

                MeshKey key = new MeshKey(styleType, cell.isPreview);
                if (!_chunkedSegments.ContainsKey(cPos))
                {
                    _chunkedSegments[cPos] =
                        new Dictionary<MeshKey, List<SegmentNative>>();
                }

                if (!_chunkedSegments[cPos].ContainsKey(key))
                {
                    _chunkedSegments[cPos][key] =
                        new List<SegmentNative>();
                }

                float transitionLength = Vector3.Distance(transitionP0, transitionP1);
                _chunkedSegments[cPos][key].Add(new SegmentNative
                {
                    p0_pos = transitionP0,
                    p0_fwd = p0Forward,
                    p0_w = cell.roadType.roadWidth,
                    p0_c = cell.roadType.curbWidth,
                    p1_pos = transitionP1,
                    p1_fwd = p1Forward,
                    p1_w = neighbor.roadType.roadWidth,
                    p1_c = neighbor.roadType.curbWidth,
                    p0_marking_w = cell.roadType.roadWidth,
                    p1_marking_w = neighbor.roadType.roadWidth,
                    lanesPerWay = Mathf.Max(1, styleType.lanesPerWay),
                    isTwoWay = styleType.UsesTwoWayTraffic ? 1 : 0,
                    showCenterDivider = 0,
                    showLaneDividers = 0,
                    showStopLinesAtIntersections = 0,
                    p0Distance = 0f,
                    p1Distance = transitionLength,
                    pathLength = transitionLength,
                    startIntersectionPullback = 0f,
                    endIntersectionPullback = 0f,
                    startMarkingPullback = 0f,
                    endMarkingPullback = 0f,
                    centerDividerWidth = styleType.centerDividerWidth,
                    laneDividerWidth = styleType.laneDividerWidth,
                    laneDividerDashLength = styleType.laneDividerDashLength,
                    laneDividerGapLength = styleType.laneDividerGapLength,
                    stopLineWidth = styleType.stopLineWidth,
                    markingSurfaceOffset = styleType.markingSurfaceOffset
                });

                if (!styleType.showCenterDivider && !styleType.showLaneDividers)
                {
                    continue;
                }

                TransitionMarkingNative marking = new TransitionMarkingNative
                {
                    p0_pos = transitionP0,
                    p0_fwd = p0Forward,
                    p0_w = cell.roadType.roadWidth,
                    p0_lanesPerWay = Mathf.Max(1, cell.roadType.lanesPerWay),
                    p0_isTwoWay = cell.roadType.UsesTwoWayTraffic ? 1 : 0,
                    p1_pos = transitionP1,
                    p1_fwd = p1Forward,
                    p1_w = neighbor.roadType.roadWidth,
                    p1_lanesPerWay = Mathf.Max(1, neighbor.roadType.lanesPerWay),
                    p1_isTwoWay = neighbor.roadType.UsesTwoWayTraffic ? 1 : 0,
                    showCenterDivider = styleType.showCenterDivider ? 1 : 0,
                    showLaneDividers = styleType.showLaneDividers ? 1 : 0,
                    centerDividerWidth = styleType.centerDividerWidth,
                    laneDividerWidth = styleType.laneDividerWidth,
                    laneDividerDashLength = styleType.laneDividerDashLength,
                    laneDividerGapLength = styleType.laneDividerGapLength,
                    markingSurfaceOffset = styleType.markingSurfaceOffset
                };
                if (!_chunkedTransitionMarkings.ContainsKey(cPos))
                {
                    _chunkedTransitionMarkings[cPos] =
                        new Dictionary<MeshKey, List<TransitionMarkingNative>>();
                }

                if (!_chunkedTransitionMarkings[cPos].ContainsKey(key))
                {
                    _chunkedTransitionMarkings[cPos][key] =
                        new List<TransitionMarkingNative>();
                }

                _chunkedTransitionMarkings[cPos][key].Add(marking);
            }
        }
    }

    private RoadType GetTransitionMarkingStyleType(RoadType first, RoadType second)
    {
        if (first == null) return second;
        if (second == null) return first;

        int firstLaneCount = GetVisualLaneCount(first);
        int secondLaneCount = GetVisualLaneCount(second);
        if (secondLaneCount > firstLaneCount) return second;
        if (firstLaneCount > secondLaneCount) return first;
        if (second.roadWidth > first.roadWidth) return second;
        return first;
    }

    private RoadType GetIntersectionCapStyleType(RoadCell cell)
    {
        RoadType styleType = cell.roadType;

        for (int i = 0; i < 8; i++)
        {
            int bit = 1 << i;
            if (!cell.HasConnection(bit)) continue;

            Vector2Int neighborPos = RoadSystemBackend.Instance.GetNeighborPosition(cell.gridPosition, bit);
            if (_roadData.TryGetValue(neighborPos, out RoadCell neighbor))
            {
                styleType = GetTransitionMarkingStyleType(styleType, neighbor.roadType);
            }
        }

        return styleType;
    }

    private int GetVisualLaneCount(RoadType type)
    {
        if (type == null) return 0;
        return Mathf.Max(1, type.lanesPerWay) * (type.UsesTwoWayTraffic ? 2 : 1);
    }

    private int CompareCells(Vector2Int left, Vector2Int right)
    {
        int x = left.x.CompareTo(right.x);
        return x != 0 ? x : left.y.CompareTo(right.y);
    }

    private void AddCap(
        Vector2Int chunkPos,
        MeshKey key,
        Vector3 position,
        float roadWidth,
        float curbWidth)
    {
        if (!_chunkedCaps.ContainsKey(chunkPos))
        {
            _chunkedCaps[chunkPos] =
                new Dictionary<MeshKey, List<PathNodeNative>>();
        }

        if (!_chunkedCaps[chunkPos].ContainsKey(key))
        {
            _chunkedCaps[chunkPos][key] =
                new List<PathNodeNative>();
        }

        _chunkedCaps[chunkPos][key].Add(new PathNodeNative
        {
            position = position,
            roadWidth = roadWidth,
            curbWidth = curbWidth
        });
    }

    private void GetMaxIntersectionWidth(RoadCell cell, out float maxWidth, out float maxCurb)
    {
        maxWidth = cell.roadType.roadWidth;
        maxCurb = cell.roadType.curbWidth;

        for (int i = 0; i < 8; i++)
        {
            int bit = 1 << i;
            if (cell.HasConnection(bit))
            {
                Vector2Int nPos = RoadSystemBackend.Instance.GetNeighborPosition(cell.gridPosition, bit);
                if (_roadData.TryGetValue(nPos, out RoadCell neighbor))
                {
                    if (neighbor.roadType.roadWidth > maxWidth) maxWidth = neighbor.roadType.roadWidth;
                    if (neighbor.roadType.curbWidth > maxCurb) maxCurb = neighbor.roadType.curbWidth;
                }
            }
        }

    }

    private float GetBoundaryMarkingPullback(
        RoadCell boundaryCell,
        int boundaryLegDirectionBit,
        RoadType sectionType,
        Vector3 boundaryPoint,
        Vector3 inwardPoint)
    {
        float segmentLength = Vector3.Distance(boundaryPoint, inwardPoint);
        if (segmentLength <= 0.0001f) return 0f;

        RoadNodeKind boundaryKind = boundaryCell != null
            ? RoadSystemBackend.Instance.GetRoadNodeKind(boundaryCell)
            : RoadNodeKind.ThroughRoad;
        bool usesStandardNodeGeometry =
            boundaryKind == RoadNodeKind.Intersection ||
            boundaryKind == RoadNodeKind.Transition;
        bool isWide =
            !usesStandardNodeGeometry &&
            ((sectionType != null && sectionType.IsWide) ||
             (boundaryCell != null &&
              boundaryCell.roadType != null &&
              boundaryCell.roadType.IsWide));
        float nodePullbackFraction =
            RoadGeometryRules.GetNodePullbackFraction(isWide);
        float pullbackUnits = nodePullbackFraction * segmentLength;
        if (boundaryKind == RoadNodeKind.Intersection)
        {
            pullbackUnits = Mathf.Max(
                pullbackUnits,
                GetIntersectionStopLinePullbackUnits(
                    boundaryCell,
                    boundaryLegDirectionBit,
                    sectionType));
        }

        return Mathf.Clamp(
            pullbackUnits,
            nodePullbackFraction * segmentLength,
            TrafficMaximumNodePullback * segmentLength);
    }

    private float GetIntersectionStopLinePullbackUnits(
        RoadCell intersectionCell,
        int boundaryLegDirectionBit,
        RoadType sectionType)
    {
        float maxRoadWidth = sectionType != null ? sectionType.roadWidth : 0f;
        float maxCurbWidth = sectionType != null ? sectionType.curbWidth : 0f;

        AccumulateRoadTypeWidth(intersectionCell != null ? intersectionCell.roadType : null, ref maxRoadWidth, ref maxCurbWidth);

        if (intersectionCell != null)
        {
            for (int i = 0; i < 8; i++)
            {
                int bit = 1 << i;
                if (!intersectionCell.HasConnection(bit)) continue;

                Vector2Int neighborPos = RoadSystemBackend.Instance.GetNeighborPosition(intersectionCell.gridPosition, bit);
                if (_roadData.TryGetValue(neighborPos, out RoadCell neighbor))
                {
                    AccumulateRoadTypeWidth(neighbor.roadType, ref maxRoadWidth, ref maxCurbWidth);
                }
            }
        }

        float geometryEnvelopePullback =
            RoadGeometryRules.GetIntersectionGeometryOuterDiameter(
                maxRoadWidth,
                maxCurbWidth) * 0.5f;
        float footprintPullback = GetProjectedIntersectionFootprintPullbackUnits(
            intersectionCell,
            boundaryLegDirectionBit,
            sectionType);

        return Mathf.Max(geometryEnvelopePullback, footprintPullback) +
               TrafficIntersectionStopLinePaddingUnits;
    }

    private float GetProjectedIntersectionFootprintPullbackUnits(
        RoadCell intersectionCell,
        int directionBit,
        RoadType sectionType)
    {
        if (intersectionCell == null || sectionType == null) return 0f;

        Vector2 approach = DirectionVector(directionBit);
        if (approach.sqrMagnitude <= 0.0001f) return 0f;

        Vector2 approachNormal = Perpendicular(approach);
        float laneEnvelopeHalfWidth = sectionType.roadWidth * 0.5f;
        float requiredPullback = 0f;

        for (int i = 0; i < 8; i++)
        {
            int otherDirectionBit = 1 << i;
            if (!intersectionCell.HasConnection(otherDirectionBit) ||
                otherDirectionBit == directionBit ||
                otherDirectionBit == OppositeDirectionBit(directionBit))
            {
                continue;
            }

            Vector2 otherDirection = DirectionVector(otherDirectionBit);
            if (otherDirection.sqrMagnitude <= 0.0001f) continue;

            Vector2 otherNormal = Perpendicular(otherDirection);
            float approachAcrossOtherRoad = Mathf.Abs(Vector2.Dot(approach, otherNormal));
            if (approachAcrossOtherRoad <= 0.0001f) continue;

            float otherHalfWidth = GetConnectedLegHalfWidth(intersectionCell, otherDirectionBit);
            float laneSpreadAcrossOtherRoad =
                laneEnvelopeHalfWidth *
                Mathf.Abs(Vector2.Dot(approachNormal, otherNormal));
            float pullback = (otherHalfWidth + laneSpreadAcrossOtherRoad) / approachAcrossOtherRoad;
            requiredPullback = Mathf.Max(requiredPullback, pullback);
        }

        return requiredPullback;
    }

    private float GetConnectedLegHalfWidth(RoadCell intersectionCell, int directionBit)
    {
        RoadType type = null;
        Vector2Int neighborPos = RoadSystemBackend.Instance.GetNeighborPosition(intersectionCell.gridPosition, directionBit);
        if (_roadData.TryGetValue(neighborPos, out RoadCell neighbor))
        {
            type = neighbor.roadType;
        }

        if (type == null)
        {
            type = intersectionCell.roadType;
        }

        return type != null ? (type.roadWidth + type.curbWidth) * 0.5f : 0f;
    }

    private void AccumulateRoadTypeWidth(
        RoadType type,
        ref float maxRoadWidth,
        ref float maxCurbWidth)
    {
        if (type == null) return;

        maxRoadWidth = Mathf.Max(maxRoadWidth, type.roadWidth);
        maxCurbWidth = Mathf.Max(maxCurbWidth, type.curbWidth);
    }

    private float ClampOpposingBoundaryPullbacks(
        float pathLength,
        ref float startPullbackUnits,
        ref float endPullbackUnits)
    {
        if (pathLength <= 0.0001f) return pathLength;

        float maxCombinedPullback = Mathf.Max(
            0f,
            pathLength - Mathf.Min(
                TrafficMinimumBoundarySeparationUnits,
                pathLength * 0.5f));
        float combinedPullback = startPullbackUnits + endPullbackUnits;
        if (combinedPullback <= maxCombinedPullback ||
            combinedPullback <= 0.0001f)
        {
            return pathLength;
        }

        float scale = maxCombinedPullback / combinedPullback;
        startPullbackUnits *= scale;
        endPullbackUnits *= scale;
        return pathLength;
    }

    private bool TryGetOtherConnectionBit(
        RoadCell cell,
        int excludedBit,
        out int otherBit)
    {
        for (int i = 0; i < 8; i++)
        {
            int bit = 1 << i;
            if (bit != excludedBit && cell.HasConnection(bit))
            {
                otherBit = bit;
                return true;
            }
        }

        otherBit = 0;
        return false;
    }

    private Vector3 MoveAlongSegment(
        Vector3 boundaryPoint,
        Vector3 inwardPoint,
        float distanceUnits)
    {
        float segmentLength = Vector3.Distance(boundaryPoint, inwardPoint);
        if (segmentLength <= 0.0001f) return boundaryPoint;

        return Vector3.Lerp(
            boundaryPoint,
            inwardPoint,
            Mathf.Clamp01(distanceUnits / segmentLength));
    }

    private Vector3 WithTerrainHeight(Vector3 position)
    {
        float px = position.x / _wm.cellSize;
        float pz = position.z / _wm.cellSize;
        position.y = _wm.GetPhysicalHeight(px, pz) * _wm.heightStep;
        return position;
    }

    private LogicalPath TracePath(RoadCell startCell, Vector2Int firstNeighborPos)
    {
        List<PathNodeNative> path = new List<PathNodeNative>();
        Vector3 startPos = GetCellWorldCenter(startCell.gridPosition);
        RoadNodeKind startKind = RoadSystemBackend.Instance.GetRoadNodeKind(startCell);
        bool startsAtIntersection = startKind == RoadNodeKind.Intersection;
        bool startsAtBoundary = startKind != RoadNodeKind.ThroughRoad;
        bool startsAtTransitionBoundary = OwnsRoadTypeTransitionBoundary(startCell);
        bool endsAtIntersection = false;
        bool endsAtTransitionBoundary = false;
        float startIntersectionPullback = 0f;
        float endIntersectionPullback = 0f;
        float startMarkingPullback = 0f;
        float endMarkingPullback = 0f;
        Vector3 startInwardPoint = startPos;
        bool hasStartBoundaryEndpoint = false;
        Vector3 endBoundaryPoint = Vector3.zero;
        Vector3 endInwardPoint = Vector3.zero;
        bool hasEndBoundaryEndpoint = false;

        path.Add(new PathNodeNative {
            position = startPos,
            roadWidth = startCell.roadType.roadWidth,
            curbWidth = startCell.roadType.curbWidth
        });
        
        RoadCell prev = startCell;
        
        // Failsafe exit
        if (!_roadData.TryGetValue(firstNeighborPos, out RoadCell curr)) 
        {
            return new LogicalPath {
                type = startCell.roadType,
                nodes = path,
                isPreview = startCell.isPreview,
                startsAtIntersection = startsAtIntersection,
                endsAtIntersection = false,
                useStandardNodeSmoothing =
                    startsAtIntersection || startsAtTransitionBoundary,
                startIntersectionPullback = 0f,
                endIntersectionPullback = 0f,
                startMarkingPullback = 0f,
                endMarkingPullback = 0f
            };
        }

        RoadType pathType = curr.roadType != null ? curr.roadType : startCell.roadType;
        float sectionWidth = pathType != null ? pathType.roadWidth : startCell.roadType.roadWidth;
        float sectionCurb = pathType != null ? pathType.curbWidth : startCell.roadType.curbWidth;
        float startNodeWidth = sectionWidth;
        float startNodeCurb = sectionCurb;
        if (startsAtIntersection)
        {
            GetMaxIntersectionWidth(startCell, out startNodeWidth, out startNodeCurb);
        }

        path[0] = new PathNodeNative {
            position = startPos,
            roadWidth = startNodeWidth,
            curbWidth = startNodeCurb
        };

        if (startsAtBoundary)
        {
            Vector3 firstNeighborCenter = GetCellWorldCenter(curr.gridPosition);
            startInwardPoint = firstNeighborCenter;
            hasStartBoundaryEndpoint = startsAtTransitionBoundary;
            int startLegDirectionBit = RoadSystemBackend.Instance.GetDirectionBit(
                startCell.gridPosition,
                curr.gridPosition);
            startMarkingPullback = GetBoundaryMarkingPullback(
                startCell,
                startLegDirectionBit,
                pathType,
                startPos,
                firstNeighborCenter);
            if (startsAtIntersection)
            {
                startIntersectionPullback = startMarkingPullback;
            }
        }

        // Do NOT allow a single visual trace to cross the boundary between a permanent and preview road
        if (curr.isPreview != startCell.isPreview)
        {
            path.Add(new PathNodeNative {
                position = GetCellWorldCenter(curr.gridPosition),
                roadWidth = sectionWidth,
                curbWidth = sectionCurb
            });
            ClampOpposingBoundaryPullbacks(
                CalculatePathLength(path),
                ref startMarkingPullback,
                ref endMarkingPullback);
            if (hasStartBoundaryEndpoint)
            {
                path[0] = new PathNodeNative {
                    position = MoveAlongSegment(startPos, startInwardPoint, startMarkingPullback),
                    roadWidth = sectionWidth,
                    curbWidth = sectionCurb
                };
            }
            if (startsAtIntersection) startIntersectionPullback = startMarkingPullback;
            return new LogicalPath {
                type = pathType,
                nodes = path,
                isPreview = startCell.isPreview,
                startsAtIntersection = startsAtIntersection,
                endsAtIntersection = false,
                useStandardNodeSmoothing =
                    startsAtIntersection || startsAtTransitionBoundary,
                startIntersectionPullback = startIntersectionPullback,
                endIntersectionPullback = endIntersectionPullback,
                startMarkingPullback = hasStartBoundaryEndpoint
                    ? 0f
                    : startsAtIntersection ? startMarkingPullback : 0f,
                endMarkingPullback = 0f
            };
        }

        _visitedSegments.Add(new EdgeHash(prev.gridPosition, curr.gridPosition));

        while (true)
        {
            Vector3 currPos = GetCellWorldCenter(curr.gridPosition);
            path.Add(new PathNodeNative {
                position = currPos,
                roadWidth = sectionWidth,
                curbWidth = sectionCurb
            });

            // Break if intersection, loop, OR preview status changes
            if (IsVisualBoundary(curr) || curr.gridPosition == startCell.gridPosition || curr.isPreview != startCell.isPreview)
            {
                RoadNodeKind endKind = RoadSystemBackend.Instance.GetRoadNodeKind(curr);
                endsAtIntersection = endKind == RoadNodeKind.Intersection;
                endsAtTransitionBoundary = OwnsRoadTypeTransitionBoundary(curr);
                bool endsAtBoundary = endKind != RoadNodeKind.ThroughRoad;
                float endNodeWidth = sectionWidth;
                float endNodeCurb = sectionCurb;
                if (endsAtIntersection)
                {
                    GetMaxIntersectionWidth(curr, out endNodeWidth, out endNodeCurb);
                }

                if (endsAtBoundary)
                {
                    int endLegDirectionBit = RoadSystemBackend.Instance.GetDirectionBit(
                        curr.gridPosition,
                        prev.gridPosition);
                    endMarkingPullback = GetBoundaryMarkingPullback(
                        curr,
                        endLegDirectionBit,
                        pathType,
                        currPos,
                        GetCellWorldCenter(prev.gridPosition));
                    endBoundaryPoint = currPos;
                    endInwardPoint = GetCellWorldCenter(prev.gridPosition);
                    hasEndBoundaryEndpoint = endsAtTransitionBoundary;
                    if (endsAtIntersection)
                    {
                        endIntersectionPullback = endMarkingPullback;
                    }
                }

                path[path.Count - 1] = new PathNodeNative {
                    position = currPos,
                    roadWidth = endNodeWidth,
                    curbWidth = endNodeCurb
                };
                break; 
            }

            Vector2Int nextPos = curr.gridPosition;
            for (int i = 0; i < 8; i++)
            {
                int bit = 1 << i;
                if (curr.HasConnection(bit))
                {
                    Vector2Int nPos = RoadSystemBackend.Instance.GetNeighborPosition(curr.gridPosition, bit);
                    if (nPos != prev.gridPosition) { nextPos = nPos; break; }
                }
            }

            _visitedSegments.Add(new EdgeHash(curr.gridPosition, nextPos));
            prev = curr;
            if (!_roadData.TryGetValue(nextPos, out curr)) break;
        }

        ClampOpposingBoundaryPullbacks(
            CalculatePathLength(path),
            ref startMarkingPullback,
            ref endMarkingPullback);
        if (hasStartBoundaryEndpoint)
        {
            path[0] = new PathNodeNative {
                position = MoveAlongSegment(startPos, startInwardPoint, startMarkingPullback),
                roadWidth = sectionWidth,
                curbWidth = sectionCurb
            };
        }

        if (hasEndBoundaryEndpoint)
        {
            path[path.Count - 1] = new PathNodeNative {
                position = MoveAlongSegment(endBoundaryPoint, endInwardPoint, endMarkingPullback),
                roadWidth = sectionWidth,
                curbWidth = sectionCurb
            };
        }

        if (startsAtIntersection) startIntersectionPullback = startMarkingPullback;
        if (endsAtIntersection) endIntersectionPullback = endMarkingPullback;

        return new LogicalPath {
            type = pathType,
            nodes = path,
            isPreview = startCell.isPreview,
            startsAtIntersection = startsAtIntersection,
            endsAtIntersection = endsAtIntersection,
            useStandardNodeSmoothing =
                startsAtIntersection ||
                endsAtIntersection ||
                startsAtTransitionBoundary ||
                endsAtTransitionBoundary,
            startIntersectionPullback = startIntersectionPullback,
            endIntersectionPullback = endIntersectionPullback,
            startMarkingPullback = hasStartBoundaryEndpoint
                ? 0f
                : startsAtIntersection ? startMarkingPullback : 0f,
            endMarkingPullback = hasEndBoundaryEndpoint
                ? 0f
                : endsAtIntersection ? endMarkingPullback : 0f
        };
    }

    private List<PathNodeNative> ApplyChaikin(
        List<PathNodeNative> path,
        int iterations,
        float cornerCutRatio)
    {
        if (path.Count < 3 || iterations == 0) return path;
        cornerCutRatio = Mathf.Clamp(cornerCutRatio, 0.01f, 0.49f);
        bool isClosedLoop = Vector3.Distance(path[0].position, path[path.Count - 1].position) < 0.01f;
        List<PathNodeNative> workingPath = new List<PathNodeNative>(path);
        if (isClosedLoop) workingPath.RemoveAt(workingPath.Count - 1);

        for (int iter = 0; iter < iterations; iter++)
        {
            float iterationCutRatio = iter == 0
                ? cornerCutRatio
                : RoadGeometryRules.StandardCornerCutRatio;
            List<PathNodeNative> newPath = new List<PathNodeNative>();
            if (!isClosedLoop) newPath.Add(workingPath[0]);

            for (int i = 0; i < workingPath.Count - (isClosedLoop ? 0 : 1); i++)
            {
                PathNodeNative p0 = workingPath[i];
                PathNodeNative p1 = workingPath[(i + 1) % workingPath.Count];

                newPath.Add(new PathNodeNative {
                    position = Vector3.Lerp(p0.position, p1.position, iterationCutRatio),
                    roadWidth = Mathf.Lerp(p0.roadWidth, p1.roadWidth, iterationCutRatio),
                    curbWidth = Mathf.Lerp(p0.curbWidth, p1.curbWidth, iterationCutRatio)
                });
                newPath.Add(new PathNodeNative {
                    position = Vector3.Lerp(p0.position, p1.position, 1f - iterationCutRatio),
                    roadWidth = Mathf.Lerp(p0.roadWidth, p1.roadWidth, 1f - iterationCutRatio),
                    curbWidth = Mathf.Lerp(p0.curbWidth, p1.curbWidth, 1f - iterationCutRatio)
                });
            }

            if (!isClosedLoop) newPath.Add(workingPath[workingPath.Count - 1]);
            workingPath = newPath;
        }

        if (isClosedLoop) workingPath.Add(workingPath[0]);
        return workingPath;
    }

    private float3 GetForward(List<PathNodeNative> path, int i, bool isClosedLoop)
    {
        if (path.Count < 2) return new float3(0, 0, 1);
        
        if (isClosedLoop && path.Count >= 4)
        {
            // For a closed loop, the node before i=0 is Count-2 (since 0 and Count-1 are the same).
            // The node after i=Count-1 is i=1.
            int prev = (i == 0) ? path.Count - 2 : i - 1;
            int next = (i == path.Count - 1) ? 1 : i + 1;
            return math.normalize(path[next].position - path[prev].position);
        }
        else
        {
            if (i == 0) return math.normalize(path[1].position - path[0].position);
            if (i == path.Count - 1) return math.normalize(path[i].position - path[i - 1].position);
            return math.normalize(path[i + 1].position - path[i - 1].position);
        }
    }

    private float[] BuildCumulativeDistances(List<PathNodeNative> path)
    {
        if (path == null || path.Count == 0) return new float[0];

        float[] distances = new float[path.Count];
        for (int i = 1; i < path.Count; i++)
        {
            distances[i] = distances[i - 1] +
                           Vector3.Distance(path[i - 1].position, path[i].position);
        }

        return distances;
    }

    private float CalculatePathLength(List<PathNodeNative> path)
    {
        if (path == null || path.Count < 2) return 0f;

        float length = 0f;
        for (int i = 1; i < path.Count; i++)
        {
            length += Vector3.Distance(path[i - 1].position, path[i].position);
        }

        return length;
    }

    private Vector2 DirectionVector(int directionBit)
    {
        Vector2Int offset =
            RoadSystemBackend.Instance.GetNeighborPosition(
                Vector2Int.zero,
                directionBit);
        return ((Vector2)offset).normalized;
    }

    private Vector2 Perpendicular(Vector2 direction)
    {
        return new Vector2(-direction.y, direction.x);
    }

    private int OppositeDirectionBit(int bit)
    {
        if (bit <= 0 || bit > 128 || (bit & (bit - 1)) != 0) return 0;
        return bit <= 8 ? bit << 4 : bit >> 4;
    }

    private Vector3 GetCellWorldCenter(Vector2Int pos)
    {
        float x = pos.x * _wm.cellSize + (_wm.cellSize * 0.5f);
        float z = pos.y * _wm.cellSize + (_wm.cellSize * 0.5f);
        float y = _wm.GetPhysicalHeight(pos.x + 0.5f, pos.y + 0.5f) * _wm.heightStep;
        return new Vector3(x, y, z);
    }

    // --- BURST JOB ---

    [BurstCompile]
    public struct RoadChunkMeshJob : IJob
    {
        [ReadOnly] public NativeArray<SegmentNative> segments;
        [ReadOnly] public NativeArray<PathNodeNative> caps;
        [ReadOnly] public NativeArray<TransitionMarkingNative> transitionMarkings;

        public float yOffset;
        public float2 shadowPlanarOffset;
        public float shadowYOffset;

        public NativeList<float3> rVerts; public NativeList<float3> rNorms; public NativeList<float2> rUVs; public NativeList<int> rTris;
        public NativeList<float3> cVerts; public NativeList<float3> cNorms; public NativeList<float2> cUVs; public NativeList<int> cTris;
        public NativeList<float3> sVerts; public NativeList<float3> sNorms; public NativeList<float2> sUVs; public NativeList<int> sTris;
        public NativeList<float3> mVerts; public NativeList<float3> mNorms; public NativeList<float2> mUVs; public NativeList<int> mTris;

        public void Execute()
        {
            for (int i = 0; i < segments.Length; i++)
            {
                SegmentNative seg = segments[i];

                ExtrudeSegment(seg.p0_pos, seg.p1_pos, seg.p0_fwd, seg.p1_fwd, seg.p0_w, seg.p1_w, yOffset, float2.zero, ref rVerts, ref rUVs, ref rNorms, ref rTris);
                ExtrudeSegment(seg.p0_pos, seg.p1_pos, seg.p0_fwd, seg.p1_fwd, seg.p0_w + seg.p0_c, seg.p1_w + seg.p1_c, yOffset - 0.02f, float2.zero, ref cVerts, ref cUVs, ref cNorms, ref cTris);
                ExtrudeSegment(seg.p0_pos, seg.p1_pos, seg.p0_fwd, seg.p1_fwd, seg.p0_w + seg.p0_c, seg.p1_w + seg.p1_c, yOffset + shadowYOffset, shadowPlanarOffset, ref sVerts, ref sUVs, ref sNorms, ref sTris);
                GenerateSegmentMarkings(seg);
            }

            for (int i = 0; i < caps.Length; i++)
            {
                PathNodeNative cap = caps[i];
                GenerateCap(cap.position, cap.roadWidth, yOffset, float2.zero, ref rVerts, ref rUVs, ref rNorms, ref rTris);
                GenerateCap(cap.position, cap.roadWidth + cap.curbWidth, yOffset - 0.02f, float2.zero, ref cVerts, ref cUVs, ref cNorms, ref cTris);
            }

            for (int i = 0; i < transitionMarkings.Length; i++)
            {
                GenerateTransitionMarkings(transitionMarkings[i]);
            }
        }

        private void GenerateTransitionMarkings(TransitionMarkingNative transition)
        {
            float markHeight = yOffset + math.max(0.001f, transition.markingSurfaceOffset);
            if (transition.showCenterDivider != 0 &&
                transition.p0_isTwoWay != 0 &&
                transition.p1_isTwoWay != 0)
            {
                AddTransitionLongitudinalMarking(
                    transition,
                    0f,
                    0f,
                    transition.centerDividerWidth,
                    markHeight,
                    0f,
                    0f,
                    ref mVerts,
                    ref mUVs,
                    ref mNorms,
                    ref mTris);
            }

            if (transition.showLaneDividers == 0) return;

            if (transition.p0_isTwoWay != 0 && transition.p1_isTwoWay != 0)
            {
                AddTwoWayTransitionLaneDividers(
                    transition,
                    -1,
                    markHeight,
                    ref mVerts,
                    ref mUVs,
                    ref mNorms,
                    ref mTris);
                AddTwoWayTransitionLaneDividers(
                    transition,
                    1,
                    markHeight,
                    ref mVerts,
                    ref mUVs,
                    ref mNorms,
                    ref mTris);
            }
            else
            {
                AddOneWayTransitionLaneDividers(
                    transition,
                    markHeight,
                    ref mVerts,
                    ref mUVs,
                    ref mNorms,
                    ref mTris);
            }
        }

        private void AddTwoWayTransitionLaneDividers(
            TransitionMarkingNative transition,
            int side,
            float markHeight,
            ref NativeList<float3> verts,
            ref NativeList<float2> uvs,
            ref NativeList<float3> norms,
            ref NativeList<int> tris)
        {
            int p0LaneCount = math.max(1, transition.p0_lanesPerWay);
            int p1LaneCount = math.max(1, transition.p1_lanesPerWay);
            if (p0LaneCount >= p1LaneCount)
            {
                for (int dividerIndex = 1; dividerIndex < p0LaneCount; dividerIndex++)
                {
                    int leftTarget = MapLaneIndex(
                        dividerIndex - 1,
                        p0LaneCount,
                        p1LaneCount);
                    int rightTarget = MapLaneIndex(
                        dividerIndex,
                        p0LaneCount,
                        p1LaneCount);
                    float p0Offset = GetTwoWaySameSideLaneDividerOffset(
                        transition.p0_w,
                        p0LaneCount,
                        side,
                        dividerIndex);
                    float p1Offset = leftTarget == rightTarget
                        ? GetTwoWayLaneCenterOffset(
                            transition.p1_w,
                            p1LaneCount,
                            side,
                            leftTarget)
                        : GetTwoWaySameSideLaneDividerOffset(
                            transition.p1_w,
                            p1LaneCount,
                            side,
                            rightTarget);

                    AddTransitionLongitudinalMarking(
                        transition,
                        p0Offset,
                        p1Offset,
                        transition.laneDividerWidth,
                        markHeight,
                        transition.laneDividerDashLength,
                        transition.laneDividerGapLength,
                        ref verts,
                        ref uvs,
                        ref norms,
                        ref tris);
                }

                return;
            }

            for (int dividerIndex = 1; dividerIndex < p1LaneCount; dividerIndex++)
            {
                int leftSource = MapLaneIndex(
                    dividerIndex - 1,
                    p1LaneCount,
                    p0LaneCount);
                int rightSource = MapLaneIndex(
                    dividerIndex,
                    p1LaneCount,
                    p0LaneCount);
                float p0Offset = leftSource == rightSource
                    ? GetTwoWayLaneCenterOffset(
                        transition.p0_w,
                        p0LaneCount,
                        side,
                        leftSource)
                    : GetTwoWaySameSideLaneDividerOffset(
                        transition.p0_w,
                        p0LaneCount,
                        side,
                        rightSource);
                float p1Offset = GetTwoWaySameSideLaneDividerOffset(
                    transition.p1_w,
                    p1LaneCount,
                    side,
                    dividerIndex);

                AddTransitionLongitudinalMarking(
                    transition,
                    p0Offset,
                    p1Offset,
                    transition.laneDividerWidth,
                    markHeight,
                    transition.laneDividerDashLength,
                    transition.laneDividerGapLength,
                    ref verts,
                    ref uvs,
                    ref norms,
                    ref tris);
            }
        }

        private void AddOneWayTransitionLaneDividers(
            TransitionMarkingNative transition,
            float markHeight,
            ref NativeList<float3> verts,
            ref NativeList<float2> uvs,
            ref NativeList<float3> norms,
            ref NativeList<int> tris)
        {
            int p0LaneCount = math.max(1, transition.p0_lanesPerWay);
            int p1LaneCount = math.max(1, transition.p1_lanesPerWay);
            if (p0LaneCount >= p1LaneCount)
            {
                for (int dividerIndex = 1; dividerIndex < p0LaneCount; dividerIndex++)
                {
                    int leftTarget = MapLaneIndex(
                        dividerIndex - 1,
                        p0LaneCount,
                        p1LaneCount);
                    int rightTarget = MapLaneIndex(
                        dividerIndex,
                        p0LaneCount,
                        p1LaneCount);
                    float p0Offset = GetOneWayLaneDividerOffset(
                        transition.p0_w,
                        p0LaneCount,
                        dividerIndex);
                    float p1Offset = leftTarget == rightTarget
                        ? GetOneWayLaneCenterOffset(
                            transition.p1_w,
                            p1LaneCount,
                            leftTarget)
                        : GetOneWayLaneDividerOffset(
                            transition.p1_w,
                            p1LaneCount,
                            rightTarget);

                    AddTransitionLongitudinalMarking(
                        transition,
                        p0Offset,
                        p1Offset,
                        transition.laneDividerWidth,
                        markHeight,
                        transition.laneDividerDashLength,
                        transition.laneDividerGapLength,
                        ref verts,
                        ref uvs,
                        ref norms,
                        ref tris);
                }

                return;
            }

            for (int dividerIndex = 1; dividerIndex < p1LaneCount; dividerIndex++)
            {
                int leftSource = MapLaneIndex(
                    dividerIndex - 1,
                    p1LaneCount,
                    p0LaneCount);
                int rightSource = MapLaneIndex(
                    dividerIndex,
                    p1LaneCount,
                    p0LaneCount);
                float p0Offset = leftSource == rightSource
                    ? GetOneWayLaneCenterOffset(
                        transition.p0_w,
                        p0LaneCount,
                        leftSource)
                    : GetOneWayLaneDividerOffset(
                        transition.p0_w,
                        p0LaneCount,
                        rightSource);
                float p1Offset = GetOneWayLaneDividerOffset(
                    transition.p1_w,
                    p1LaneCount,
                    dividerIndex);

                AddTransitionLongitudinalMarking(
                    transition,
                    p0Offset,
                    p1Offset,
                    transition.laneDividerWidth,
                    markHeight,
                    transition.laneDividerDashLength,
                    transition.laneDividerGapLength,
                    ref verts,
                    ref uvs,
                    ref norms,
                    ref tris);
            }
        }

        private void AddTransitionLongitudinalMarking(
            TransitionMarkingNative transition,
            float p0Offset,
            float p1Offset,
            float requestedWidth,
            float hOffset,
            float dashLength,
            float gapLength,
            ref NativeList<float3> verts,
            ref NativeList<float2> uvs,
            ref NativeList<float3> norms,
            ref NativeList<int> tris)
        {
            float length = math.distance(transition.p0_pos, transition.p1_pos);
            if (length <= 0.001f) return;

            if (dashLength <= 0f || gapLength <= 0f)
            {
                AddTransitionLongitudinalMarkingRange(
                    transition,
                    p0Offset,
                    p1Offset,
                    requestedWidth,
                    hOffset,
                    0f,
                    1f,
                    ref verts,
                    ref uvs,
                    ref norms,
                    ref tris);
                return;
            }

            float cycleLength = math.max(0.001f, dashLength + gapLength);
            for (float dashStart = 0f; dashStart < length; dashStart += cycleLength)
            {
                float dashEnd = math.min(length, dashStart + dashLength);
                if (dashEnd - dashStart <= 0.001f) continue;

                AddTransitionLongitudinalMarkingRange(
                    transition,
                    p0Offset,
                    p1Offset,
                    requestedWidth,
                    hOffset,
                    dashStart / length,
                    dashEnd / length,
                    ref verts,
                    ref uvs,
                    ref norms,
                    ref tris);
            }
        }

        private void AddTransitionLongitudinalMarkingRange(
            TransitionMarkingNative transition,
            float p0Offset,
            float p1Offset,
            float requestedWidth,
            float hOffset,
            float t0,
            float t1,
            ref NativeList<float3> verts,
            ref NativeList<float2> uvs,
            ref NativeList<float3> norms,
            ref NativeList<int> tris)
        {
            float3 r0 = GetRight(transition.p0_fwd);
            float3 r1 = GetRight(transition.p1_fwd);
            float width = math.max(0.001f, requestedWidth);
            float3 base0 = math.lerp(transition.p0_pos, transition.p1_pos, t0);
            float3 base1 = math.lerp(transition.p0_pos, transition.p1_pos, t1);
            float3 right0 = SafeNormalize(math.lerp(r0, r1, t0), r0);
            float3 right1 = SafeNormalize(math.lerp(r0, r1, t1), r1);
            float offset0 = math.lerp(p0Offset, p1Offset, t0);
            float offset1 = math.lerp(p0Offset, p1Offset, t1);

            float3 p0 = base0 + new float3(0, hOffset, 0) + (right0 * offset0);
            float3 p1 = base1 + new float3(0, hOffset, 0) + (right1 * offset1);
            AddQuad(
                p0 - (right0 * (width * 0.5f)),
                p0 + (right0 * (width * 0.5f)),
                p1 - (right1 * (width * 0.5f)),
                p1 + (right1 * (width * 0.5f)),
                ref verts,
                ref uvs,
                ref norms,
                ref tris);
        }

        private float GetTwoWaySameSideLaneDividerOffset(
            float roadWidth,
            int lanesPerWay,
            int side,
            int dividerIndex)
        {
            int lanes = math.max(1, lanesPerWay);
            float laneWidth = roadWidth / math.max(1, lanes * 2);
            if (side < 0)
            {
                return (-roadWidth * 0.5f) + (laneWidth * dividerIndex);
            }

            return (roadWidth * 0.5f) - (laneWidth * dividerIndex);
        }

        private float GetOneWayLaneDividerOffset(
            float roadWidth,
            int laneCount,
            int dividerIndex)
        {
            int lanes = math.max(1, laneCount);
            float laneWidth = roadWidth / lanes;
            return (-roadWidth * 0.5f) + (laneWidth * dividerIndex);
        }

        private float GetTwoWayLaneCenterOffset(
            float roadWidth,
            int lanesPerWay,
            int side,
            int laneOrdinal)
        {
            int lanes = math.max(1, lanesPerWay);
            int ordinal = math.clamp(laneOrdinal, 0, lanes - 1);
            float laneWidth = roadWidth / math.max(1, lanes * 2);
            return (side < 0 ? -1f : 1f) * laneWidth * (ordinal + 0.5f);
        }

        private float GetOneWayLaneCenterOffset(
            float roadWidth,
            int laneCount,
            int laneOrdinal)
        {
            int lanes = math.max(1, laneCount);
            int ordinal = math.clamp(laneOrdinal, 0, lanes - 1);
            float laneWidth = roadWidth / lanes;
            return (-roadWidth * 0.5f) + (laneWidth * (ordinal + 0.5f));
        }

        private int MapLaneIndex(
            int laneIndex,
            int fromLaneCount,
            int toLaneCount)
        {
            int fromCount = math.max(1, fromLaneCount);
            int toCount = math.max(1, toLaneCount);
            int mapped = (int)math.floor((float)laneIndex * toCount / fromCount);
            return math.clamp(mapped, 0, toCount - 1);
        }

        private void GenerateSegmentMarkings(SegmentNative seg)
        {
            int lanesPerWay = math.max(1, seg.lanesPerWay);
            int totalLanes = lanesPerWay * (seg.isTwoWay != 0 ? 2 : 1);
            if (totalLanes <= 1 &&
                seg.showCenterDivider == 0 &&
                seg.showStopLinesAtIntersections == 0)
            {
                return;
            }

            float markHeight = yOffset + math.max(0.001f, seg.markingSurfaceOffset);
            float visibleStartDistance = math.max(0f, seg.startMarkingPullback);
            float visibleEndDistance = math.max(
                visibleStartDistance,
                seg.pathLength - math.max(0f, seg.endMarkingPullback));

            if (seg.showCenterDivider != 0 && seg.isTwoWay != 0)
            {
                AddLongitudinalMarking(
                    seg,
                    0f,
                    0f,
                    seg.centerDividerWidth,
                    markHeight,
                    visibleStartDistance,
                    visibleEndDistance,
                    ref mVerts,
                    ref mUVs,
                    ref mNorms,
                    ref mTris);
            }

            if (seg.showLaneDividers != 0 && totalLanes > 1)
            {
                for (int laneBoundary = 1; laneBoundary < totalLanes; laneBoundary++)
                {
                    if (seg.showCenterDivider != 0 &&
                        seg.isTwoWay != 0 &&
                        laneBoundary == lanesPerWay)
                    {
                        continue;
                    }

                    float p0LaneWidth = seg.p0_marking_w / totalLanes;
                    float p1LaneWidth = seg.p1_marking_w / totalLanes;
                    float p0Offset = (-seg.p0_marking_w * 0.5f) + (p0LaneWidth * laneBoundary);
                    float p1Offset = (-seg.p1_marking_w * 0.5f) + (p1LaneWidth * laneBoundary);

                    AddLongitudinalMarking(
                        seg,
                        p0Offset,
                        p1Offset,
                        seg.laneDividerWidth,
                        markHeight,
                        visibleStartDistance,
                        visibleEndDistance,
                        seg.laneDividerDashLength,
                        seg.laneDividerGapLength,
                        ref mVerts,
                        ref mUVs,
                        ref mNorms,
                        ref mTris);
                }
            }

            if (seg.showStopLinesAtIntersections != 0)
            {
                if (seg.startIntersectionPullback > 0f &&
                    SegmentContainsDistance(seg, visibleStartDistance))
                {
                    GetStopLineLateralRange(seg, true, out float lateralMin, out float lateralMax);
                    AddStopLine(
                        SampleSegmentPosition(seg, visibleStartDistance),
                        GetSegmentForward(seg),
                        lateralMin,
                        lateralMax,
                        seg.stopLineWidth,
                        markHeight,
                        ref mVerts,
                        ref mUVs,
                        ref mNorms,
                        ref mTris);
                }

                if (seg.endIntersectionPullback > 0f &&
                    SegmentContainsDistance(seg, visibleEndDistance))
                {
                    GetStopLineLateralRange(seg, false, out float lateralMin, out float lateralMax);
                    AddStopLine(
                        SampleSegmentPosition(seg, visibleEndDistance),
                        GetSegmentForward(seg),
                        lateralMin,
                        lateralMax,
                        seg.stopLineWidth,
                        markHeight,
                        ref mVerts,
                        ref mUVs,
                        ref mNorms,
                        ref mTris);
                }
            }
        }

        private void AddLongitudinalMarking(
            SegmentNative seg,
            float p0Offset,
            float p1Offset,
            float requestedWidth,
            float hOffset,
            float visibleStartDistance,
            float visibleEndDistance,
            ref NativeList<float3> verts,
            ref NativeList<float2> uvs,
            ref NativeList<float3> norms,
            ref NativeList<int> tris)
        {
            AddLongitudinalMarking(
                seg,
                p0Offset,
                p1Offset,
                requestedWidth,
                hOffset,
                visibleStartDistance,
                visibleEndDistance,
                0f,
                0f,
                ref verts,
                ref uvs,
                ref norms,
                ref tris);
        }

        private void AddLongitudinalMarking(
            SegmentNative seg,
            float p0Offset,
            float p1Offset,
            float requestedWidth,
            float hOffset,
            float visibleStartDistance,
            float visibleEndDistance,
            float dashLength,
            float gapLength,
            ref NativeList<float3> verts,
            ref NativeList<float2> uvs,
            ref NativeList<float3> norms,
            ref NativeList<int> tris)
        {
            float segmentStart = math.max(seg.p0Distance, visibleStartDistance);
            float segmentEnd = math.min(seg.p1Distance, visibleEndDistance);
            if (segmentEnd - segmentStart <= 0.001f) return;

            if (dashLength <= 0f || gapLength <= 0f)
            {
                AddLongitudinalMarkingRange(
                    seg,
                    p0Offset,
                    p1Offset,
                    requestedWidth,
                    hOffset,
                    segmentStart,
                    segmentEnd,
                    ref verts,
                    ref uvs,
                    ref norms,
                    ref tris);
                return;
            }

            float cycleLength = math.max(0.001f, dashLength + gapLength);
            float firstDashStart =
                math.floor(segmentStart / cycleLength) * cycleLength;
            for (float dashStart = firstDashStart;
                 dashStart < segmentEnd;
                 dashStart += cycleLength)
            {
                float dashEnd = dashStart + dashLength;
                float clippedStart = math.max(segmentStart, dashStart);
                float clippedEnd = math.min(segmentEnd, dashEnd);
                if (clippedEnd - clippedStart <= 0.001f) continue;

                AddLongitudinalMarkingRange(
                    seg,
                    p0Offset,
                    p1Offset,
                    requestedWidth,
                    hOffset,
                    clippedStart,
                    clippedEnd,
                    ref verts,
                    ref uvs,
                    ref norms,
                    ref tris);
            }
        }

        private void AddLongitudinalMarkingRange(
            SegmentNative seg,
            float p0Offset,
            float p1Offset,
            float requestedWidth,
            float hOffset,
            float rangeStart,
            float rangeEnd,
            ref NativeList<float3> verts,
            ref NativeList<float2> uvs,
            ref NativeList<float3> norms,
            ref NativeList<int> tris)
        {
            float3 r0 = GetRight(seg.p0_fwd);
            float3 r1 = GetRight(seg.p1_fwd);
            float width = math.max(0.001f, requestedWidth);
            float segmentDistance = math.max(0.001f, seg.p1Distance - seg.p0Distance);
            float t0 = math.saturate((rangeStart - seg.p0Distance) / segmentDistance);
            float t1 = math.saturate((rangeEnd - seg.p0Distance) / segmentDistance);
            float3 base0 = math.lerp(seg.p0_pos, seg.p1_pos, t0);
            float3 base1 = math.lerp(seg.p0_pos, seg.p1_pos, t1);
            float3 right0 = SafeNormalize(math.lerp(r0, r1, t0), r0);
            float3 right1 = SafeNormalize(math.lerp(r0, r1, t1), r1);
            float offset0 = math.lerp(p0Offset, p1Offset, t0);
            float offset1 = math.lerp(p0Offset, p1Offset, t1);

            float3 p0 = base0 + new float3(0, hOffset, 0) + (right0 * offset0);
            float3 p1 = base1 + new float3(0, hOffset, 0) + (right1 * offset1);
            AddQuad(
                p0 - (right0 * (width * 0.5f)),
                p0 + (right0 * (width * 0.5f)),
                p1 - (right1 * (width * 0.5f)),
                p1 + (right1 * (width * 0.5f)),
                ref verts,
                ref uvs,
                ref norms,
                ref tris);
        }

        private bool SegmentContainsDistance(SegmentNative seg, float distance)
        {
            return distance >= seg.p0Distance - 0.0001f &&
                   distance <= seg.p1Distance + 0.0001f &&
                   seg.p1Distance - seg.p0Distance > 0.0001f;
        }

        private float3 SampleSegmentPosition(SegmentNative seg, float distance)
        {
            float segmentDistance = math.max(0.001f, seg.p1Distance - seg.p0Distance);
            float t = math.saturate((distance - seg.p0Distance) / segmentDistance);
            return math.lerp(seg.p0_pos, seg.p1_pos, t);
        }

        private void GetStopLineLateralRange(
            SegmentNative seg,
            bool atStartIntersection,
            out float lateralMin,
            out float lateralMax)
        {
            float roadWidth = atStartIntersection ? seg.p0_marking_w : seg.p1_marking_w;
            float halfRoadWidth = math.max(0.001f, roadWidth * 0.5f);
            if (seg.isTwoWay == 0)
            {
                lateralMin = -halfRoadWidth;
                lateralMax = halfRoadWidth;
                return;
            }

            if (atStartIntersection)
            {
                lateralMin = -halfRoadWidth;
                lateralMax = 0f;
            }
            else
            {
                lateralMin = 0f;
                lateralMax = halfRoadWidth;
            }
        }

        private float3 GetSegmentForward(SegmentNative seg)
        {
            return SafeNormalize(seg.p1_pos - seg.p0_pos, seg.p0_fwd);
        }

        private void AddStopLine(
            float3 centerPoint,
            float3 roadForward,
            float lateralMin,
            float lateralMax,
            float requestedWidth,
            float hOffset,
            ref NativeList<float3> verts,
            ref NativeList<float2> uvs,
            ref NativeList<float3> norms,
            ref NativeList<int> tris)
        {
            float3 fwd = SafeNormalize(roadForward, new float3(0, 0, 1));
            float3 right = GetRight(fwd);
            float halfStopWidth = math.max(0.001f, requestedWidth * 0.5f);
            float3 center = centerPoint + new float3(0, hOffset, 0);

            AddQuad(
                center + (right * lateralMin) - (fwd * halfStopWidth),
                center + (right * lateralMax) - (fwd * halfStopWidth),
                center + (right * lateralMin) + (fwd * halfStopWidth),
                center + (right * lateralMax) + (fwd * halfStopWidth),
                ref verts,
                ref uvs,
                ref norms,
                ref tris);
        }

        private void AddQuad(
            float3 left0,
            float3 right0,
            float3 left1,
            float3 right1,
            ref NativeList<float3> verts,
            ref NativeList<float2> uvs,
            ref NativeList<float3> norms,
            ref NativeList<int> tris)
        {
            int s = verts.Length;
            verts.Add(left0); verts.Add(right0); verts.Add(left1); verts.Add(right1);
            uvs.Add(new float2(0, 0)); uvs.Add(new float2(1, 0)); uvs.Add(new float2(0, 1)); uvs.Add(new float2(1, 1));
            norms.Add(new float3(0, 1, 0)); norms.Add(new float3(0, 1, 0)); norms.Add(new float3(0, 1, 0)); norms.Add(new float3(0, 1, 0));

            tris.Add(s); tris.Add(s + 2); tris.Add(s + 1);
            tris.Add(s + 1); tris.Add(s + 2); tris.Add(s + 3);
        }

        private float3 GetRight(float3 forward)
        {
            return SafeNormalize(math.cross(new float3(0, 1, 0), forward), new float3(1, 0, 0));
        }

        private float3 SafeNormalize(float3 value, float3 fallback)
        {
            return math.lengthsq(value) > 0.000001f ? math.normalize(value) : fallback;
        }

        private void ExtrudeSegment(float3 pos0, float3 pos1, float3 f0, float3 f1, float w0, float w1, float hOffset, float2 pOffset, ref NativeList<float3> verts, ref NativeList<float2> uvs, ref NativeList<float3> norms, ref NativeList<int> tris)
        {
            float3 r0 = math.normalize(math.cross(new float3(0, 1, 0), f0));
            float3 r1 = math.normalize(math.cross(new float3(0, 1, 0), f1));

            float3 p0 = pos0 + new float3(pOffset.x, hOffset, pOffset.y);
            float3 p1 = pos1 + new float3(pOffset.x, hOffset, pOffset.y);

            float3 l0 = p0 - (r0 * (w0 / 2f));
            float3 right0 = p0 + (r0 * (w0 / 2f));
            float3 l1 = p1 - (r1 * (w1 / 2f));
            float3 right1 = p1 + (r1 * (w1 / 2f));

            int s = verts.Length;
            verts.Add(l0); verts.Add(right0); verts.Add(l1); verts.Add(right1);
            uvs.Add(new float2(0, 0)); uvs.Add(new float2(1, 0)); uvs.Add(new float2(0, 1)); uvs.Add(new float2(1, 1));
            norms.Add(new float3(0, 1, 0)); norms.Add(new float3(0, 1, 0)); norms.Add(new float3(0, 1, 0)); norms.Add(new float3(0, 1, 0));

            tris.Add(s); tris.Add(s + 2); tris.Add(s + 1);
            tris.Add(s + 1); tris.Add(s + 2); tris.Add(s + 3);
        }

        private void GenerateCap(float3 center, float width, float hOffset, float2 pOffset, ref NativeList<float3> verts, ref NativeList<float2> uvs, ref NativeList<float3> norms, ref NativeList<int> tris)
        {
            int segments = 24; float radius = width / 2f; int centerIndex = verts.Length;
            float3 actualCenter = center + new float3(pOffset.x, hOffset, pOffset.y);
            
            verts.Add(actualCenter); uvs.Add(new float2(0.5f, 0.5f)); norms.Add(new float3(0, 1, 0));

            for (int i = 0; i <= segments; i++)
            {
                float angle = ((float)i / segments) * math.PI * 2f;
                float3 edge = actualCenter + new float3(math.cos(angle) * radius, 0, math.sin(angle) * radius);
                
                verts.Add(edge); uvs.Add(new float2((math.cos(angle) + 1f) / 2f, (math.sin(angle) + 1f) / 2f)); norms.Add(new float3(0, 1, 0));
                
                if (i > 0) { tris.Add(centerIndex); tris.Add(centerIndex + i + 1); tris.Add(centerIndex + i); }
            }
        }
    }
}
