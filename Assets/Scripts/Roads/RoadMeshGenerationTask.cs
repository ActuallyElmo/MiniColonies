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

public class RoadMeshGenerationTask : ISimulationTask
{
    private enum TaskState { Init, TraceAndSmooth, GroupSegments, ScheduleJobs, WaitForJobs, Complete }
    private TaskState _state = TaskState.Init;

    private HashSet<Vector2Int> _dirtyChunks;
    private Dictionary<Vector2Int, RoadCell> _roadData;
    private WorldManager _wm;
    private RoadVisualSystem _vis;

    private List<RoadCell> _roadsToProcess;
    private HashSet<EdgeHash> _visitedSegments = new HashSet<EdgeHash>();
    
    // Main Thread Path Data (NEW: Added isPreview flag)
    private struct LogicalPath { public List<PathNodeNative> nodes; public RoadType type; public bool isPreview; }
    private List<LogicalPath> _allSmoothedPaths = new List<LogicalPath>();
    
    // Grouped Data for Jobs (NEW: Grouped by MeshKey instead of RoadType)
    private Dictionary<Vector2Int, Dictionary<MeshKey, List<SegmentNative>>> _chunkedSegments = new Dictionary<Vector2Int, Dictionary<MeshKey, List<SegmentNative>>>();
    private Dictionary<Vector2Int, Dictionary<MeshKey, List<PathNodeNative>>> _chunkedCaps = new Dictionary<Vector2Int, Dictionary<MeshKey, List<PathNodeNative>>>();

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
    }

    public RoadMeshGenerationTask(
        HashSet<Vector2Int> chunks,
        IReadOnlyDictionary<Vector2Int, RoadCell> roads)
    {
        _dirtyChunks = chunks;
        _roadData = new Dictionary<Vector2Int, RoadCell>(roads.Count);
        foreach (KeyValuePair<Vector2Int, RoadCell> pair in roads)
        {
            _roadData.Add(pair.Key, pair.Value);
        }
        _wm = WorldManager.Instance;
        _vis = RoadVisualSystem.Instance;
        _roadsToProcess = new List<RoadCell>(_roadData.Values);
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
                if (IsRealNode(cell))
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
                if (!IsRealNode(cell))
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
                var smoothedNodes = ApplyChaikin(path.nodes, _vis.smoothingIterations);

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

                for (int i = 0; i < path.nodes.Count - 1; i++)
                {
                    float3 f0 = GetForward(path.nodes, i, isClosedLoop);
                    float3 f1 = GetForward(path.nodes, i + 1, isClosedLoop);

                    PathNodeNative n0 = path.nodes[i];
                    PathNodeNative n1 = path.nodes[i + 1];

                    SegmentNative seg = new SegmentNative {
                        p0_pos = n0.position, p0_fwd = f0, p0_w = n0.roadWidth, p0_c = n0.curbWidth,
                        p1_pos = n1.position, p1_fwd = f1, p1_w = n1.roadWidth, p1_c = n1.curbWidth
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

            foreach (var cell in _roadsToProcess)
            {
                if (IsRealNode(cell))
                {
                    Vector2Int cPos = new Vector2Int(Mathf.FloorToInt((float)cell.gridPosition.x / _wm.chunkSize), Mathf.FloorToInt((float)cell.gridPosition.y / _wm.chunkSize));
                    if (!_dirtyChunks.Contains(cPos)) continue;

                    float maxWidth = cell.roadType.roadWidth;
                    float maxCurb = cell.roadType.curbWidth;

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

                    MeshKey capKey = new MeshKey(cell.roadType, cell.isPreview); // Create isolated key

                    if (!_chunkedCaps.ContainsKey(cPos)) _chunkedCaps[cPos] = new Dictionary<MeshKey, List<PathNodeNative>>();
                    if (!_chunkedCaps[cPos].ContainsKey(capKey)) _chunkedCaps[cPos][capKey] = new List<PathNodeNative>();

                    _chunkedCaps[cPos][capKey].Add(new PathNodeNative {
                        position = GetCellWorldCenter(cell.gridPosition), roadWidth = maxWidth, curbWidth = maxCurb
                    });
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

                foreach (MeshKey key in keysInChunk)
                {
                    // Pass the isolated parameters!
                    MeshGroup group = _vis.GetOrCreateMeshGroup(key.type, chunkPos, key.isPreview);
                    _targetMeshGroups.Add(group);

                    List<SegmentNative> segments = _chunkedSegments.ContainsKey(chunkPos) && _chunkedSegments[chunkPos].ContainsKey(key) 
                        ? _chunkedSegments[chunkPos][key] : new List<SegmentNative>();
                        
                    List<PathNodeNative> caps = _chunkedCaps.ContainsKey(chunkPos) && _chunkedCaps[chunkPos].ContainsKey(key) 
                        ? _chunkedCaps[chunkPos][key] : new List<PathNodeNative>();

                    var job = new RoadChunkMeshJob
                    {
                        segments = new NativeArray<SegmentNative>(segments.ToArray(), Allocator.TempJob),
                        caps = new NativeArray<PathNodeNative>(caps.ToArray(), Allocator.TempJob),
                        
                        yOffset = _vis.yOffset,
                        shadowPlanarOffset = new float2(_vis.shadowOffset.x, _vis.shadowOffset.z),
                        shadowYOffset = _vis.shadowOffset.y,

                        rVerts = new NativeList<float3>(Allocator.TempJob), rNorms = new NativeList<float3>(Allocator.TempJob), rUVs = new NativeList<float2>(Allocator.TempJob), rTris = new NativeList<int>(Allocator.TempJob),
                        cVerts = new NativeList<float3>(Allocator.TempJob), cNorms = new NativeList<float3>(Allocator.TempJob), cUVs = new NativeList<float2>(Allocator.TempJob), cTris = new NativeList<int>(Allocator.TempJob),
                        sVerts = new NativeList<float3>(Allocator.TempJob), sNorms = new NativeList<float3>(Allocator.TempJob), sUVs = new NativeList<float2>(Allocator.TempJob), sTris = new NativeList<int>(Allocator.TempJob)
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

                job.segments.Dispose(); job.caps.Dispose();
                job.rVerts.Dispose(); job.rNorms.Dispose(); job.rUVs.Dispose(); job.rTris.Dispose();
                job.cVerts.Dispose(); job.cNorms.Dispose(); job.cUVs.Dispose(); job.cTris.Dispose();
                job.sVerts.Dispose(); job.sNorms.Dispose(); job.sUVs.Dispose(); job.sTris.Dispose();
            }

            _state = TaskState.Complete;
            return true;
        }

        return false;
    }

    // --- MAIN THREAD HELPERS ---

    private bool IsRealNode(RoadCell cell) 
    { 
        // 1. Standard Intersection or Dead End
        if (RoadSystemBackend.Instance.GetConnectionCount(cell) != 2) return true; 

        // 2. Transition Node (Mirroring your Traffic System logic)
        for (int i = 0; i < 8; i++)
        {
            int bit = 1 << i;
            if (cell.HasConnection(bit))
            {
                Vector2Int nPos = RoadSystemBackend.Instance.GetNeighborPosition(cell.gridPosition, bit);
                if (_roadData.TryGetValue(nPos, out RoadCell neighbor))
                {
                    if (neighbor.roadType != cell.roadType) return true;
                }
            }
        }
        return false;
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

    private LogicalPath TracePath(RoadCell startCell, Vector2Int firstNeighborPos)
    {
        List<PathNodeNative> path = new List<PathNodeNative>();
        Vector3 startPos = GetCellWorldCenter(startCell.gridPosition);

        // FIX: If the start node is an intersection, flare the width to match the intersection cap!
        float startWidth = startCell.roadType.roadWidth;
        float startCurb = startCell.roadType.curbWidth;
        if (IsRealNode(startCell)) GetMaxIntersectionWidth(startCell, out startWidth, out startCurb);

        path.Add(new PathNodeNative { position = startPos, roadWidth = startWidth, curbWidth = startCurb });
        
        RoadCell prev = startCell;
        
        // Failsafe exit
        if (!_roadData.TryGetValue(firstNeighborPos, out RoadCell curr)) 
            return new LogicalPath { type = startCell.roadType, nodes = path, isPreview = startCell.isPreview };

        // Do NOT allow a single visual trace to cross the boundary between a permanent and preview road
        if (curr.isPreview != startCell.isPreview)
        {
            path.Add(new PathNodeNative { position = GetCellWorldCenter(curr.gridPosition), roadWidth = prev.roadType.roadWidth, curbWidth = prev.roadType.curbWidth });
            return new LogicalPath { type = startCell.roadType, nodes = path, isPreview = startCell.isPreview };
        }

        _visitedSegments.Add(new EdgeHash(prev.gridPosition, curr.gridPosition));

        while (true)
        {
            Vector3 currPos = GetCellWorldCenter(curr.gridPosition);
            path.Add(new PathNodeNative { position = currPos, roadWidth = curr.roadType.roadWidth, curbWidth = curr.roadType.curbWidth });

            // Break if intersection, loop, OR preview status changes
            if (IsRealNode(curr) || curr.gridPosition == startCell.gridPosition || curr.isPreview != startCell.isPreview) 
            {
                // FIX: If ending at an intersection, flare the width to perfectly seal against the cap!
                float endWidth = curr.roadType.roadWidth * 0.9f;
                float endCurb = curr.roadType.curbWidth * 0.9f;
                if (IsRealNode(curr)) GetMaxIntersectionWidth(curr, out endWidth, out endCurb);
                
                path[path.Count - 1] = new PathNodeNative { position = currPos, roadWidth = endWidth, curbWidth = endCurb };
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

        return new LogicalPath { type = startCell.roadType, nodes = path, isPreview = startCell.isPreview };
    }

    private List<PathNodeNative> ApplyChaikin(List<PathNodeNative> path, int iterations)
    {
        if (path.Count < 3 || iterations == 0) return path;
        bool isClosedLoop = Vector3.Distance(path[0].position, path[path.Count - 1].position) < 0.01f;
        List<PathNodeNative> workingPath = new List<PathNodeNative>(path);
        if (isClosedLoop) workingPath.RemoveAt(workingPath.Count - 1);

        for (int iter = 0; iter < iterations; iter++)
        {
            List<PathNodeNative> newPath = new List<PathNodeNative>();
            if (!isClosedLoop) newPath.Add(workingPath[0]);

            for (int i = 0; i < workingPath.Count - (isClosedLoop ? 0 : 1); i++)
            {
                PathNodeNative p0 = workingPath[i];
                PathNodeNative p1 = workingPath[(i + 1) % workingPath.Count];

                newPath.Add(new PathNodeNative {
                    position = Vector3.Lerp(p0.position, p1.position, 0.25f),
                    roadWidth = Mathf.Lerp(p0.roadWidth, p1.roadWidth, 0.25f),
                    curbWidth = Mathf.Lerp(p0.curbWidth, p1.curbWidth, 0.25f)
                });
                newPath.Add(new PathNodeNative {
                    position = Vector3.Lerp(p0.position, p1.position, 0.75f),
                    roadWidth = Mathf.Lerp(p0.roadWidth, p1.roadWidth, 0.75f),
                    curbWidth = Mathf.Lerp(p0.curbWidth, p1.curbWidth, 0.75f)
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

        public float yOffset;
        public float2 shadowPlanarOffset;
        public float shadowYOffset;

        public NativeList<float3> rVerts; public NativeList<float3> rNorms; public NativeList<float2> rUVs; public NativeList<int> rTris;
        public NativeList<float3> cVerts; public NativeList<float3> cNorms; public NativeList<float2> cUVs; public NativeList<int> cTris;
        public NativeList<float3> sVerts; public NativeList<float3> sNorms; public NativeList<float2> sUVs; public NativeList<int> sTris;

        public void Execute()
        {
            for (int i = 0; i < segments.Length; i++)
            {
                SegmentNative seg = segments[i];

                ExtrudeSegment(seg.p0_pos, seg.p1_pos, seg.p0_fwd, seg.p1_fwd, seg.p0_w, seg.p1_w, yOffset, float2.zero, ref rVerts, ref rUVs, ref rNorms, ref rTris);
                ExtrudeSegment(seg.p0_pos, seg.p1_pos, seg.p0_fwd, seg.p1_fwd, seg.p0_w + seg.p0_c, seg.p1_w + seg.p1_c, yOffset - 0.02f, float2.zero, ref cVerts, ref cUVs, ref cNorms, ref cTris);
                ExtrudeSegment(seg.p0_pos, seg.p1_pos, seg.p0_fwd, seg.p1_fwd, seg.p0_w + seg.p0_c, seg.p1_w + seg.p1_c, yOffset + shadowYOffset, shadowPlanarOffset, ref sVerts, ref sUVs, ref sNorms, ref sTris);
            }

            for (int i = 0; i < caps.Length; i++)
            {
                PathNodeNative cap = caps[i];
                GenerateCap(cap.position, cap.roadWidth, yOffset, float2.zero, ref rVerts, ref rUVs, ref rNorms, ref rTris);
                GenerateCap(cap.position, cap.roadWidth + cap.curbWidth, yOffset - 0.02f, float2.zero, ref cVerts, ref cUVs, ref cNorms, ref cTris);
            }
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
