using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class WorldPlacementHighlightTests
{
    private Texture2D texture;
    private Sprite edge;
    private Sprite corner;
    private BuildingData buildingData;
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Vector2> uvs = new List<Vector2>();
    private readonly List<int> indices = new List<int>();
    private const float Width = 1f / 16f;

    [SetUp]
    public void SetUp()
    {
        texture = new Texture2D(64, 32);
        edge = Sprite.Create(texture, new Rect(0, 16, 16, 16), Vector2.one * 0.5f);
        corner = Sprite.Create(texture, new Rect(32, 16, 16, 16), Vector2.zero);
        buildingData = ScriptableObject.CreateInstance<BuildingData>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(buildingData);
        Object.DestroyImmediate(corner);
        Object.DestroyImmediate(edge);
        Object.DestroyImmediate(texture);
        vertices.Clear(); uvs.Clear(); indices.Clear();
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void RotatedBuildingFootprintClosesConcaveJoinWithInnerCornerSprite(int rotation)
    {
        buildingData.footprint = new List<BuildingTile>
        {
            new BuildingTile { localPosition = Vector2Int.zero },
            new BuildingTile { localPosition = Vector2Int.up },
            new BuildingTile { localPosition = Vector2Int.right }
        };
        var footprint = new HashSet<Vector2Int>();
        var origin = new Vector2Int(7, 5);
        foreach (BuildingTile tile in buildingData.GetRotatedFootprint(rotation))
            footprint.Add(origin + tile.localPosition);
        Build(footprint);
        Assert.AreEqual(4, CornerVertexCount(), "Exactly one concave join for every rotation.");
        Assert.That(Area(), Is.EqualTo(8f * Width - 4f * Width * Width).Within(0.00001f),
            "Eight exposed sides, five convex joins, and one filled concave join.");
        foreach (Vector3 vertex in vertices)
            Assert.That(vertex.y, Is.Zero, "Height sampling belongs to the renderer.");
    }

    [Test]
    public void AdjacentTilesHaveNoInternalLineOrAlphaOverlap()
    {
        var footprint = new HashSet<Vector2Int> { Vector2Int.zero, Vector2Int.right };
        Build(footprint);
        Assert.AreEqual(0, CornerVertexCount());
        Assert.That(Area(), Is.EqualTo(6f * Width - 4f * Width * Width).Within(0.00001f));
        for (int i = 0; i < indices.Count; i += 3)
        {
            Vector3 center = (vertices[indices[i]] + vertices[indices[i + 1]] + vertices[indices[i + 2]]) / 3f;
            Assert.IsTrue(center.x <= Width || center.x >= 2f - Width || center.z <= Width || center.z >= 1f - Width);
        }
    }

    [Test]
    public void HoleRetainsFourInnerEdgesAndFourConcaveJoins()
    {
        var footprint = new HashSet<Vector2Int>();
        for (int x = 0; x < 3; x++)
            for (int z = 0; z < 3; z++)
                if (x != 1 || z != 1) footprint.Add(new Vector2Int(x, z));
        Build(footprint);
        Assert.AreEqual(16, CornerVertexCount());
        Assert.That(Area(), Is.EqualTo(16f * Width).Within(0.00001f));
        foreach (Vector3 vertex in vertices)
            Assert.IsFalse(vertex.x > 1f && vertex.x < 2f && vertex.z > 1f && vertex.z < 2f,
                "The hole remains outside the highlighted footprint.");
    }

    [Test]
    public void SeparateTouchingSourcesKeepTheirOwnPerimeters()
    {
        Build(new HashSet<Vector2Int> { Vector2Int.zero });
        Build(new HashSet<Vector2Int> { Vector2Int.right });
        Assert.That(Area(), Is.EqualTo(8f * Width - 8f * Width * Width).Within(0.00001f));
    }

    [Test]
    public void InteriorAndMissingCellsEmitNoGeometry()
    {
        var footprint = new HashSet<Vector2Int>();
        for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++) footprint.Add(new Vector2Int(x, z));
        WorldPlacementOutlineGeometry.AppendCell(Vector2Int.zero, 4, edge, vertices, uvs, indices, footprint, corner);
        WorldPlacementOutlineGeometry.AppendCell(Vector2Int.one * 5, 4, edge, vertices, uvs, indices, footprint, corner);
        Assert.IsEmpty(vertices);
        Assert.IsEmpty(indices);
    }

    [TestCase(0)]
    [TestCase(100)]
    public void SubdivisionCountIsBoundedAndMissingSpriteIsSafe(int subdivisions)
    {
        WorldPlacementOutlineGeometry.AppendCell(Vector2Int.zero, subdivisions, null, vertices, uvs, indices);
        Assert.IsEmpty(indices);
        WorldPlacementOutlineGeometry.AppendCell(Vector2Int.zero, subdivisions, edge, vertices, uvs, indices);
        Assert.AreEqual(Mathf.Clamp(subdivisions, 1, 16) * 4 * 6, indices.Count);
        Assert.That(Area(), Is.EqualTo(4f * Width - 4f * Width * Width).Within(0.00001f));
    }

    private void Build(HashSet<Vector2Int> footprint)
    {
        foreach (Vector2Int cell in footprint)
            WorldPlacementOutlineGeometry.AppendCell(cell, 4, edge, vertices, uvs, indices, footprint, corner);
    }

    private int CornerVertexCount()
    {
        int count = 0;
        var expected = new Vector2(47.5f / 64f, 31.5f / 32f);
        foreach (Vector2 uv in uvs) if (uv == expected) count++;
        return count;
    }

    private float Area()
    {
        float area = 0f;
        for (int i = 0; i < indices.Count; i += 3)
            area += Vector3.Cross(vertices[indices[i + 1]] - vertices[indices[i]],
                vertices[indices[i + 2]] - vertices[indices[i]]).magnitude * 0.5f;
        return area;
    }
}
