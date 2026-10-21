using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class WorldPlacementHighlightPlayModeTests
{
    // Runtime code uses Assembly-CSharp, which cannot be referenced by this asmdef.
    private static Type GameType(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static object Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method).Invoke(target, args);
    private GameObject root;
    private Material material;
    private Sprite edge;
    private Texture2D texture;
    private float previousAnimationTime;

    [SetUp]
    public void SetUp()
    {
        previousAnimationTime = Shader.GetGlobalFloat("_WorldPlacementHighlightTime");
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (root != null) Object.Destroy(root);
        if (material != null) Object.Destroy(material);
        if (edge != null) Object.Destroy(edge);
        if (texture != null) Object.Destroy(texture);
        yield return null;
        Shader.SetGlobalFloat("_WorldPlacementHighlightTime", previousAnimationTime);
    }

    [UnityTest]
    public IEnumerator PendingBuildsCancelAndPublishedMeshesReleaseOnReplaceRegenerationAndDisable()
    {
        root = new GameObject("World placement lifecycle test");
        var highlight = (Behaviour)root.AddComponent(GameType("WorldPlacementHighlight"));
        Component world = root.GetComponent(GameType("WorldManager"));
        texture = new Texture2D(16, 16);
        edge = Sprite.Create(texture, new Rect(0, 0, 16, 16), Vector2.one * 0.5f);
        Shader shader = Shader.Find("MiniColonies/MapResourceHighlight");
        Assert.IsNotNull(shader);
        material = new Material(shader);
        void Show(Vector2Int cell) => Call(highlight, "Show", new[] { cell }, material, edge, null, Color.white, float.NaN);

        Show(Vector2Int.zero);
        Call(highlight, "Clear");
        for (int i = 0; i < 3; i++) yield return null;
        Assert.IsEmpty(root.GetComponentsInChildren<MeshFilter>(true), "Cancelled work must not publish later.");

        Show(Vector2Int.zero);
        for (int i = 0; i < 3; i++) yield return null;
        Mesh first = root.GetComponentInChildren<MeshFilter>().sharedMesh;
        Assert.That(first.vertices[0].y, Is.EqualTo(0.025f).Within(0.00001f), "Invalid offsets use the safe default.");
        yield return null;
        Assert.AreSame(first, root.GetComponentInChildren<MeshFilter>().sharedMesh);

        Show(Vector2Int.right);
        Assert.IsEmpty(root.GetComponentsInChildren<MeshFilter>(), "Old feedback hides immediately.");
        for (int i = 0; i < 3; i++) yield return null;
        Assert.IsTrue(first == null, "Replacing a highlight must destroy its owned mesh.");
        Mesh second = root.GetComponentInChildren<MeshFilter>().sharedMesh;
        Call(world, "ClearWorld");
        yield return null;
        Assert.IsTrue(second == null);
        Assert.IsEmpty(root.GetComponentsInChildren<MeshFilter>(true));

        Show(Vector2Int.zero);
        for (int i = 0; i < 3; i++) yield return null;
        Mesh third = root.GetComponentInChildren<MeshFilter>().sharedMesh;
        highlight.enabled = false;
        yield return null;
        Assert.IsTrue(third == null);
        Assert.IsEmpty(root.GetComponentsInChildren<MeshFilter>(true));
    }

    [UnityTest]
    public IEnumerator AnimationUsesRealTimeAtEverySimulationSpeedWithoutRebuildingMeshes()
    {
        root = new GameObject("World placement animation test");
        Component clock = root.AddComponent(GameType("GameTimeManager"));
        var highlight = (Behaviour)root.AddComponent(GameType("WorldPlacementHighlight"));
        Component world = root.GetComponent(GameType("WorldManager"));
        world.GetType().GetField("cellSize").SetValue(world, 2f);
        texture = new Texture2D(16, 16);
        edge = Sprite.Create(texture, new Rect(0, 0, 16, 16), Vector2.one * 0.5f);
        material = new Material(Shader.Find("MiniColonies/MapResourceHighlight"));
        Call(highlight, "Show", new[] { Vector2Int.zero, Vector2Int.right }, material, edge, null, Color.white, 0.025f);
        for (int i = 0; i < 3; i++) yield return null;
        Mesh mesh = root.GetComponentInChildren<MeshFilter>().sharedMesh;
        var properties = new MaterialPropertyBlock();
        root.GetComponentInChildren<MeshRenderer>().GetPropertyBlock(properties);
        Assert.AreEqual(8f, properties.GetFloat("_PixelsPerWorldUnit"),
            "A 16-pixel edge spanning a two-unit cell has eight pattern pixels per world unit.");

        foreach (float speed in new[] { 0f, 0.5f, 1f, 2f, 4f })
        {
            clock.GetType().GetProperty("SimulationSpeed").SetValue(clock, speed);
            float before = Time.unscaledTime;
            // No wall-clock waits: advance frames and compare against their engine time.
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(Shader.GetGlobalFloat("_WorldPlacementHighlightTime"),
                Is.InRange(before, Time.unscaledTime), "The animation clock follows real time at every speed, including pause.");
            Assert.AreSame(mesh, root.GetComponentInChildren<MeshFilter>().sharedMesh,
                "Animation and speed changes must reuse the existing outline geometry.");
        }
    }

    [UnityTest]
    public IEnumerator PixelDensityChangesKeepTheOriginalPhysicalOutlineWidth()
    {
        root = new GameObject("World placement density test");
        var highlight = (Behaviour)root.AddComponent(GameType("WorldPlacementHighlight"));
        texture = new Texture2D(16, 16);
        edge = Sprite.Create(texture, new Rect(0, 0, 16, 16), Vector2.one * 0.5f);
        material = new Material(Shader.Find("MiniColonies/MapResourceHighlight"));
        void Show() => Call(highlight, "Show", new[] { Vector2Int.zero }, material, edge, null, Color.white, 0.025f);

        material.SetFloat("_OutlineWidthPixels", 1f);
        Show();
        for (int i = 0; i < 3; i++) yield return null;
        Mesh first = root.GetComponentInChildren<MeshFilter>().sharedMesh;
        Vector3[] originalVertices = first.vertices;
        material.SetFloat("_OutlineWidthPixels", 4f);
        yield return null;
        Assert.AreSame(first, root.GetComponentInChildren<MeshFilter>().sharedMesh,
            "Live density edits must reuse the mesh.");
        Show();
        for (int i = 0; i < 3; i++) yield return null;
        CollectionAssert.AreEqual(originalVertices, root.GetComponentInChildren<MeshFilter>().sharedMesh.vertices,
            "Rebuilding at a different density must preserve the exact physical perimeter.");
    }

    [UnityTest]
    public IEnumerator SolidSelectionAndAnimatedHoverCoexistAndClearIndependently()
    {
        root = new GameObject("Independent placement states test");
        var highlight = (Behaviour)root.AddComponent(GameType("WorldPlacementHighlight"));
        CreateAppearance();
        void ShowHover() => Call(highlight, "Show", new[] { Vector2Int.right * 3 }, material, edge, null, Color.white, 0.025f);
        void ShowSelection() => Call(highlight, "ShowSelection", new[] { Vector2Int.zero }, material, edge, null, Color.white, 0.025f);
        ShowSelection();
        ShowHover();
        for (int i = 0; i < 3; i++) yield return null;
        Assert.AreEqual(2, root.GetComponentsInChildren<MeshFilter>().Length);
        Mesh selectionMesh = StateRenderer(true).GetComponent<MeshFilter>().sharedMesh;
        Assert.AreSame(material, StateRenderer(true).sharedMaterial);
        Assert.AreSame(material, StateRenderer(false).sharedMaterial);
        Assert.AreEqual(0f, material.GetFloat("_SolidOutline"), "State must never mutate the shared material.");

        Call(highlight, "Clear");
        Assert.AreEqual(1, root.GetComponentsInChildren<MeshFilter>().Length);
        yield return null;
        Assert.AreSame(selectionMesh, StateRenderer(true).GetComponent<MeshFilter>().sharedMesh);
        ShowHover();
        for (int i = 0; i < 3; i++) yield return null;
        Mesh hoverMesh = StateRenderer(false).GetComponent<MeshFilter>().sharedMesh;
        Call(highlight, "ClearSelection");
        yield return null;
        Assert.IsTrue(selectionMesh == null);
        Assert.AreSame(hoverMesh, StateRenderer(false).GetComponent<MeshFilter>().sharedMesh);

        ShowSelection();
        for (int i = 0; i < 3; i++) yield return null;
        Call(root.GetComponent(GameType("WorldManager")), "ClearWorld");
        yield return null;
        Assert.IsEmpty(root.GetComponentsInChildren<MeshFilter>(true));
        ShowSelection();
        ShowHover();
        highlight.enabled = false;
        for (int i = 0; i < 3; i++) yield return null;
        Assert.IsEmpty(root.GetComponentsInChildren<MeshFilter>(true), "Disabling cancels pending work in both states.");
    }

    [UnityTest]
    public IEnumerator BuildingSelectionFollowsItsOwnerAndClearsOnDemolitionAndTools()
    {
        root = new GameObject("Building selection outline test");
        Component backend = root.AddComponent(GameType("BuildingSystemBackend"));
        Component actions = root.AddComponent(GameType("PlayerActionManager"));
        var presenter = (Behaviour)root.AddComponent(GameType("WorldPlacementHover"));
        CreateAppearance();
        presenter.GetType().GetField("highlightMaterial").SetValue(presenter, material);
        presenter.GetType().GetField("outlineSprite").SetValue(presenter, edge);
        Component first = CreateBuilding(backend, Vector2Int.zero);
        Component second = CreateBuilding(backend, Vector2Int.right * 3);
        Call(actions, "SelectBuilding", first);
        for (int i = 0; i < 3; i++) yield return null;
        // Stop physical mouse input; public selection and backend notifications remain active.
        ((Behaviour)actions).enabled = false;
        Assert.AreEqual(1, root.GetComponentsInChildren<MeshFilter>().Length,
            "A selection remains visible even when there is no world hover.");
        Mesh firstMesh = StateRenderer(true).GetComponent<MeshFilter>().sharedMesh;
        Call(actions, "SelectBuilding", first);
        yield return null;
        Assert.AreSame(firstMesh, StateRenderer(true).GetComponent<MeshFilter>().sharedMesh,
            "Repeated selection must not rebuild an unchanged perimeter.");

        Call(actions, "SelectBuilding", second);
        for (int i = 0; i < 3; i++) yield return null;
        Assert.IsTrue(firstMesh == null);
        Assert.AreEqual(1, root.GetComponentsInChildren<MeshFilter>().Length);
        ((Behaviour)second).enabled = false;
        for (int i = 0; i < 3; i++) yield return null;
        Assert.IsEmpty(root.GetComponentsInChildren<MeshFilter>());
        ((Behaviour)second).enabled = true;
        for (int i = 0; i < 3; i++) yield return null;
        Assert.IsNotNull(StateRenderer(true));
        presenter.enabled = false;
        yield return null;
        Assert.IsEmpty(root.GetComponentsInChildren<MeshFilter>(true));
        presenter.enabled = true;
        for (int i = 0; i < 3; i++) yield return null;
        Assert.IsNotNull(StateRenderer(true));

        Assert.IsTrue((bool)Call(backend, "TryDemolishBuilding", Vector2Int.right * 3));
        Assert.IsNull(actions.GetType().GetProperty("SelectedBuilding").GetValue(actions));
        Assert.IsEmpty(root.GetComponentsInChildren<MeshFilter>());
        yield return null;
        Call(actions, "SelectBuilding", first);
        for (int i = 0; i < 3; i++) yield return null;
        Call(actions, "SetModeBuildRoad");
        Assert.IsNull(actions.GetType().GetProperty("SelectedBuilding").GetValue(actions));
        Assert.IsEmpty(root.GetComponentsInChildren<MeshFilter>());
        Call(actions, "SetModeNone");
        Call(actions, "SelectBuilding", first);
        Call(actions, "SelectBuilding", new object[] { null });
        for (int i = 0; i < 3; i++) yield return null;
        Assert.IsEmpty(root.GetComponentsInChildren<MeshFilter>(true), "Deselecting cancels a pending solid build.");
    }

    [UnityTest]
    public IEnumerator RangeChannelsAnimateIndependentlyAndCancelWithoutAffectingSelection()
    {
        root = new GameObject("Range outline channels test");
        var highlight = (Behaviour)root.AddComponent(GameType("WorldPlacementHighlight"));
        CreateAppearance();
        object first = new object();
        object second = new object();
        Call(highlight, "ShowRange", first, new[] { new Vector2Int(4, 4) }, material, edge, null, Color.cyan, 0.04f);
        Call(highlight, "ShowRange", second, new[] { new Vector2Int(8, 8) }, material, edge, null, Color.red, 0.04f);
        Call(highlight, "ShowSelection", new[] { Vector2Int.zero }, material, edge, null, Color.white, 0.025f);
        for (int i = 0; i < 4; i++) yield return null;
        Assert.AreEqual(3, root.GetComponentsInChildren<MeshFilter>().Length);
        var properties = new MaterialPropertyBlock();
        int animated = 0;
        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>())
        {
            renderer.GetPropertyBlock(properties);
            if (properties.GetFloat("_SolidOutline") == 0f) animated++;
        }
        Assert.AreEqual(2, animated, "Both ranges use the existing dotted shader mode.");
        Call(highlight, "ClearSelection");
        Assert.AreEqual(2, root.GetComponentsInChildren<MeshFilter>().Length);
        float before = Time.unscaledTime;
        for (int i = 0; i < 2; i++) yield return null;
        Assert.That(Shader.GetGlobalFloat("_WorldPlacementHighlightTime"), Is.InRange(before, Time.unscaledTime),
            "Range-only feedback must advance even when there is no hover outline.");
        Call(highlight, "ClearRange", first);
        Assert.AreEqual(1, root.GetComponentsInChildren<MeshFilter>().Length);
        Call(highlight, "ClearAll");
        for (int i = 0; i < 2; i++) yield return null;
        Assert.IsEmpty(root.GetComponentsInChildren<MeshFilter>(true));
        Call(highlight, "ShowRange", first, new[] { Vector2Int.one }, material, edge, null, Color.cyan, 0.04f);
        Call(highlight, "ClearRange", first);
        for (int i = 0; i < 3; i++) yield return null;
        Assert.IsEmpty(root.GetComponentsInChildren<MeshFilter>(true), "Cancelled pending ranges cannot publish later.");
    }

    [UnityTest]
    public IEnumerator RangeDisplayKeepsTheSameVisibleColorWhenARequirementBecomesSatisfied()
    {
        root = new GameObject("Range requirement color regression");
        var highlight = (Behaviour)root.AddComponent(GameType("WorldPlacementHighlight"));
        var world = (Behaviour)root.GetComponent(GameType("WorldManager"));
        // Test explicit terrain without letting Start generate a scene/world.
        world.enabled = false;
        world.GetType().GetField("worldSize").SetValue(world, 16);
        world.GetType().GetField("makeCircular").SetValue(world, false);
        var layer = Activator.CreateInstance(GameType("TerrainLayer"));
        layer.GetType().GetField("surfaceType").SetValue(layer, Enum.Parse(GameType("TerrainSurfaceType"), "FlatGround"));
        ((IList)world.GetType().GetField("layers").GetValue(world)).Add(layer);
        Component backend = root.AddComponent(GameType("BuildingSystemBackend"));
        CreateAppearance();
        var data = ScriptableObject.CreateInstance(GameType("BuildingData"));
        object display = Activator.CreateInstance(GameType("BuildingRangeDisplay"));
        try
        {
            var footprint = (IList)data.GetType().GetField("footprint").GetValue(data);
            footprint.Add(Activator.CreateInstance(GameType("BuildingTile")));
            object range = Activator.CreateInstance(GameType("BuildingRange"));
            range.GetType().GetField("color").SetValue(range, default(Color));
            ((IList)data.GetType().GetField("ranges").GetValue(data)).Add(range);
            var required = (IList)range.GetType().GetField("requiredTileTypes").GetValue(range);
            foreach (bool matched in new[] { false, true })
            {
                required.Clear();
                object tile = Activator.CreateInstance(GameType("WorldTileType"));
                tile.GetType().GetField("kind").SetValue(tile,
                    Enum.Parse(GameType("WorldTileKind"), matched ? "FlatGround" : "WaterLevel"));
                required.Add(tile);
                object work = Call(backend, "RequestPlacementRanges", world, data, new Vector2Int(4, 4), footprint);
                Assert.IsTrue((bool)Call(work, "Advance", 64));
                Assert.AreEqual(matched, work.GetType().GetProperty("AllRequirementsMet").GetValue(work));
                Call(display, "Show", work, highlight, material, edge, null, 0.04f);
                for (int i = 0; i < 4; i++) yield return null;
                MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>();
                Assert.IsNotEmpty(renderers, "Finding the required tile must not hide the range.");
                var block = new MaterialPropertyBlock();
                foreach (MeshRenderer renderer in renderers)
                {
                    renderer.GetPropertyBlock(block);
                    Assert.AreEqual(new Color(0.2f, 0.85f, 1f, 1f), block.GetColor("_Color"),
                        "Both states use the normal visible color, never a forced invalid red.");
                }
            }
        }
        finally
        {
            Call(display, "Clear");
            Object.Destroy(data);
        }
    }

    private void CreateAppearance()
    {
        texture = new Texture2D(16, 16);
        edge = Sprite.Create(texture, new Rect(0, 0, 16, 16), Vector2.one * 0.5f);
        material = new Material(Shader.Find("MiniColonies/MapResourceHighlight"));
    }

    private MeshRenderer StateRenderer(bool solid)
    {
        var properties = new MaterialPropertyBlock();
        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>())
        {
            renderer.GetPropertyBlock(properties);
            if ((properties.GetFloat("_SolidOutline") > 0.5f) == solid) return renderer;
        }
        Assert.Fail("Expected an outline for " + (solid ? "selection" : "hover"));
        return null;
    }

    private Component CreateBuilding(Component backend, Vector2Int origin)
    {
        var instance = new GameObject("Selection test building");
        instance.transform.SetParent(root.transform);
        Component building = instance.AddComponent(GameType("Building"));
        building.GetType().GetField("originCell").SetValue(building, origin);
        var occupied = (System.Collections.Generic.ICollection<Vector2Int>)building.GetType().GetField("occupiedCells").GetValue(building);
        occupied.Add(origin);
        Type tileType = GameType("BuildingTile");
        var footprint = (IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(tileType));
        footprint.Add(Activator.CreateInstance(tileType));
        Call(backend, "RegisterBuilding", building, footprint);
        return building;
    }
}
