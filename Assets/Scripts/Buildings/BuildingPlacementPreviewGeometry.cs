using System;
using System.Collections.Generic;
using UnityEngine;

[Flags]
public enum BuildingPreviewEdges
{
    None = 0,
    Left = 1,
    Right = 2,
    Bottom = 4,
    Top = 8
}

public static class BuildingPlacementPreviewGeometry
{
    public static BuildingPreviewEdges GetExposedEdges(Vector2Int cell, ISet<Vector2Int> footprint)
    {
        BuildingPreviewEdges edges = BuildingPreviewEdges.None;
        if (!footprint.Contains(cell + Vector2Int.left)) edges |= BuildingPreviewEdges.Left;
        if (!footprint.Contains(cell + Vector2Int.right)) edges |= BuildingPreviewEdges.Right;
        if (!footprint.Contains(cell + Vector2Int.down)) edges |= BuildingPreviewEdges.Bottom;
        if (!footprint.Contains(cell + Vector2Int.up)) edges |= BuildingPreviewEdges.Top;
        return edges;
    }

    // UV.z distinguishes atlas outline samples from repeating hatch samples.
    // Hatch coordinates are in footprint space, so their phase continues across cells.
    public static Mesh CreateCellMesh(
        Vector2Int cell, ISet<Vector2Int> footprint, float cellSize, Sprite outlineEdge, Sprite cellFill = null)
    {
        BuildingPreviewEdges edges = GetExposedEdges(cell, footprint);
        var vertices = new List<Vector3>(20);
        var uvs = new List<Vector3>(20);
        var triangles = new List<int>(30);
        float width = 1f / outlineEdge.rect.width;
        float x0 = cell.x - 0.5f;
        float x1 = cell.x + 0.5f;
        float z0 = cell.y - 0.5f;
        float z1 = cell.y + 0.5f;
        float innerX0 = x0 + (edges.HasFlag(BuildingPreviewEdges.Left) ? width : 0f);
        float innerX1 = x1 - (edges.HasFlag(BuildingPreviewEdges.Right) ? width : 0f);
        float innerZ0 = z0 + (edges.HasFlag(BuildingPreviewEdges.Bottom) ? width : 0f);
        float innerZ1 = z1 - (edges.HasFlag(BuildingPreviewEdges.Top) ? width : 0f);

        AppendQuad(innerX0, innerZ0, innerX1, innerZ1, true);
        if (edges.HasFlag(BuildingPreviewEdges.Bottom)) AppendQuad(x0, z0, x1, innerZ0, false);
        if (edges.HasFlag(BuildingPreviewEdges.Top)) AppendQuad(x0, innerZ1, x1, z1, false);
        if (edges.HasFlag(BuildingPreviewEdges.Left)) AppendQuad(x0, innerZ0, innerX0, innerZ1, false);
        if (edges.HasFlag(BuildingPreviewEdges.Right)) AppendQuad(innerX1, innerZ0, x1, innerZ1, false);

        var mesh = new Mesh { name = "Building preview cell " + cell };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;

        void AppendQuad(float left, float bottom, float right, float top, bool hatch)
        {
            int start = vertices.Count;
            AddVertex(left, bottom, false);
            AddVertex(left, top, false);
            AddVertex(right, top, true);
            AddVertex(right, bottom, true);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);

            void AddVertex(float x, float z, bool rightSide)
            {
                vertices.Add(new Vector3(x * cellSize, 0.1f, z * cellSize));
                if (hatch)
                {
                    if (cellFill == null) uvs.Add(new Vector3(x + 0.5f, z + 0.5f, 1f));
                    else
                    {
                        Rect fillRect = cellFill.rect;
                        uvs.Add(new Vector3(
                            (fillRect.x + 0.5f + (x - cell.x + 0.5f) * (fillRect.width - 1f)) / cellFill.texture.width,
                            (fillRect.y + 0.5f + (z - cell.y + 0.5f) * (fillRect.height - 1f)) / cellFill.texture.height, 2f));
                    }
                    return;
                }
                // Sample the white top pixel row of the supplied outline edge,
                // staying inside its rect to avoid atlas bleed.
                Rect rect = outlineEdge.rect;
                uvs.Add(new Vector3(
                    (rightSide ? rect.xMax - 0.5f : rect.xMin + 0.5f) / outlineEdge.texture.width,
                    (rect.yMax - 0.5f) / outlineEdge.texture.height, 0f));
            }
        }
    }

    public static Mesh CreateIconMesh(Sprite sprite, float diameter)
    {
        Vector2[] source = sprite.vertices;
        Vector2[] spriteUvs = sprite.uv;
        ushort[] sourceTriangles = sprite.triangles;
        var vertices = new Vector3[source.Length];
        var uvs = new Vector3[source.Length];
        var triangles = new int[sourceTriangles.Length];
        Bounds bounds = sprite.bounds;
        float scale = diameter / Mathf.Max(bounds.size.x, bounds.size.y, 0.0001f);
        for (int i = 0; i < source.Length; i++)
        {
            vertices[i] = new Vector3((source[i].x - bounds.center.x) * scale, 0f,
                (source[i].y - bounds.center.y) * scale);
            uvs[i] = new Vector3(spriteUvs[i].x, spriteUvs[i].y, 2f);
        }
        for (int i = 0; i < triangles.Length; i++) triangles[i] = sourceTriangles[i];
        var mesh = new Mesh { name = "Building ground icon " + sprite.name };
        mesh.vertices = vertices;
        mesh.SetUVs(0, new List<Vector3>(uvs));
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }
}
