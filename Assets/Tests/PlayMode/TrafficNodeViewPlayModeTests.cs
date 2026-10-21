using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class TrafficNodeViewPlayModeTests
{
    // The game uses the predefined Assembly-CSharp assembly, which cannot be
    // referenced by an asmdef. Resolve its types only at this integration boundary.
    private static Type GameType(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static object Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method).Invoke(target, args);
    private static object Read(object target, string property) =>
        target.GetType().GetProperty(property).GetValue(target);
    private GameObject root;
    private Scene loadedScene;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (root != null) Object.Destroy(root);
        if (loadedScene.IsValid() && loadedScene.isLoaded)
            yield return SceneManager.UnloadSceneAsync(loadedScene);
        yield return null;
    }

    [UnityTest]
    public IEnumerator OverlayBuildsOnceRefreshesExplicitlyAndReleasesOnExit()
    {
        root = new GameObject("Traffic node lifecycle test");
        var backend = root.AddComponent(GameType("TrafficSystemBackend"));
        var views = root.AddComponent(GameType("PlayerViewModeManager"));
        SetSnapshot(backend, MakeSnapshot(600));
        yield return null;
        Call(views, "SetTrafficView");
        Call(views, "SetTrafficNodeView", true);
        for (int i = 0; i < 12; i++) yield return null;
        var overlay = (Behaviour)root.GetComponent(GameType("TrafficNodeViewOverlay"));
        var filters = root.GetComponentsInChildren<MeshFilter>();
        Assert.Greater(filters.Length, 1, "Large lane geometry should be split into bounded chunks.");
        Mesh firstMesh = filters[0].sharedMesh;
        yield return null;
        yield return null;
        Assert.AreSame(firstMesh, root.GetComponentInChildren<MeshFilter>().sharedMesh,
            "An unchanged overlay must keep its mesh across frames.");
        var style = GameType("TrafficNodeViewStyle").GetProperty("Default").GetValue(null);
        style.GetType().GetField("lineColor").SetValue(style, Color.red);
        style.GetType().GetField("showArrows").SetValue(style, false);
        Call(overlay, "ApplyAppearance", style);
        for (int i = 0; i < 12; i++) yield return null;
        Assert.IsTrue(firstMesh == null, "Rebuilding must release old meshes.");
        firstMesh = root.GetComponentInChildren<MeshFilter>().sharedMesh;
        Assert.AreEqual(Color.red, firstMesh.colors[0]);
        SetSnapshot(backend, MakeSnapshot(4));
        var published = (Action)GameType("TrafficSystemBackend")
            .GetField("OnTrafficNetworkReady", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(backend);
        published?.Invoke();
        yield return null;
        Assert.IsTrue(firstMesh == null);
        Assert.AreEqual(1, root.GetComponentsInChildren<MeshFilter>().Length);
        Call(views, "SetNormalView");
        Assert.AreEqual(0, root.GetComponentsInChildren<MeshFilter>().Length);
        yield return null;
        Assert.AreEqual(0, root.GetComponentsInChildren<MeshFilter>(true).Length);
        Call(views, "SetTrafficView");
        Call(views, "SetTrafficNodeView", true);
        yield return null;
        overlay.enabled = false;
        yield return null;
        Assert.AreEqual(0, root.GetComponentsInChildren<MeshFilter>(true).Length);
        overlay.enabled = true;
        yield return null;
        Assert.AreEqual(1, root.GetComponentsInChildren<MeshFilter>().Length);
        style.GetType().GetField("showArrows").SetValue(style, true);
        style.GetType().GetField("arrowSpacing").SetValue(style, 1f);
        Call(overlay, "ApplyAppearance", style);
        yield return null;
        Assert.AreEqual(33, root.GetComponentInChildren<MeshFilter>().sharedMesh.vertexCount,
            "Three line segments and three evenly spaced arrows should be generated.");
        style.GetType().GetField("arrowSpacing").SetValue(style, 2f);
        Call(overlay, "ApplyAppearance", style);
        yield return null;
        Assert.AreEqual(30, root.GetComponentInChildren<MeshFilter>().sharedMesh.vertexCount);
        SetSnapshot(backend, MakeSnapshot(600));
        published?.Invoke();
        Call(views, "SetNormalView");
        for (int i = 0; i < 12; i++) yield return null;
        Assert.AreEqual(0, root.GetComponentsInChildren<MeshFilter>(true).Length,
            "Leaving traffic during a rebuild must cancel all pending geometry.");
    }

    [UnityTest]
    public IEnumerator SampleSceneTrafficButtonCreatesAndControlsNodeOption()
    {
        yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Additive);
        loadedScene = SceneManager.GetSceneByName("SampleScene");
        yield return null;
        var ui = Object.FindFirstObjectByType(GameType("PlayerViewModeUI"));
        Assert.IsNotNull(ui);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var trafficButton = (Button)ui.GetType().GetField("trafficButton", flags).GetValue(ui);
        var nodeButton = (Button)ui.GetType().GetField("trafficNodeButton", flags).GetValue(ui);
        Assert.IsNotNull(nodeButton);
        Assert.IsFalse(nodeButton.gameObject.activeSelf);
        trafficButton.onClick.Invoke();
        Assert.IsTrue(nodeButton.gameObject.activeInHierarchy);
        nodeButton.onClick.Invoke();
        var views = GameType("PlayerViewModeManager").GetProperty("Instance").GetValue(null);
        Assert.IsTrue((bool)Read(views, "TrafficNodeViewEnabled"));
        nodeButton.onClick.Invoke();
        Assert.IsFalse((bool)Read(views, "TrafficNodeViewEnabled"));
        Call(views, "SetNormalView");
        Assert.IsFalse(nodeButton.gameObject.activeSelf);
        yield return null;
    }

    private static void SetSnapshot(Component backend, object snapshot) =>
        backend.GetType().GetProperty("CurrentTrafficGraphSnapshot").SetValue(backend, snapshot);

    private static object MakeSnapshot(int samples)
    {
        var points = new Vector3[samples];
        for (int i = 0; i < samples; i++) points[i] = Vector3.forward * i;
        object Create(string name, params object[] args) => Activator.CreateInstance(GameType(name),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, args, null);
        var geometryId = Create("TrafficGeometryId", 1UL);
        var lane = Create("LaneRecord", Create("LaneId", 1UL), Create("RoadSectionId", 1UL),
            Enum.Parse(GameType("TrafficLaneFlowDirection"), "SectionStartToEnd"),
            0, 1, Vector2Int.zero, Vector2Int.up, 1, 2,
            Enum.Parse(GameType("RoadPermissionMask"), "GeneralRoad"),
            Enum.Parse(GameType("VehicleCapabilityMask"), "GroundRoad"), 1f, geometryId, null, null);
        var lanes = Array.CreateInstance(GameType("LaneRecord"), 1);
        lanes.SetValue(lane, 0);
        var geometry = Array.CreateInstance(GameType("TrafficGeometryRecord"), 1);
        geometry.SetValue(Create("TrafficGeometryRecord", geometryId, points), 0);
        return Create("TrafficGraphSnapshot", Create("TrafficGraphVersion", 1), 0, 0,
            null, lanes, null, null, null, null, geometry, null, null);
    }
}
