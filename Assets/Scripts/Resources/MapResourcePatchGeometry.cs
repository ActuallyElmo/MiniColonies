using System.Collections.Generic;
using UnityEngine;

// Splits existing terrain triangles along broad patch edges. Both sides retain their
// original height plane, winding and solid color; no overlay or pixel-sized fragments.
public sealed class MapResourcePatchGeometry
{
    public readonly List<Vector3> Vertices = new List<Vector3>();
    public readonly List<int> Triangles = new List<int>();
    // Rendering classification only. These flags cannot answer whether a tile owns a resource.
    public readonly List<bool> UsesResourceTint = new List<bool>();
    private readonly Vector3[] _polygon = new Vector3[16];
    private readonly Vector3[] _inside = new Vector3[16];
    private readonly Vector3[] _outside = new Vector3[16];

    public void Clear()
    {
        Vertices.Clear();
        Triangles.Clear();
        UsesResourceTint.Clear();
    }

    // Keep decorative prefab origins on the visible patch when its tile coverage shrinks.
    public static Vector2 ConstrainPoint(Vector2 point, Vector3[] cuts, int cutCount)
    {
        var center = new Vector2(0.5f, 0.5f);
        float fraction = 1f;
        for (int i = 0; i < cutCount; i++)
        {
            Vector3 plane = cuts[i];
            float distance = plane.x * point.x + plane.y * point.y + plane.z;
            if (distance >= 0f) continue;
            float centerDistance = plane.x * center.x + plane.y * center.y + plane.z;
            fraction = Mathf.Min(fraction, centerDistance / (centerDistance - distance) * 0.95f);
        }
        return fraction < 1f ? Vector2.LerpUnclamped(center, point, fraction) : point;
    }

    // Positions are (tile-local X, physical height, tile-local Z).
    public void AppendTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3[] cuts, int cutCount)
    {
        _polygon[0] = a; _polygon[1] = b; _polygon[2] = c;
        int count = 3;
        for (int cut = 0; cut < cutCount && count >= 3; cut++)
        {
            int insideCount = 0, outsideCount = 0;
            Vector3 plane = cuts[cut];
            Vector3 previous = _polygon[count - 1];
            float previousDistance = Distance(previous, plane);
            for (int i = 0; i < count; i++)
            {
                Vector3 current = _polygon[i];
                float distance = Distance(current, plane);
                if ((distance >= 0f) != (previousDistance >= 0f))
                {
                    Vector3 intersection = Vector3.LerpUnclamped(previous, current,
                        previousDistance / (previousDistance - distance));
                    Add(_inside, ref insideCount, intersection);
                    Add(_outside, ref outsideCount, intersection);
                }
                if (distance >= 0f) Add(_inside, ref insideCount, current);
                else Add(_outside, ref outsideCount, current);
                previous = current;
                previousDistance = distance;
            }
            Emit(_outside, outsideCount, false);
            count = insideCount;
            for (int i = 0; i < count; i++) _polygon[i] = _inside[i];
        }
        Emit(_polygon, count, true);
    }

    private static float Distance(Vector3 point, Vector3 plane) =>
        plane.x * point.x + plane.y * point.z + plane.z;

    private static void Add(Vector3[] buffer, ref int count, Vector3 point)
    {
        if (count == 0 || (buffer[count - 1] - point).sqrMagnitude > 1e-14f)
            buffer[count++] = point;
    }

    private void Emit(Vector3[] polygon, int count, bool resource)
    {
        if (count > 1 && (polygon[0] - polygon[count - 1]).sqrMagnitude < 1e-14f) count--;
        if (count < 3) return;
        int start = Vertices.Count;
        for (int i = 1; i < count - 1; i++)
        {
            Vector3 ab = polygon[i] - polygon[0], ac = polygon[i + 1] - polygon[0];
            if (Mathf.Abs(ab.x * ac.z - ab.z * ac.x) < 1e-12f) continue;
            Triangles.Add(start); Triangles.Add(start + i); Triangles.Add(start + i + 1);
        }
        for (int i = 0; i < count; i++)
        {
            Vertices.Add(polygon[i]);
            UsesResourceTint.Add(resource);
        }
    }
}
