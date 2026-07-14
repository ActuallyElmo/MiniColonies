using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;

[System.Serializable]
public class TerrainLayer
{
    public string name = "New Layer";
    [Range(0f, 1f)]
    public float noiseThreshold;
    public Color layerColor = Color.white;
    
    [Tooltip("If a generated patch of this layer has fewer vertices than this, it will be overwritten by a neighboring layer.")]
    public int minRegionSize = 0; 

    [Tooltip("If false, buildings/roads cannot be placed here, and the layer shows as red in build mode.")]
    public bool isBuildable = true;
}

public class WorldManager : MonoBehaviour
{
    [Header("World Settings (Playable)")]
    public int worldSize = 100;
    public int chunkSize = 10;
    public string seed = "ColonySim";
    public bool randomizeSeedOnPlay = true;

    [Header("Performance Settings")]
    [Tooltip("Maximum milliseconds allowed per frame during async generation. 8-10ms is good for maintaining 60FPS.")]
    public float maxMillisecondsPerFrame = 8f;

    [Header("World Settings (Extrusion / Visuals)")]
    [Tooltip("How many extra cells to draw OUTSIDE the worldSize. Purely visual, unplayable.")]
    public int extrusion = 10;
    [Tooltip("How much darker the extruded terrain should be (1 = normal, 0 = black).")]
    [Range(0.1f, 1f)]
    public float extrusionDarkenFactor = 0.6f;
    [Tooltip("How many tiles it takes to smoothly fade into the maximum darkness.")]
    public float extrusionFadeDistance = 5f;

    [Tooltip("How many tiny quads to draw inside each logic cell. Higher = smoother curves.")]
    [Range(1, 16)]
    public int meshSubdivisions = 4;
    
    [Header("Visual Smoothing")]
    [Tooltip("How wide the circular curves should be. 2 or 3 is usually perfect.")]
    public int curveRadius = 2;
    public float[,] visualHeights;
    
    [Header("Debugging")]
    public bool showDebugGrid = false;
    public Color gridColor = new Color(1f, 1f, 1f, 0.3f);
    [Tooltip("How much physical height variation is allowed before a tile is marked as a Transition.")]
    [Range(0.01f, 0.9f)]
    public float slopeTolerance = 0.05f;
    [Tooltip("How sharp the cliff walls are. 0.1 is a gentle hill, 0.99 is a sheer cliff.")]
    [Range(0.1f, 0.99f)]
    public float cliffSteepness = 0.8f;
    
    [Header("Topology")]
    public bool makeCircular = true;
    public float cellSize = 1.0f;
    public float heightStep = 1.0f;
    public float noiseScale = 0.05f;

    [Header("Visuals")]
    public Material terrainMaterial;
    public List<TerrainLayer> layers = new List<TerrainLayer>();

    private Texture2D _gridDataTexture;
    private int[,] _elevationData;
    private Dictionary<Vector2Int, ChunkRenderer> _chunks = new Dictionary<Vector2Int, ChunkRenderer>();
    private Stopwatch _genTimer = new Stopwatch();

    public static WorldManager Instance { get; private set; }
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        if (randomizeSeedOnPlay)
        {
            seed = Random.Range(10000, 99999).ToString(); 
        }
        GenerateWorld();
    }

    [ContextMenu("Generate World")]
    public void GenerateWorld()
    {
        if (layers.Count == 0) return;

        if (Application.isPlaying) StartCoroutine(GenerateWorldAsync());
        else GenerateWorldSync(); 
    }

    private IEnumerator GenerateWorldAsync()
    {
        PrepareGeneration();
        yield return null; 

        GenerateWorldData();
        yield return null; 

        CleanUpRegions(); 
        yield return null; 

        CalculateVisualHeights(); 
        yield return null; 
        
        yield return StartCoroutine(BuildChunksAsyncTimeBoxed());
        GenerateGridDataTexture();
    }

    private void GenerateWorldSync()
    {
        PrepareGeneration();
        GenerateWorldData();
        CleanUpRegions();
        CalculateVisualHeights();
        BuildChunksSync();
        GenerateGridDataTexture();
    }

    private void PrepareGeneration()
    {
        ClearWorld();
        Shader.SetGlobalFloat("_MaxLayers", layers.Count); 
        Shader.SetGlobalFloat("_HeightStep", heightStep);
    }

    [ContextMenu("Clear World")]
    public void ClearWorld()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
        _chunks.Clear();
        _elevationData = null;
    }

    // --- INTERNAL DATA GENERATION (HIDDEN ARRAY SIZE) ---
    // The internal arrays are sized up to handle the extrusion math,
    // ensuring the generated border noise perfectly matches the playable edge.
    private int InternalSize => worldSize + (extrusion * 2);

    private void GenerateWorldData()
    {
        _elevationData = new int[InternalSize + 1, InternalSize + 1];
        
        System.Random prng = new System.Random(seed.GetHashCode());
        float offsetX = prng.Next(-100000, 100000);
        float offsetZ = prng.Next(-100000, 100000);

        for (int x = 0; x <= InternalSize; x++)
        {
            for (int z = 0; z <= InternalSize; z++)
            {
                float xCoord = (x + offsetX) * noiseScale;
                float zCoord = (z + offsetZ) * noiseScale;
                float noiseVal = Mathf.PerlinNoise(xCoord, zCoord);
                
                int assignedLayer = 0;
                for (int i = 0; i < layers.Count; i++)
                {
                    if (noiseVal >= layers[i].noiseThreshold) assignedLayer = i;
                }
                _elevationData[x, z] = assignedLayer;
            }
        }
    }

    private void CalculateVisualHeights()
    {
        visualHeights = new float[InternalSize + 1, InternalSize + 1];

        for (int x = 0; x <= InternalSize; x++)
        {
            for (int z = 0; z <= InternalSize; z++)
            {
                float sum = 0;
                int count = 0;

                for (int bx = -curveRadius; bx <= curveRadius; bx++)
                {
                    for (int bz = -curveRadius; bz <= curveRadius; bz++)
                    {
                        int nx = Mathf.Clamp(x + bx, 0, InternalSize);
                        int nz = Mathf.Clamp(z + bz, 0, InternalSize);

                        if (Vector2.Distance(new Vector2(x, z), new Vector2(nx, nz)) <= curveRadius)
                        {
                            sum += _elevationData[nx, nz];
                            count++;
                        }
                    }
                }
                visualHeights[x, z] = sum / count;
            }
        }
    }

    private void CleanUpRegions()
    {
        bool[,] visited = new bool[InternalSize + 1, InternalSize + 1];

        for (int x = 0; x <= InternalSize; x++)
        {
            for (int z = 0; z <= InternalSize; z++)
            {
                if (!visited[x, z])
                {
                    int currentLayer = _elevationData[x, z];
                    int requiredMinSize = layers[currentLayer].minRegionSize;

                    if (requiredMinSize <= 0) continue;

                    List<Vector2Int> region = new List<Vector2Int>();
                    Dictionary<int, int> edgeNeighborCounts = new Dictionary<int, int>();
                    Queue<Vector2Int> queue = new Queue<Vector2Int>();

                    queue.Enqueue(new Vector2Int(x, z));
                    visited[x, z] = true;

                    while (queue.Count > 0)
                    {
                        Vector2Int current = queue.Dequeue();
                        region.Add(current);

                        Vector2Int[] directions = {
                            new Vector2Int(current.x + 1, current.y),
                            new Vector2Int(current.x - 1, current.y),
                            new Vector2Int(current.x, current.y + 1),
                            new Vector2Int(current.x, current.y - 1)
                        };

                        foreach (Vector2Int neighbor in directions)
                        {
                            if (neighbor.x >= 0 && neighbor.x <= InternalSize && neighbor.y >= 0 && neighbor.y <= InternalSize)
                            {
                                int neighborLayer = _elevationData[neighbor.x, neighbor.y];

                                if (neighborLayer == currentLayer)
                                {
                                    if (!visited[neighbor.x, neighbor.y])
                                    {
                                        visited[neighbor.x, neighbor.y] = true;
                                        queue.Enqueue(neighbor);
                                    }
                                }
                                else
                                {
                                    if (edgeNeighborCounts.ContainsKey(neighborLayer)) edgeNeighborCounts[neighborLayer]++;
                                    else edgeNeighborCounts[neighborLayer] = 1;
                                }
                            }
                        }
                    }

                    if (region.Count < requiredMinSize)
                    {
                        int majorityNeighborLayer = currentLayer;
                        int highestCount = -1;

                        foreach (var kvp in edgeNeighborCounts)
                        {
                            if (kvp.Value > highestCount)
                            {
                                highestCount = kvp.Value;
                                majorityNeighborLayer = kvp.Key;
                            }
                        }

                        foreach (Vector2Int pos in region)
                        {
                            _elevationData[pos.x, pos.y] = majorityNeighborLayer;
                        }
                    }
                }
            }
        }
    }

    // --- CHUNK GENERATION ---
    private IEnumerator BuildChunksAsyncTimeBoxed()
    {
        int minChunk = Mathf.FloorToInt(-extrusion / (float)chunkSize);
        int maxChunk = Mathf.CeilToInt((worldSize + extrusion) / (float)chunkSize);

        _genTimer.Restart();

        for (int cx = minChunk; cx < maxChunk; cx++)
        {
            for (int cz = minChunk; cz < maxChunk; cz++)
            {
                SpawnChunk(cx, cz);

                // We evaluate the timer AFTER spawning the chunk. 
                // If the chunk took us over the limit, we pause.
                if (_genTimer.ElapsedMilliseconds >= maxMillisecondsPerFrame)
                {
                    yield return null; 
                    _genTimer.Restart();
                }
            }
        }
    }

    private void BuildChunksSync()
    {
        int minChunk = Mathf.FloorToInt(-extrusion / (float)chunkSize);
        int maxChunk = Mathf.CeilToInt((worldSize + extrusion) / (float)chunkSize);

        for (int cx = minChunk; cx < maxChunk; cx++)
        {
            for (int cz = minChunk; cz < maxChunk; cz++) SpawnChunk(cx, cz);
        }
    }

    private void SpawnChunk(int cx, int cz)
    {
        Vector2Int chunkPos = new Vector2Int(cx, cz);
        GameObject chunkObj = new GameObject($"Chunk_{cx}_{cz}");
        chunkObj.transform.parent = this.transform;
        chunkObj.layer = LayerMask.NameToLayer("Terrain");
        
        float chunkPhysicalSize = chunkSize * cellSize;
        
        // NO MORE OFFSETS! Playable chunk 0,0 is perfectly locked to World Space 0,0,0
        chunkObj.transform.position = new Vector3(cx * chunkPhysicalSize, 0, cz * chunkPhysicalSize);

        ChunkRenderer chunk = chunkObj.AddComponent<ChunkRenderer>();
        chunk.Initialize(this, cx, cz, chunkSize, terrainMaterial);
        chunk.GenerateMesh();

        _chunks.Add(chunkPos, chunk);
    }

    private void GenerateGridDataTexture()
    {
        // This is explicitly sized to worldSize so it perfectly fits your build grid
        if (_gridDataTexture == null || _gridDataTexture.width != worldSize)
        {
            _gridDataTexture = new Texture2D(worldSize, worldSize, TextureFormat.RGBA32, false);
            _gridDataTexture.filterMode = FilterMode.Point; 
            _gridDataTexture.wrapMode = TextureWrapMode.Clamp;
        }

        for (int x = 0; x < worldSize; x++)
        {
            for (int z = 0; z < worldSize; z++)
            {
                Color cellColor;
                int layer = GetCellLayer(x, z);
                
                if (IsTransition(x, z) || !IsLayerBuildable(layer) || IsExtrusionCell(x, z))
{
                    float divisor = Mathf.Max(1f, layers.Count - 1f);
                    float lerpValue = (float)layer / divisor; 
                    cellColor = Color.Lerp(new Color(1f, 0.2f, 0.2f), new Color(0.9f, 0f, 0f), lerpValue);
                }
                else cellColor = GetLayerColor(layer);
                
                _gridDataTexture.SetPixel(x, z, cellColor);
            }
        }
        
        _gridDataTexture.Apply();
        Shader.SetGlobalTexture("_GridDataTex", _gridDataTexture);
        Shader.SetGlobalFloat("_WorldPhysicalSize", worldSize * cellSize);
    }

    public float GetExtrusionDarkenFactor(float globalX, float globalZ)
    {
        float distOut = 0f;

        if (makeCircular)
        {
            Vector2 center = new Vector2(worldSize / 2f, worldSize / 2f);
            float dist = Vector2.Distance(new Vector2(globalX, globalZ), center);
            
            // Start the darkening exactly at the new -1f clamp border
            distOut = Mathf.Max(0, dist - ((worldSize / 2f) - 1f));
        }
        else
        {
            // The un-darkened playable area now strictly sits between 1 and worldSize - 1
            float minBound = 1f;
            float maxBound = worldSize - 1f;

            float dx = 0;
            if (globalX < minBound) dx = minBound - globalX;
            else if (globalX > maxBound) dx = globalX - maxBound;

            float dz = 0;
            if (globalZ < minBound) dz = minBound - globalZ;
            else if (globalZ > maxBound) dz = globalZ - maxBound;

            distOut = Mathf.Sqrt(dx * dx + dz * dz);
        }

        // If we are completely inside the playable map, return 1.0 (no darkness)
        if (distOut <= 0f) return 1f;

        // Smoothly lerp between 1.0 and the darken factor
        float t = Mathf.Clamp01(distOut / Mathf.Max(0.1f, extrusionFadeDistance));
        t = Mathf.SmoothStep(0f, 1f, t); 

        return Mathf.Lerp(1f, extrusionDarkenFactor, t);
    }

    // --- API & DATA ACCESS ---

    public bool IsExtrusionCell(int cellX, int cellZ)
    {
        if (makeCircular)
        {
            Vector2 center = new Vector2(worldSize / 2f, worldSize / 2f);
            float dist = Vector2.Distance(new Vector2(cellX + 0.5f, cellZ + 0.5f), center);
            
            // Subtract 1 from the radius to create the protective red border
            return dist > (worldSize / 2f) - 1f; 
        }
        else
        {
            // For square maps, the outermost edge row/column forms the red border
            return cellX <= 0 || cellX >= worldSize - 1 || cellZ <= 0 || cellZ >= worldSize - 1;
        }
    }

    public bool IsLayerBuildable(int layerIndex)
    {
        if (layerIndex < 0 || layerIndex >= layers.Count) return false;
        return layers[layerIndex].isBuildable;
    }

    public bool IsBuildableCell(int cellX, int cellZ)
    {
        // Immediately block interaction with the visual borders
        if (IsExtrusionCell(cellX, cellZ)) return false;

        int myLayer = GetCellLayer(cellX, cellZ);
        if (!IsLayerBuildable(myLayer)) return false;

        if (IsTransition(cellX, cellZ))
        {
            int searchRadius = 2; 
            for (int bx = -searchRadius; bx <= searchRadius; bx++)
            {
                for (int bz = -searchRadius; bz <= searchRadius; bz++)
                {
                    int nx = Mathf.Clamp(cellX + bx, 0, worldSize - 1);
                    int nz = Mathf.Clamp(cellZ + bz, 0, worldSize - 1);

                    if (!IsTransition(nx, nz)) 
                    {
                        if (!IsLayerBuildable(GetCellLayer(nx, nz))) return false;
                    }
                }
            }
        }
        return true;
    }

    public float GetPhysicalHeight(float gx, float gz)
    {
        if (visualHeights == null) return 0f;

        // Shift external grid coordinates into the internal array space
        float internalX = gx + extrusion;
        float internalZ = gz + extrusion;

        float sampleX = internalX - 0.5f;
        float sampleZ = internalZ - 0.5f;

        // Clamp to internal boundaries
        sampleX = Mathf.Clamp(sampleX, 0, InternalSize - 1);
        sampleZ = Mathf.Clamp(sampleZ, 0, InternalSize - 1);

        int xMin = Mathf.FloorToInt(sampleX);
        int xMax = Mathf.Clamp(xMin + 1, 0, InternalSize - 1);
        int zMin = Mathf.FloorToInt(sampleZ);
        int zMax = Mathf.Clamp(zMin + 1, 0, InternalSize - 1);

        float tx = sampleX - xMin;
        float tz = sampleZ - zMin;

        float h00 = visualHeights[xMin, zMin];
        float h10 = visualHeights[xMax, zMin];
        float h01 = visualHeights[xMin, zMax];
        float h11 = visualHeights[xMax, zMax];

        float heightX0 = Mathf.Lerp(h00, h10, tx);
        float heightX1 = Mathf.Lerp(h01, h11, tx);
        float rawHeight = Mathf.Lerp(heightX0, heightX1, tz);

        float baseLayer = Mathf.Floor(rawHeight);
        float slope = rawHeight - baseLayer;

        float jumpWidth = 1f - cliffSteepness;
        float lowerBound = 0.5f - (jumpWidth / 2f);
        float upperBound = 0.5f + (jumpWidth / 2f);

        if (slope <= lowerBound) slope = 0f;
        else if (slope >= upperBound) slope = 1f;
        else 
        {
            float t = (slope - lowerBound) / jumpWidth;
            slope = Mathf.SmoothStep(0f, 1f, t);
        }

        return baseLayer + slope;
    }

    public bool IsTransition(int cellX, int cellZ)
    {
        int sampleResolution = 4; 
        int slopedSamples = 0;
        float totalSamples = sampleResolution * sampleResolution;

        for (int sx = 0; sx < sampleResolution; sx++)
        {
            for (int sz = 0; sz < sampleResolution; sz++)
            {
                float u = (sx + 0.5f) / sampleResolution;
                float v = (sz + 0.5f) / sampleResolution;

                float height = GetPhysicalHeight(cellX + u, cellZ + v);
                float fractionalHeight = height - Mathf.Floor(height);

                if (fractionalHeight > slopeTolerance && fractionalHeight < (1f - slopeTolerance))
                {
                    slopedSamples++;
                }
            }
        }
        return (slopedSamples / totalSamples) > 0.15f; 
    }

    public int GetCellLayer(int cellX, int cellZ)
    {
        float centerHeight = GetPhysicalHeight(cellX + 0.5f, cellZ + 0.5f);
        return Mathf.RoundToInt(centerHeight);
    }

    public int GetElevation(int x, int z)
    {
        if (x < 0 || x > worldSize || z < 0 || z > worldSize) return 0;
        return _elevationData[x + extrusion, z + extrusion];
    }

    public Color GetLayerColor(int layerIndex)
    {
        layerIndex = Mathf.Clamp(layerIndex, 0, layers.Count - 1);
        return layers[layerIndex].layerColor;
    }

    public void SetElevation(int x, int z, int newLayerIndex)
    {
        if (x < 0 || x > worldSize || z < 0 || z > worldSize) return;
        _elevationData[x + extrusion, z + extrusion] = Mathf.Clamp(newLayerIndex, 0, layers.Count - 1);
        UpdateChunksAroundPoint(x, z);
    }

    private void UpdateChunksAroundPoint(int x, int z)
    {
        int cx = x / chunkSize;
        int cz = z / chunkSize;
        
        RefreshChunk(cx, cz);

        if (x % chunkSize == 0) RefreshChunk(cx - 1, cz);
        if (x % chunkSize == chunkSize - 1) RefreshChunk(cx + 1, cz);
        if (z % chunkSize == 0) RefreshChunk(cx, cz - 1);
        if (z % chunkSize == chunkSize - 1) RefreshChunk(cx, cz + 1);
    }

    private void RefreshChunk(int cx, int cz)
    {
        Vector2Int pos = new Vector2Int(cx, cz);
        if (_chunks.TryGetValue(pos, out ChunkRenderer chunk))
        {
            chunk.GenerateMesh();
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showDebugGrid || _elevationData == null || visualHeights == null) return;

        // Gizmos explicitly lock to the playable area only
        for (int x = 0; x < worldSize; x++)
        {
            for (int z = 0; z < worldSize; z++)
            {
                bool isTransition = IsTransition(x, z);
                int cellLayer = GetCellLayer(x, z);

                float centerX = (x + 0.5f) * cellSize;
                float centerZ = (z + 0.5f) * cellSize;
                
                float physicalCenterY = GetPhysicalHeight(x + 0.5f, z + 0.5f) * heightStep;
                Vector3 centerPos = new Vector3(centerX, physicalCenterY + 0.2f, centerZ);

                Gizmos.color = gridColor;
                Gizmos.DrawWireCube(centerPos, new Vector3(cellSize, 0, cellSize));

                string labelText = isTransition ? "T" : cellLayer.ToString();
                GUIStyle style = new GUIStyle();
                style.alignment = TextAnchor.MiddleCenter;
                style.fontStyle = FontStyle.Bold;
                style.normal.textColor = isTransition ? new Color(1f, 0.4f, 0.4f) : new Color(0.4f, 1f, 0.4f);

                UnityEditor.Handles.Label(centerPos, labelText, style);
            }
        }
    }
#endif
}