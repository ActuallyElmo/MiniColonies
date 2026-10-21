using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class IntersectionControlPanelTests
{
    private GameObject canvasObject;
    private Canvas canvas;
    private RectTransform rect;
    private IntersectionControlPanel panel;
    private PlayerViewModeManager views;
    private IntersectionBuilderSession session;
    private TrafficGraphSnapshot graph;
    private readonly ControlledNodeId firstNode = new ControlledNodeId(1);
    private readonly ControlledNodeId secondNode = new ControlledNodeId(2);

    [SetUp]
    public void SetUp()
    {
        canvasObject = new GameObject("Intersection panel test canvas", typeof(RectTransform), typeof(Canvas));
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var panelObject = new GameObject("Intersection panel", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(canvasObject.transform, false);
        panelObject.SetActive(false);
        rect = panelObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(60f, 40f);
        panel = panelObject.AddComponent<IntersectionControlPanel>();
        SetField(panel, "panelRect", rect);
        SetField(panel, "overlayCanvas", canvas);

        var toolObject = new GameObject("Intersection tool test");
        toolObject.transform.SetParent(canvasObject.transform, false);
        toolObject.SetActive(false);
        views = toolObject.AddComponent<PlayerViewModeManager>();
        SetField(views, "<CurrentViewMode>k__BackingField", PlayerViewMode.Traffic);
        var builder = toolObject.AddComponent<IntersectionBuilderTool>();
        graph = new TrafficGraphSnapshot(default, 0, 0,
            Array.Empty<RoadSectionRecord>(), Array.Empty<LaneRecord>(), Array.Empty<MovementRecord>(),
            new[] { Node(firstNode, Vector2Int.zero), Node(secondNode, Vector2Int.one) },
            Array.Empty<MovementOwnerRecord>(), Array.Empty<LaneTraversalSegmentRecord>(),
            Array.Empty<TrafficGeometryRecord>(), Array.Empty<BuildingPortAnchorRecord>(),
            Array.Empty<MovementRejectionRecord>());
        SetField(builder, "graph", graph);
        SetField(builder, "views", views);
        session = (IntersectionBuilderSession)GetField(builder, "session");
        Assert.IsTrue(session.Focus(graph, firstNode));
        panel.Initialize(views, builder, IntersectionBuilderStyle.Default);
        Invoke("OnEnable");
        Canvas.ForceUpdateCanvases();
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(canvasObject);

    [TestCase(1f, 1f)]
    [TestCase(2f, 1.35f)]
    public void BackgroundDragPreservesGrabOffsetWithCanvasAndPanelScaling(float canvasScale, float panelScale)
    {
        canvas.scaleFactor = canvasScale;
        rect.localScale = Vector3.one * panelScale;
        Canvas.ForceUpdateCanvases();
        Vector2 start = ScreenPivot();
        var pointer = Pointer(rect.gameObject, start + new Vector2(-10f, 5f));
        Dispatch(pointer, ExecuteEvents.beginDragHandler);
        Vector2 delta = new Vector2(-20f, 10f);
        pointer.position += delta;
        Dispatch(pointer, ExecuteEvents.dragHandler);
        Assert.That(Vector2.Distance(start + delta, ScreenPivot()), Is.LessThan(0.01f));

        Dispatch(pointer, ExecuteEvents.endDragHandler);
        pointer.position += delta;
        Dispatch(pointer, ExecuteEvents.dragHandler);
        Assert.That(Vector2.Distance(start + delta, ScreenPivot()), Is.LessThan(0.01f));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DragStartingOnAChildControlCannotMovePanel(bool slider)
    {
        var child = new GameObject("Control", typeof(RectTransform), typeof(Image));
        child.transform.SetParent(rect, false);
        if (slider) child.AddComponent<Slider>();
        else child.AddComponent<Button>();
        Vector2 start = rect.anchoredPosition;
        var pointer = Pointer(child, ScreenPivot());
        Dispatch(pointer, ExecuteEvents.beginDragHandler);
        pointer.position += new Vector2(-20f, 10f);
        Dispatch(pointer, ExecuteEvents.dragHandler);
        Assert.AreEqual(start, rect.anchoredPosition);
    }

    [TestCase(PointerEventData.InputButton.Right)]
    [TestCase(PointerEventData.InputButton.Middle)]
    public void NonLeftButtonCannotDragPanel(PointerEventData.InputButton button)
    {
        Vector2 start = rect.anchoredPosition;
        var pointer = Pointer(rect.gameObject, ScreenPivot());
        pointer.button = button;
        Dispatch(pointer, ExecuteEvents.beginDragHandler);
        pointer.position += new Vector2(-20f, 10f);
        Dispatch(pointer, ExecuteEvents.dragHandler);
        Assert.AreEqual(start, rect.anchoredPosition);
    }

    [Test]
    public void RefreshKeepsDraggedPositionAndReopeningRestoresConfiguredDefault()
    {
        SetField(panel, "rightScreenMargin", 35f);
        panel.ApplyScreenAnchor();
        var pointer = Pointer(rect.gameObject, ScreenPivot());
        Dispatch(pointer, ExecuteEvents.beginDragHandler);
        pointer.position += new Vector2(-20f, 10f);
        Dispatch(pointer, ExecuteEvents.dragHandler);
        Vector2 dragged = rect.anchoredPosition;
        Invoke("RefreshPanel");
        Assert.AreEqual(dragged, rect.anchoredPosition);

        session.Reset();
        Invoke("RefreshPanel");
        Assert.IsFalse(panel.IsVisible);
        Assert.IsTrue(session.Focus(graph, firstNode));
        Invoke("RefreshPanel");
        Assert.IsTrue(panel.IsVisible);
        Assert.AreEqual(new Vector2(-35f, 0f), rect.anchoredPosition);
        pointer.position += new Vector2(-20f, 10f);
        Dispatch(pointer, ExecuteEvents.dragHandler);
        Assert.AreEqual(new Vector2(-35f, 0f), rect.anchoredPosition,
            "An interrupted drag must not resume when the panel reopens.");
    }

    [Test]
    public void OpeningAnotherIntersectionRestoresDefaultPosition()
    {
        rect.anchoredPosition += new Vector2(-20f, 10f);
        Assert.IsTrue(session.Focus(graph, secondNode));
        Invoke("RefreshPanel");
        Assert.AreEqual(new Vector2(-20f, 0f), rect.anchoredPosition);
    }

    [Test]
    public void DisableCancelsDragAndEnableRestoresDefaultPosition()
    {
        var pointer = Pointer(rect.gameObject, ScreenPivot());
        Dispatch(pointer, ExecuteEvents.beginDragHandler);
        pointer.position += new Vector2(-20f, 10f);
        Dispatch(pointer, ExecuteEvents.dragHandler);
        Invoke("OnDisable");
        Invoke("OnEnable");
        pointer.position += new Vector2(-20f, 10f);
        Dispatch(pointer, ExecuteEvents.dragHandler);
        Assert.AreEqual(new Vector2(-20f, 0f), rect.anchoredPosition);
    }

    [Test]
    public void DragClampsCardInsideScreenEdges()
    {
        var pointer = Pointer(rect.gameObject, ScreenPivot());
        Dispatch(pointer, ExecuteEvents.beginDragHandler);
        foreach (Vector2 position in new[] { new Vector2(-10000f, -10000f), new Vector2(10000f, 10000f) })
        {
            pointer.position = position;
            Dispatch(pointer, ExecuteEvents.dragHandler);
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, corner);
                Assert.That(screen.x, Is.InRange(7.99f, Screen.width - 7.99f));
                Assert.That(screen.y, Is.InRange(7.99f, Screen.height - 7.99f));
            }
        }
    }

    private static ControlledNodeRecord Node(ControlledNodeId id, Vector2Int cell) =>
        new ControlledNodeRecord(id, cell, RoadNodeKind.Intersection, IntersectionRuleType.FreeForAll,
            0, 0, 30f, Array.Empty<LaneId>(), Array.Empty<LaneId>(), Array.Empty<MovementId>());

    private Vector2 ScreenPivot() => RectTransformUtility.WorldToScreenPoint(null, rect.position);

    private static PointerEventData Pointer(GameObject target, Vector2 position) =>
        new PointerEventData(null)
        {
            button = PointerEventData.InputButton.Left,
            position = position,
            pointerPressRaycast = new RaycastResult { gameObject = target }
        };

    private void Dispatch<T>(PointerEventData pointer, ExecuteEvents.EventFunction<T> handler)
        where T : IEventSystemHandler => ExecuteEvents.Execute(rect.gameObject, pointer, handler);

    private void Invoke(string method) => typeof(IntersectionControlPanel)
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, null);

    private static object GetField(object target, string name) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

    private static void SetField(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
}
