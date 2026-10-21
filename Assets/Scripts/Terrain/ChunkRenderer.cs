using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class ChunkRenderer : MonoBehaviour
{
    private Mesh _mesh;
    private MeshCollider _meshCollider;
    private int _chunkX, _chunkZ, _chunkSize;
    private WorldManager _world;

    public void Initialize(WorldManager world, int x, int z, int size, Material mat)
    {
        _world = world;
        _chunkX = x;
        _chunkZ = z;
        _chunkSize = size;
        
        GetComponent<MeshRenderer>().sharedMaterial = mat;
        GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _meshCollider = GetComponent<MeshCollider>();
        
        _mesh = new Mesh();
        _mesh.name = $"Chunk_{x}_{z}";
        GetComponent<MeshFilter>().sharedMesh = _mesh;
    }

    private void OnDestroy()
    {
        if (_mesh == null) return;

        if (Application.isPlaying) Destroy(_mesh);
        else DestroyImmediate(_mesh);
        _mesh = null;
    }

    public void GenerateMesh()
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Color> colors = new List<Color>();
        var uvs = new List<Vector2>();
        var materials = new List<Material> { _world.terrainMaterial };
        var submeshes = new List<List<int>> { triangles };
        float paletteBrightness = 0f;
        foreach (var layer in _world.layers) paletteBrightness = Mathf.Max(paletteBrightness, layer.layerColor.maxColorComponent);
        int blendSeed = MapResourceGenerator.StableSeed(_world.seed + "::resource-blend");
        var patchGeometry = new MapResourcePatchGeometry();
        var polygonCuts = new Vector3[MapResourceTerrainAppearance.MaximumPolygonCuts];

        float worldRadius = _world.worldSize / 2f;
        float extendedRadius = worldRadius + _world.extrusion;
        Vector2 worldCenter = new Vector2(worldRadius, worldRadius);

        Vector3 chunkOffset = new Vector3(_chunkX * _chunkSize * _world.cellSize, 0, _chunkZ * _chunkSize * _world.cellSize);

        int subDivs = _world.TerrainMeshSubdivisions;
        float subStep = 1f / subDivs;
        float physSize = _world.cellSize;

        for (int x = 0; x < _chunkSize; x++)
        {
            for (int z = 0; z < _chunkSize; z++)
            {
                int gx = (_chunkX * _chunkSize) + x;
                int gz = (_chunkZ * _chunkSize) + z;
                Material resourceMaterial = _world.ResourceMap.TryGetSource(new Vector2Int(gx, gz), out var source)
                    ? source.Definition.terrainMaterial : null;
                int materialIndex = 0;
                if (resourceMaterial != null)
                {
                    materialIndex = materials.IndexOf(resourceMaterial);
                    if (materialIndex < 0)
                    {
                        materialIndex = materials.Count;
                        materials.Add(resourceMaterial);
                        submeshes.Add(new List<int>());
                    }
                }
                List<int> cellTriangles = submeshes[materialIndex];
                int boundaryMask = resourceMaterial != null
                    ? MapResourceTerrainAppearance.BoundaryMask(_world.ResourceMap, new Vector2Int(gx, gz), source.Definition) : 0;
                int resourceBlendSeed = source != null ? unchecked(blendSeed ^ ((int)source.Definition.resourceType * 397)) : blendSeed;
                bool polygonal = resourceMaterial != null &&
                    source.Definition.terrainTransitionStyle == MapResourceTerrainTransition.Polygonal;
                int cutCount = polygonal ? MapResourceTerrainAppearance.BuildPolygonCuts(
                    new Vector2Int(gx, gz), boundaryMask, resourceBlendSeed, source.Definition, polygonCuts) : 0;

                if (_world.makeCircular)
                {
                    Vector2 quadCenter = new Vector2(gx + 0.5f, gz + 0.5f);
                    if (Vector2.Distance(quadCenter, worldCenter) > extendedRadius) continue;
                }
                else
                {
                    if (gx < -_world.extrusion || gx >= _world.worldSize + _world.extrusion || 
                        gz < -_world.extrusion || gz >= _world.worldSize + _world.extrusion) 
                        continue;
                }

                for (int sx = 0; sx < subDivs; sx++)
                {
                    for (int sz = 0; sz < subDivs; sz++)
                    {
                        float u0 = sx * subStep;
                        float u1 = (sx + 1) * subStep;
                        float v0 = sz * subStep;
                        float v1 = (sz + 1) * subStep;

                        // Precise float coordinates of the 4 corners
                        float gx0 = gx + u0;
                        float gx1 = gx + u1;
                        float gz0 = gz + v0;
                        float gz1 = gz + v1;

                        float hBL = _world.GetPhysicalHeight(gx0, gz0);
                        float hBR = _world.GetPhysicalHeight(gx1, gz0);
                        float hTL = _world.GetPhysicalHeight(gx0, gz1);
                        float hTR = _world.GetPhysicalHeight(gx1, gz1);

                        Vector3 pBL = new Vector3(gx0 * physSize, hBL * _world.heightStep, gz0 * physSize) - chunkOffset;
                        Vector3 pBR = new Vector3(gx1 * physSize, hBR * _world.heightStep, gz0 * physSize) - chunkOffset;
                        Vector3 pTL = new Vector3(gx0 * physSize, hTL * _world.heightStep, gz1 * physSize) - chunkOffset;
                        Vector3 pTR = new Vector3(gx1 * physSize, hTR * _world.heightStep, gz1 * physSize) - chunkOffset;

                        float averageHeight = (hBL + hBR + hTL + hTR) / 4f;
                        
                        // Get the base color for the flat quad
                        Color baseTint = _world.GetLayerColor(Mathf.RoundToInt(averageHeight));
                        if (polygonal)
                        {
                            Color resourceTint = MapResourceTerrainAppearance.ShadeTint(baseTint, source.Definition.terrainTint, paletteBrightness);
                            patchGeometry.Clear();
                            var bl = new Vector3(u0, hBL * _world.heightStep, v0);
                            var br = new Vector3(u1, hBR * _world.heightStep, v0);
                            var tl = new Vector3(u0, hTL * _world.heightStep, v1);
                            var tr = new Vector3(u1, hTR * _world.heightStep, v1);
                            patchGeometry.AppendTriangle(bl, tl, tr, polygonCuts, cutCount);
                            patchGeometry.AppendTriangle(bl, tr, br, polygonCuts, cutCount);
                            int start = vertices.Count;
                            for (int i = 0; i < patchGeometry.Vertices.Count; i++)
                            {
                                Vector3 point = patchGeometry.Vertices[i];
                                float gridX = gx + point.x, gridZ = gz + point.z;
                                Color tint = patchGeometry.UsesResourceTint[i] ? resourceTint : baseTint;
                                float darken = _world.GetExtrusionDarkenFactor(gridX, gridZ);
                                vertices.Add(new Vector3(gridX * physSize, point.y, gridZ * physSize) - chunkOffset);
                                colors.Add(new Color(tint.r * darken, tint.g * darken, tint.b * darken, tint.a));
                                uvs.Add(new Vector2(gridX, gridZ));
                            }
                            foreach (int index in patchGeometry.Triangles) cellTriangles.Add(start + index);
                            continue;
                        }
                        Color tintBL = baseTint, tintBR = baseTint, tintTL = baseTint, tintTR = baseTint;
                        if (resourceMaterial != null)
                        {
                            Color resourceTint = MapResourceTerrainAppearance.ShadeTint(baseTint, source.Definition.terrainTint, paletteBrightness);
                            float width = source.Definition.terrainBlendWidth;
                            float amount = source.Definition.terrainBlendAmount;
                            float roundness = source.Definition.terrainEdgeRoundness;
                            float randomness = boundaryMask != 0 && width > 0f && amount > 0f
                                ? source.Definition.terrainBlendRandomness : 0f;
                            float noiseSize = source.Definition.terrainBlendNoiseSize;
                            float noiseBL = MapResourceTerrainAppearance.BlendVariation(gx0, gz0, resourceBlendSeed, randomness, noiseSize);
                            float noiseBR = MapResourceTerrainAppearance.BlendVariation(gx1, gz0, resourceBlendSeed, randomness, noiseSize);
                            float noiseTL = MapResourceTerrainAppearance.BlendVariation(gx0, gz1, resourceBlendSeed, randomness, noiseSize);
                            float noiseTR = MapResourceTerrainAppearance.BlendVariation(gx1, gz1, resourceBlendSeed, randomness, noiseSize);
                            tintBL = Color.Lerp(baseTint, resourceTint, MapResourceTerrainAppearance.BlendWeight(u0, v0, boundaryMask, width, amount, roundness, noiseBL));
                            tintBR = Color.Lerp(baseTint, resourceTint, MapResourceTerrainAppearance.BlendWeight(u1, v0, boundaryMask, width, amount, roundness, noiseBR));
                            tintTL = Color.Lerp(baseTint, resourceTint, MapResourceTerrainAppearance.BlendWeight(u0, v1, boundaryMask, width, amount, roundness, noiseTL));
                            tintTR = Color.Lerp(baseTint, resourceTint, MapResourceTerrainAppearance.BlendWeight(u1, v1, boundaryMask, width, amount, roundness, noiseTR));
                        }

                        // Calculate the specific smooth darkening factor for EACH vertex individually
                        float fBL = _world.GetExtrusionDarkenFactor(gx0, gz0);
                        float fBR = _world.GetExtrusionDarkenFactor(gx1, gz0);
                        float fTL = _world.GetExtrusionDarkenFactor(gx0, gz1);
                        float fTR = _world.GetExtrusionDarkenFactor(gx1, gz1);

                        // Apply the gradient mathematically
                        Color cBL = new Color(tintBL.r * fBL, tintBL.g * fBL, tintBL.b * fBL, tintBL.a);
                        Color cBR = new Color(tintBR.r * fBR, tintBR.g * fBR, tintBR.b * fBR, tintBR.a);
                        Color cTL = new Color(tintTL.r * fTL, tintTL.g * fTL, tintTL.b * fTL, tintTL.a);
                        Color cTR = new Color(tintTR.r * fTR, tintTR.g * fTR, tintTR.b * fTR, tintTR.a);

                        int vIndex = vertices.Count;

                        vertices.Add(pBL); colors.Add(cBL);
                        vertices.Add(pTL); colors.Add(cTL);
                        vertices.Add(pTR); colors.Add(cTR);
                        vertices.Add(pBR); colors.Add(cBR);
                        uvs.Add(new Vector2(gx0, gz0));
                        uvs.Add(new Vector2(gx0, gz1));
                        uvs.Add(new Vector2(gx1, gz1));
                        uvs.Add(new Vector2(gx1, gz0));

                        cellTriangles.Add(vIndex);
                        cellTriangles.Add(vIndex + 1);
                        cellTriangles.Add(vIndex + 2);
                        cellTriangles.Add(vIndex);
                        cellTriangles.Add(vIndex + 2);
                        cellTriangles.Add(vIndex + 3);
                    }
                }
            }
        }

        _mesh.Clear();
        _mesh.indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        _mesh.vertices = vertices.ToArray();
        _mesh.subMeshCount = submeshes.Count;
        for (int i = 0; i < submeshes.Count; i++) _mesh.SetTriangles(submeshes[i], i);
        _mesh.SetUVs(0, uvs);
        _mesh.colors = colors.ToArray();
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();

        _meshCollider.sharedMesh = _mesh;
        GetComponent<MeshRenderer>().sharedMaterials = materials.ToArray();
    }
}
