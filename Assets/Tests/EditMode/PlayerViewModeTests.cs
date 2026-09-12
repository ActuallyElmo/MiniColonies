using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public sealed class PlayerViewModeTests
{
    private GameObject managers;
    private PlayerViewModeManager views;
    private PlayerActionManager actions;

    [SetUp]
    public void SetUp()
    {
        managers = new GameObject("View Mode Tests");
        actions = managers.AddComponent<PlayerActionManager>();
        views = managers.AddComponent<PlayerViewModeManager>();
        // Edit-mode tests do not run MonoBehaviour's play-mode lifecycle.
        InvokeLifecycle(actions, "Awake");
        InvokeLifecycle(views, "Awake");
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(managers);

    [Test]
    public void StartsInNormalViewWithExistingToolsAvailable()
    {
        Assert.AreEqual(PlayerViewMode.Normal, views.CurrentViewMode);
        Assert.IsTrue(PlayerViewModeManager.AllowsGameplayActions);
        actions.SetModePlaceBuilding();
        Assert.AreEqual(PlayerMode.PlaceBuilding, actions.currentMode);
    }

    [TestCase(PlayerViewMode.Overview)]
    [TestCase(PlayerViewMode.Traffic)]
    public void RestrictedViewsCancelToolsAndBlockEveryToolEntryPoint(PlayerViewMode mode)
    {
        actions.SetModeBuildRoad();
        bool selectionCleared = false;
        actions.BuildingSelectionChanged += building => selectionCleared = building == null;
        views.SetViewMode(mode);

        Assert.IsTrue(selectionCleared);
        Assert.IsFalse(PlayerViewModeManager.AllowsGameplayActions);
        Assert.AreEqual(PlayerMode.None, actions.currentMode);
        Assert.AreEqual(0f, Shader.GetGlobalFloat("_ShowColoredGrid"));
        foreach (System.Action selectTool in new System.Action[] {
                     actions.SetModeBuildRoad, actions.SetModeUpgradeRoad,
                     actions.SetModeDelete, actions.SetModePlaceBuilding,
                     actions.SetModeDemolishBuilding })
        {
            selectTool();
            Assert.AreEqual(PlayerMode.None, actions.currentMode);
        }

        views.SetNormalView();
        Assert.AreEqual(PlayerMode.None, actions.currentMode);
        actions.SetModePlaceBuilding();
        Assert.AreEqual(PlayerMode.PlaceBuilding, actions.currentMode);
    }

    [Test]
    public void ViewChangeCancelsAnInProgressRoadDragBeforeNotifyingListeners()
    {
        var roads = managers.AddComponent<RoadBuilderInput>();
        InvokeLifecycle(roads, "Awake");
        var backend = managers.AddComponent<RoadSystemBackend>();
        if (RoadSystemBackend.Instance != backend) InvokeLifecycle(backend, "Awake");
        backend.BeginPreview();
        SetField(roads, "_isDragging", true);
        SetField(roads, "_madeChangesDuringDrag", true);
        views.ViewModeChanged += _ => {
            Assert.IsFalse(roads.TryCancelActivePlacement());
            Assert.IsFalse(PlayerViewModeManager.AllowsGameplayActions);
            Assert.IsFalse(backend.IsPreviewMode);
        };
        views.SetOverviewView();
        Assert.IsFalse((bool)GetField(roads, "_madeChangesDuringDrag"));
    }

    [Test]
    public void SwitchingRestrictedViewsPreservesUIVisibilityAndKeepsViewButtonsAvailable()
    {
        var uiRoot = new GameObject("Test Canvas", typeof(RectTransform));
        uiRoot.transform.SetParent(managers.transform);
        uiRoot.SetActive(false);
        var ui = uiRoot.AddComponent<PlayerViewModeUI>();
        var normalRoot = new GameObject("Normal UI");
        normalRoot.transform.SetParent(uiRoot.transform);
        var inactiveRoot = new GameObject("Already Hidden UI");
        inactiveRoot.transform.SetParent(uiRoot.transform);
        inactiveRoot.SetActive(false);
        var buttonObject = new GameObject("Persistent Camera Controls", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(uiRoot.transform);
        SetField(ui, "viewModeManager", views);
        SetField(ui, "normalUIRoots", new[] { normalRoot, inactiveRoot });
        uiRoot.SetActive(true);
        InvokeLifecycle(ui, "OnEnable");

        views.SetOverviewView();
        Assert.IsFalse(normalRoot.activeSelf);
        Assert.IsTrue(buttonObject.activeInHierarchy);
        views.SetTrafficView();
        Assert.IsFalse(normalRoot.activeSelf);
        Assert.IsTrue(buttonObject.GetComponent<Button>().IsInteractable());
        views.SetNormalView();
        Assert.IsTrue(normalRoot.activeSelf);
        Assert.IsFalse(inactiveRoot.activeSelf);
    }

    [Test]
    public void ReSelectingCurrentViewDoesNotResetTheActiveTool()
    {
        actions.SetModeBuildRoad();
        int changes = 0;
        views.ViewModeChanged += _ => changes++;
        views.SetNormalView();
        Assert.AreEqual(PlayerMode.BuildRoad, actions.currentMode);
        Assert.AreEqual(0, changes);
    }

    [Test]
    public void DisabledViewUIStillHidesExternalRootsAndRestoresThemWhenNormalReturns()
    {
        var ui = managers.AddComponent<PlayerViewModeUI>();
        var normalRoot = new GameObject("External Normal UI");
        normalRoot.transform.SetParent(managers.transform);
        SetField(ui, "viewModeManager", views);
        SetField(ui, "normalUIRoots", new[] { normalRoot });
        InvokeLifecycle(ui, "OnEnable");

        views.SetOverviewView();
        ui.enabled = false;
        InvokeLifecycle(ui, "OnDisable");
        Assert.IsFalse(normalRoot.activeSelf, "Disabling the presenter must not reveal gameplay UI.");
        views.SetTrafficView();
        Assert.IsFalse(normalRoot.activeSelf);
        views.SetNormalView();
        Assert.IsTrue(normalRoot.activeSelf, "External canvases must still follow mode changes.");

        ui.enabled = true;
        InvokeLifecycle(ui, "OnEnable");
        views.SetOverviewView();
        Assert.IsFalse(normalRoot.activeSelf);
        views.SetNormalView();
        Assert.IsTrue(normalRoot.activeSelf);
    }

    [TestCase(PlayerViewMode.Overview)]
    [TestCase(PlayerViewMode.Traffic)]
    public void ModeSwitchImmediatelyHidesGridEvenWhenItsControllerIsInactive(PlayerViewMode mode)
    {
        var controllerObject = new GameObject("Inactive Grid Controller");
        controllerObject.transform.SetParent(managers.transform);
        var grid = controllerObject.AddComponent<GridOverlayHandler>();
        grid.gridProjector = new GameObject("Visible Grid");
        grid.gridProjector.transform.SetParent(managers.transform);
        controllerObject.SetActive(false);
        views.SetViewMode(mode);
        Assert.IsFalse(grid.gridProjector.activeSelf);
    }

    [Test]
    public void InvalidViewDoesNotChangePermissionsOrActiveTool()
    {
        actions.SetModeBuildRoad();
        Assert.Throws<System.ArgumentOutOfRangeException>(() => views.SetViewMode((PlayerViewMode)100));
        Assert.AreEqual(PlayerViewMode.Normal, views.CurrentViewMode);
        Assert.AreEqual(PlayerMode.BuildRoad, actions.currentMode);
    }

    [TestCase(PlayerViewMode.Overview)]
    [TestCase(PlayerViewMode.Traffic)]
    public void RestrictedViewsClearSelectedBuildingAndPreventPanelsFromReopening(PlayerViewMode mode)
    {
        var building = managers.AddComponent<Building>();
        var overlay = managers.AddComponent<BuildingPanelOverlayManager>();
        var panelObject = new GameObject("Building Panel", typeof(RectTransform));
        panelObject.transform.SetParent(managers.transform);
        var panel = panelObject.AddComponent<BuildingInfoPanel>();
        panel.Initialize(building);
        var binding = new BuildingPanelBinding { panel = panel };
        SetField(overlay, "panelBindings", new List<BuildingPanelBinding> { binding });
        SetField(overlay, "activeBinding", binding);
        SetField(overlay, "selectedBuilding", building);
        SetField(actions, "<SelectedBuilding>k__BackingField", building);

        views.SetViewMode(mode);
        Assert.IsNull(actions.SelectedBuilding);
        Assert.IsNull(overlay.SelectedBuilding);
        Assert.IsNull(panel.CurrentBuilding);
        Assert.IsFalse(panelObject.activeSelf);
        overlay.ShowBuilding(building);
        Assert.IsFalse(panelObject.activeSelf);
        views.SetNormalView();
        Assert.IsFalse(panelObject.activeSelf, "Returning to Normal must not reopen the old panel.");
    }

    [TestCase(PlayerViewMode.Overview)]
    [TestCase(PlayerViewMode.Traffic)]
    public void RestrictedViewsLeaveSimulationClockRunning(PlayerViewMode mode)
    {
        var clock = managers.AddComponent<GameTimeManager>();
        InvokeLifecycle(clock, "Awake");
        float timeScale = Time.timeScale;
        views.SetViewMode(mode);
        int previousHour = clock.CurrentHour;
        clock.AdvanceTime(clock.RealSecondsPerGameHour);
        Assert.AreEqual(previousHour + 1, clock.CurrentHour);
        Assert.AreEqual(timeScale, Time.timeScale);
    }

    [TestCase(PlayerViewMode.Overview)]
    [TestCase(PlayerViewMode.Traffic)]
    public void DestroyingViewUIDoesNotReactivateRootsOrLeaveEventSubscriptions(PlayerViewMode mode)
    {
        var ui = managers.AddComponent<PlayerViewModeUI>();
        var normalRoot = new GameObject("Normal UI");
        normalRoot.transform.SetParent(managers.transform);
        SetField(ui, "viewModeManager", views);
        SetField(ui, "normalUIRoots", new[] { normalRoot });
        InvokeLifecycle(ui, "OnEnable");
        views.SetViewMode(mode);

        InvokeLifecycle(ui, "OnDestroy");
        Assert.IsFalse(normalRoot.activeSelf, "Teardown must not reactivate objects awaiting destruction.");
        Assert.AreEqual(0, ((Dictionary<GameObject, bool>)GetField(ui, "savedVisibility")).Count);
        views.SetNormalView();
        Assert.IsFalse(normalRoot.activeSelf, "A destroyed presenter must no longer receive mode changes.");
    }

    [Test]
    public void CameraUsesAndClampsToEachViewModesZoomRange()
    {
        var cameraObject = new GameObject("View Mode Camera");
        cameraObject.transform.SetParent(managers.transform);
        var camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 6f;
        var controller = cameraObject.AddComponent<CameraController>();
        controller.cam = camera;
        controller.minZoom = 6f;
        controller.maxZoom = 20f;
        controller.overviewMinZoom = 12f;
        controller.overviewMaxZoom = 40f;
        controller.trafficMinZoom = 8f;
        controller.trafficMaxZoom = 30f;
        InvokeLifecycle(controller, "Awake");
        InvokeLifecycle(controller, "Start");

        Assert.AreEqual(6f, controller.ActiveMinZoom);
        Assert.AreEqual(20f, controller.ActiveMaxZoom);

        views.SetOverviewView();
        Assert.AreEqual(12f, controller.ActiveMinZoom);
        Assert.AreEqual(40f, controller.ActiveMaxZoom);
        Assert.AreEqual(6f, camera.orthographicSize, "The rendered camera should transition smoothly.");
        Assert.AreEqual(12f, (float)GetField(controller, "targetZoom"));

        camera.orthographicSize = 38f;
        SetField(controller, "targetZoom", 38f);
        views.SetTrafficView();
        Assert.AreEqual(8f, controller.ActiveMinZoom);
        Assert.AreEqual(30f, controller.ActiveMaxZoom);
        Assert.AreEqual(38f, camera.orthographicSize, "The rendered camera should transition smoothly.");
        Assert.AreEqual(30f, (float)GetField(controller, "targetZoom"));

        views.SetNormalView();
        Assert.AreEqual(38f, camera.orthographicSize);
        Assert.AreEqual(20f, (float)GetField(controller, "targetZoom"));
        Assert.AreEqual(0f, controller.CurrentZoomPercentage);
    }

    [Test]
    public void CameraNormalizesInvalidPerModeZoomRange()
    {
        var cameraObject = new GameObject("View Mode Camera");
        cameraObject.transform.SetParent(managers.transform);
        var camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 10f;
        var controller = cameraObject.AddComponent<CameraController>();
        controller.cam = camera;
        controller.overviewMinZoom = 25f;
        controller.overviewMaxZoom = 5f;
        InvokeLifecycle(controller, "Awake");
        InvokeLifecycle(controller, "Start");

        views.SetOverviewView();
        Assert.AreEqual(25f, controller.ActiveMinZoom);
        Assert.AreEqual(25f, controller.ActiveMaxZoom);
        Assert.AreEqual(10f, camera.orthographicSize);
        Assert.AreEqual(25f, (float)GetField(controller, "targetZoom"));
        Assert.AreEqual(100f, controller.CurrentZoomPercentage);
    }

    [Test]
    public void CameraMovementSpeedDoesNotChangeWhenViewModeChangesAtTheSameZoom()
    {
        var cameraObject = new GameObject("View Mode Camera");
        cameraObject.transform.SetParent(managers.transform);
        var camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 12f;
        var controller = cameraObject.AddComponent<CameraController>();
        controller.cam = camera;
        controller.minZoom = 6f;
        controller.maxZoom = 20f;
        controller.overviewMinZoom = 12f;
        controller.overviewMaxZoom = 40f;
        controller.trafficMinZoom = 8f;
        controller.trafficMaxZoom = 30f;
        InvokeLifecycle(controller, "Awake");
        InvokeLifecycle(controller, "Start");

        float normalSpeed = controller.CurrentMovementSpeed;
        views.SetOverviewView();
        float overviewSpeed = controller.CurrentMovementSpeed;
        views.SetTrafficView();
        float trafficSpeed = controller.CurrentMovementSpeed;

        Assert.AreEqual(normalSpeed, overviewSpeed);
        Assert.AreEqual(normalSpeed, trafficSpeed);
        Assert.AreEqual(
            Mathf.Lerp(controller.minSpeed, controller.maxSpeed, 6f / 14f),
            trafficSpeed,
            0.0001f);
    }

    private static void InvokeLifecycle(object target, string name) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(target, null);

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static object GetField(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
}
