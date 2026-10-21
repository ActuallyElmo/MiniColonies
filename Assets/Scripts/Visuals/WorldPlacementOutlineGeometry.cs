using System.Collections.Generic;
using UnityEngine;

public static class WorldPlacementOutlineGeometry
{
    // Logical tile geometry, independent of object type and terrain paint.
    public static void AppendCell(Vector2Int cell, int subdivisions, Sprite outlineEdge,
        List<Vector3> vertices, List<Vector2> uvs, List<int> indices,
        ISet<Vector2Int> footprint = null, Sprite innerCorner = null)
    {
        if (outlineEdge == null || (footprint != null && !footprint.Contains(cell))) return;
        subdivisions = Mathf.Clamp(subdivisions, 1, 16);
        // Physical thickness stays fixed. Pixel density is a shader-only setting.
        float width = Mathf.Min(0.25f, 1f / outlineEdge.rect.width);
        bool Missing(Vector2Int direction) => footprint == null || !footprint.Contains(cell + direction);
        bool left = Missing(Vector2Int.left);
        bool right = Missing(Vector2Int.right);
        bool bottomEdge = Missing(Vector2Int.down);
        bool topEdge = Missing(Vector2Int.up);
        Rect rect = outlineEdge.rect;
        float u0 = (rect.xMin + 0.5f) / outlineEdge.texture.width;
        float u1 = (rect.xMax - 0.5f) / outlineEdge.texture.width;
        float v = (rect.yMax - 0.5f) / outlineEdge.texture.height;
        for (int i = 0; i < subdivisions; i++)
        {
            float start = i / (float)subdivisions;
            float end = (i + 1) / (float)subdivisions;
            if (bottomEdge) Quad(start, 0f, end, width);
            if (topEdge) Quad(start, 1f - width, end, 1f);
            // Omit corner overlap so transparent outlines have uniform alpha.
            float bottom = bottomEdge ? width : 0f;
            float top = topEdge ? 1f - width : 1f;
            if (left) Quad(0f, Mathf.Lerp(bottom, top, start), width, Mathf.Lerp(bottom, top, end));
            if (right) Quad(1f - width, Mathf.Lerp(bottom, top, start), 1f, Mathf.Lerp(bottom, top, end));
        }

        // The new sprite's white top-right pixel closes concave joins inside
        // a tile with two occupied cardinal neighbors and a missing diagonal.
        // Strips never overlap these joins, preserving uniform alpha.
        if (innerCorner != null && footprint != null)
        {
            if (!left && !bottomEdge && Missing(Vector2Int.left + Vector2Int.down)) Corner(0f, 0f);
            if (!right && !bottomEdge && Missing(Vector2Int.right + Vector2Int.down)) Corner(1f - width, 0f);
            if (!left && !topEdge && Missing(Vector2Int.left + Vector2Int.up)) Corner(0f, 1f - width);
            if (!right && !topEdge && Missing(Vector2Int.right + Vector2Int.up)) Corner(1f - width, 1f - width);
        }

        void Corner(float x, float z)
        {
            int first = uvs.Count;
            Quad(x, z, x + width, z + width);
            Vector2 uv = new Vector2((innerCorner.rect.xMax - 0.5f) / innerCorner.texture.width,
                (innerCorner.rect.yMax - 0.5f) / innerCorner.texture.height);
            for (int i = first; i < uvs.Count; i++) uvs[i] = uv;
        }

        void Quad(float left, float bottom, float right, float top)
        {
            if (right <= left || top <= bottom) return;
            AddQuad(new Vector3(left, 0f, bottom), new Vector3(left, 0f, top),
                new Vector3(right, 0f, top), new Vector3(right, 0f, bottom));
        }

        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int first = vertices.Count;
            var offset = new Vector3(cell.x, 0f, cell.y);
            vertices.Add(offset + a); vertices.Add(offset + b);
            vertices.Add(offset + c); vertices.Add(offset + d);
            // Match the building-placement outline's white top pixel-row sampling.
            uvs.Add(new Vector2(u0, v)); uvs.Add(new Vector2(u0, v));
            uvs.Add(new Vector2(u1, v)); uvs.Add(new Vector2(u1, v));
            indices.Add(first); indices.Add(first + 1); indices.Add(first + 2);
            indices.Add(first); indices.Add(first + 2); indices.Add(first + 3);
        }
    }
}
