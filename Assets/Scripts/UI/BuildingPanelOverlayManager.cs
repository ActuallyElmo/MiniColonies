using System;
using System.Collections.Generic;
using UnityEngine;

public enum BuildingCategory
{
    Housing,
    Industrial
}

[Serializable]
public sealed class BuildingPanelBinding
{
    [Tooltip("The general building category whose instances use this panel.")]
    public BuildingCategory category;

    [Tooltip("A panel that is already a child of the overlay Canvas.")]
    public BuildingInfoPanel panel;
}

[DisallowMultipleComponent]
public sealed class BuildingPanelOverlayManager : MonoBehaviour
{
    [Header("Overlay")]
    [SerializeField] private Canvas overlayCanvas;
    private Camera worldCamera;

    [Header("Building Panels")]
    [SerializeField] private List<BuildingPanelBinding> panelBindings =
        new List<BuildingPanelBinding>();

    [Header("Panel Positioning")]
    [SerializeField, Min(0f)]
    [Tooltip("Horizontal screen-space gap between the building and its panel.")]
    private float buildingPanelGap = 16f;

    [SerializeField, Min(0f)]
    [Tooltip("Minimum distance in pixels between a panel and a screen edge.")]
    private float screenPadding = 8f;

    private Building selectedBuilding;
    private BuildingPanelBinding activeBinding;
    private readonly List<Renderer> selectedBuildingRenderers =
        new List<Renderer>();
    private readonly Vector3[] panelWorldCorners = new Vector3[4];
    private PlayerActionManager subscribedPlayerActionManager;
    private BuildingSystemBackend subscribedBuildingSystem;

    public Building SelectedBuilding => selectedBuilding;
    public bool IsShowingPanel =>
        selectedBuilding != null &&
        activeBinding != null &&
        activeBinding.panel != null &&
        activeBinding.panel.gameObject.activeSelf;

    private void Awake()
    {
        HideAllPanels();
    }

    private void Start()
    {
        SubscribeToGameEvents();

        if (worldCamera == null)
        {
            worldCamera = Camera.main;
        }

        if (PlayerActionManager.Instance != null &&
            PlayerActionManager.Instance.currentMode != PlayerMode.None)
        {
            HideCurrentPanel();
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFromGameEvents();
    }

    private void LateUpdate()
    {
        if (selectedBuilding == null || activeBinding == null)
        {
            return;
        }

        if (PlayerActionManager.Instance == null ||
            PlayerActionManager.Instance.currentMode != PlayerMode.None)
        {
            HideCurrentPanel();
            return;
        }

        UpdateActivePanelPosition();
    }

    public void ShowBuilding(Building building)
    {
        if (building == null ||
            PlayerActionManager.Instance == null ||
            PlayerActionManager.Instance.currentMode != PlayerMode.None)
        {
            HideCurrentPanel();
            return;
        }

        BuildingPanelBinding binding = FindBinding(building.data);
        if (binding == null || binding.panel == null)
        {
            HideCurrentPanel();
            string buildingName =
                building.data != null ? building.data.buildingName : building.name;
            Debug.LogWarning(
                $"No building information panel is configured for {buildingName}.",
                this);
            return;
        }

        HideAllPanels();

        selectedBuilding = building;
        activeBinding = binding;
        CacheBuildingRenderers(building);

        binding.panel.gameObject.SetActive(true);
        binding.panel.Initialize(building);
        Canvas.ForceUpdateCanvases();
        UpdateActivePanelPosition();
    }

    /// <summary>
    /// Can be connected directly to a panel close Button in the Inspector.
    /// </summary>
    public void HideCurrentPanel()
    {
        if (activeBinding != null && activeBinding.panel != null)
        {
            activeBinding.panel.Clear();
        }

        selectedBuilding = null;
        activeBinding = null;
        selectedBuildingRenderers.Clear();
        HideAllPanels();
    }

    private BuildingPanelBinding FindBinding(BuildingData buildingData)
    {
        if (!TryGetBuildingCategory(
                buildingData,
                out BuildingCategory buildingCategory))
        {
            return null;
        }

        for (int i = 0; i < panelBindings.Count; i++)
        {
            BuildingPanelBinding binding = panelBindings[i];
            if (binding != null && binding.category == buildingCategory)
            {
                return binding;
            }
        }

        return null;
    }

    private static bool TryGetBuildingCategory(
        BuildingData buildingData,
        out BuildingCategory buildingCategory)
    {
        if (buildingData is PopHousingBuildingData)
        {
            buildingCategory = BuildingCategory.Housing;
            return true;
        }

        if (buildingData is ResourceProductionBuildingData)
        {
            buildingCategory = BuildingCategory.Industrial;
            return true;
        }

        buildingCategory = default;
        return false;
    }

    private void UpdateActivePanelPosition()
    {
        if (activeBinding == null ||
            activeBinding.panel == null ||
            selectedBuilding == null)
        {
            return;
        }

        if (worldCamera == null)
        {
            worldCamera = Camera.main;
            if (worldCamera == null) return;
        }

        if (overlayCanvas == null)
        {
            overlayCanvas =
                activeBinding.panel.GetComponentInParent<Canvas>();
            if (overlayCanvas == null) return;
        }

        if (!TryGetBuildingScreenRect(out Rect buildingScreenRect))
        {
            activeBinding.panel.gameObject.SetActive(false);
            return;
        }

        activeBinding.panel.gameObject.SetActive(true);

        RectTransform panelRect =
            activeBinding.panel.transform as RectTransform;
        if (panelRect == null) return;

        RectTransform positioningRect =
            panelRect.parent as RectTransform;
        if (positioningRect == null)
        {
            positioningRect = overlayCanvas.transform as RectTransform;
        }
        if (positioningRect == null) return;

        Camera canvasCamera =
            overlayCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : overlayCanvas.worldCamera;

        Canvas.ForceUpdateCanvases();
        if (!TryGetScreenRect(panelRect, canvasCamera, out Rect panelScreenRect))
        {
            return;
        }

        Vector2 desiredPanelPivot = new Vector2(
            buildingScreenRect.xMax +
                buildingPanelGap +
                panelScreenRect.width * panelRect.pivot.x,
            buildingScreenRect.center.y +
                panelScreenRect.height * (panelRect.pivot.y - 0.5f));

        if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                positioningRect,
                desiredPanelPivot,
                canvasCamera,
                out Vector3 desiredWorldPosition))
        {
            return;
        }

        panelRect.position = desiredWorldPosition;
        Canvas.ForceUpdateCanvases();
        ClampPanelToScreen(panelRect, positioningRect, canvasCamera);
    }

    private void CacheBuildingRenderers(Building building)
    {
        selectedBuildingRenderers.Clear();
        if (building == null) return;

        Renderer[] renderers =
            building.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer candidate = renderers[i];
            if (candidate == null ||
                candidate.GetComponentInParent<VehicleAI>(true) != null)
            {
                continue;
            }

            selectedBuildingRenderers.Add(candidate);
        }
    }

    private bool TryGetBuildingScreenRect(out Rect screenRect)
    {
        float minimumX = float.MaxValue;
        float minimumY = float.MaxValue;
        float maximumX = float.MinValue;
        float maximumY = float.MinValue;
        bool foundVisiblePoint = false;

        for (int i = 0; i < selectedBuildingRenderers.Count; i++)
        {
            Renderer buildingRenderer = selectedBuildingRenderers[i];
            if (buildingRenderer == null ||
                !buildingRenderer.enabled ||
                !buildingRenderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            Bounds bounds = buildingRenderer.bounds;
            Vector3 minimum = bounds.min;
            Vector3 maximum = bounds.max;

            for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
            {
                Vector3 worldCorner = new Vector3(
                    (cornerIndex & 1) == 0 ? minimum.x : maximum.x,
                    (cornerIndex & 2) == 0 ? minimum.y : maximum.y,
                    (cornerIndex & 4) == 0 ? minimum.z : maximum.z);

                Vector3 screenPoint =
                    worldCamera.WorldToScreenPoint(worldCorner);
                if (screenPoint.z <= 0f) continue;

                minimumX = Mathf.Min(minimumX, screenPoint.x);
                minimumY = Mathf.Min(minimumY, screenPoint.y);
                maximumX = Mathf.Max(maximumX, screenPoint.x);
                maximumY = Mathf.Max(maximumY, screenPoint.y);
                foundVisiblePoint = true;
            }
        }

        if (!foundVisiblePoint)
        {
            Vector3 fallbackPoint =
                worldCamera.WorldToScreenPoint(selectedBuilding.transform.position);
            if (fallbackPoint.z <= 0f)
            {
                screenRect = default;
                return false;
            }

            minimumX = maximumX = fallbackPoint.x;
            minimumY = maximumY = fallbackPoint.y;
        }

        screenRect = Rect.MinMaxRect(
            minimumX,
            minimumY,
            maximumX,
            maximumY);
        return true;
    }

    private bool TryGetScreenRect(
        RectTransform rectTransform,
        Camera canvasCamera,
        out Rect screenRect)
    {
        rectTransform.GetWorldCorners(panelWorldCorners);

        float minimumX = float.MaxValue;
        float minimumY = float.MaxValue;
        float maximumX = float.MinValue;
        float maximumY = float.MinValue;

        for (int i = 0; i < panelWorldCorners.Length; i++)
        {
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(
                canvasCamera,
                panelWorldCorners[i]);
            minimumX = Mathf.Min(minimumX, screenPoint.x);
            minimumY = Mathf.Min(minimumY, screenPoint.y);
            maximumX = Mathf.Max(maximumX, screenPoint.x);
            maximumY = Mathf.Max(maximumY, screenPoint.y);
        }

        screenRect = Rect.MinMaxRect(
            minimumX,
            minimumY,
            maximumX,
            maximumY);
        return maximumX > minimumX && maximumY > minimumY;
    }

    private void ClampPanelToScreen(
        RectTransform panelRect,
        RectTransform positioningRect,
        Camera canvasCamera)
    {
        if (!TryGetScreenRect(
                panelRect,
                canvasCamera,
                out Rect panelScreenRect))
        {
            return;
        }

        float minimumScreenX = screenPadding;
        float maximumScreenX = Screen.width - screenPadding;
        float minimumScreenY = screenPadding;
        float maximumScreenY = Screen.height - screenPadding;

        Vector2 correction = Vector2.zero;

        if (panelScreenRect.xMin < minimumScreenX)
        {
            correction.x += minimumScreenX - panelScreenRect.xMin;
        }
        if (panelScreenRect.xMax > maximumScreenX)
        {
            correction.x += maximumScreenX - panelScreenRect.xMax;
        }
        if (panelScreenRect.yMin < minimumScreenY)
        {
            correction.y += minimumScreenY - panelScreenRect.yMin;
        }
        if (panelScreenRect.yMax > maximumScreenY)
        {
            correction.y += maximumScreenY - panelScreenRect.yMax;
        }

        if (correction == Vector2.zero) return;

        Vector2 currentPivotScreenPosition =
            RectTransformUtility.WorldToScreenPoint(
                canvasCamera,
                panelRect.position);
        Vector2 correctedPivotScreenPosition =
            currentPivotScreenPosition + correction;

        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                positioningRect,
                correctedPivotScreenPosition,
                canvasCamera,
                out Vector3 correctedWorldPosition))
        {
            panelRect.position = correctedWorldPosition;
        }
    }

    private void HideAllPanels()
    {
        var hiddenPanels = new HashSet<BuildingInfoPanel>();

        for (int i = 0; i < panelBindings.Count; i++)
        {
            BuildingInfoPanel panel = panelBindings[i]?.panel;
            if (panel != null && hiddenPanels.Add(panel))
            {
                if (panel.CurrentBuilding != null)
                {
                    panel.Clear();
                }

                panel.gameObject.SetActive(false);
            }
        }
    }

    private void SubscribeToGameEvents()
    {
        subscribedPlayerActionManager = PlayerActionManager.Instance;
        if (subscribedPlayerActionManager != null)
        {
            subscribedPlayerActionManager.ModeChanged += HandleModeChanged;
            subscribedPlayerActionManager.BuildingSelectionChanged +=
                HandleBuildingSelectionChanged;
        }

        subscribedBuildingSystem = BuildingSystemBackend.Instance;
        if (subscribedBuildingSystem != null)
        {
            subscribedBuildingSystem.OnBuildingRemoved += HandleBuildingRemoved;
        }
    }

    private void UnsubscribeFromGameEvents()
    {
        if (subscribedPlayerActionManager != null)
        {
            subscribedPlayerActionManager.ModeChanged -= HandleModeChanged;
            subscribedPlayerActionManager.BuildingSelectionChanged -=
                HandleBuildingSelectionChanged;
        }

        if (subscribedBuildingSystem != null)
        {
            subscribedBuildingSystem.OnBuildingRemoved -= HandleBuildingRemoved;
        }
    }

    private void HandleModeChanged(PlayerMode newMode)
    {
        if (newMode != PlayerMode.None)
        {
            HideCurrentPanel();
        }
    }

    private void HandleBuildingSelectionChanged(Building building)
    {
        if (building != null)
        {
            ShowBuilding(building);
        }
        else
        {
            HideCurrentPanel();
        }
    }

    private void HandleBuildingRemoved(Building building)
    {
        if (building == selectedBuilding)
        {
            HideCurrentPanel();
        }
    }
}
