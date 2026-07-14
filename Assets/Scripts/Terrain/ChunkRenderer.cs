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

    public void GenerateMesh()
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Color> colors = new List<Color>();

        float worldRadius = _world.worldSize / 2f;
        float extendedRadius = worldRadius + _world.extrusion;
        Vector2 worldCenter = new Vector2(worldRadius, worldRadius);

        Vector3 chunkOffset = new Vector3(_chunkX * _chunkSize * _world.cellSize, 0, _chunkZ * _chunkSize * _world.cellSize);

        int subDivs = _world.meshSubdivisions;
        float subStep = 1f / subDivs;
        float physSize = _world.cellSize;

        for (int x = 0; x < _chunkSize; x++)
        {
            for (int z = 0; z < _chunkSize; z++)
            {
                int gx = (_chunkX * _chunkSize) + x;
                int gz = (_chunkZ * _chunkSize) + z;

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

                        // Calculate the specific smooth darkening factor for EACH vertex individually
                        float fBL = _world.GetExtrusionDarkenFactor(gx0, gz0);
                        float fBR = _world.GetExtrusionDarkenFactor(gx1, gz0);
                        float fTL = _world.GetExtrusionDarkenFactor(gx0, gz1);
                        float fTR = _world.GetExtrusionDarkenFactor(gx1, gz1);

                        // Apply the gradient mathematically
                        Color cBL = new Color(baseTint.r * fBL, baseTint.g * fBL, baseTint.b * fBL, baseTint.a);
                        Color cBR = new Color(baseTint.r * fBR, baseTint.g * fBR, baseTint.b * fBR, baseTint.a);
                        Color cTL = new Color(baseTint.r * fTL, baseTint.g * fTL, baseTint.b * fTL, baseTint.a);
                        Color cTR = new Color(baseTint.r * fTR, baseTint.g * fTR, baseTint.b * fTR, baseTint.a);

                        int vIndex = vertices.Count;

                        vertices.Add(pBL); colors.Add(cBL);
                        vertices.Add(pTL); colors.Add(cTL);
                        vertices.Add(pTR); colors.Add(cTR);
                        vertices.Add(pBR); colors.Add(cBR);

                        triangles.Add(vIndex);
                        triangles.Add(vIndex + 1);
                        triangles.Add(vIndex + 2);
                        triangles.Add(vIndex);
                        triangles.Add(vIndex + 2);
                        triangles.Add(vIndex + 3);
                    }
                }
            }
        }

        _mesh.Clear();
        _mesh.vertices = vertices.ToArray();
        _mesh.triangles = triangles.ToArray();
        _mesh.colors = colors.ToArray();
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();

        _meshCollider.sharedMesh = _mesh;
    }
}