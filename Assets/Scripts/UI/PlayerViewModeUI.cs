using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Keeps view controls visible while hiding only the assigned gameplay UI roots.
/// Additional camera controls can live alongside these roots on the same Canvas.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerViewModeUI : MonoBehaviour
{
    [SerializeField] private PlayerViewModeManager viewModeManager;
    [SerializeField] private Button overviewButton;
    [SerializeField] private Button normalButton;
    [SerializeField] private Button trafficButton;
    [SerializeField] private GameObject[] normalUIRoots;
    [SerializeField] private Button trafficNodeButton;
    [SerializeField] private PopTransportVehicleData debugPopVehicleData;
    [SerializeField] private Button debugPopVehicleButton;
    private TMP_Text trafficNodeLabel;
    private TMP_Text debugPopVehicleLabel;
    private bool ownsTrafficNodeButton;
    private bool ownsDebugPopVehicleButton;
    private bool ownsDebugPopVehicleSpawner;
    private DebugPopVehicleSpawner debugPopVehicleSpawner;

    private readonly Dictionary<GameObject, bool> savedVisibility = new Dictionary<GameObject, bool>();

    private void OnEnable()
    {
        if (viewModeManager == null) return;
        EnsureTrafficNodeButton();
        EnsureDebugPopVehicleSpawner();
        EnsureDebugPopVehicleButton();
        if (trafficNodeButton != null)
        {
            trafficNodeButton.onClick.RemoveListener(viewModeManager.ToggleTrafficNodeView);
            trafficNodeButton.onClick.AddListener(viewModeManager.ToggleTrafficNodeView);
        }
        if (debugPopVehicleButton != null && debugPopVehicleSpawner != null)
        {
            debugPopVehicleButton.onClick.RemoveListener(
                debugPopVehicleSpawner.ToggleSpawning);
            debugPopVehicleButton.onClick.AddListener(
                debugPopVehicleSpawner.ToggleSpawning);
        }
        viewModeManager.TrafficNodeViewChanged -= ApplyTrafficNodeView;
        viewModeManager.TrafficNodeViewChanged += ApplyTrafficNodeView;
        if (debugPopVehicleSpawner != null)
        {
            debugPopVehicleSpawner.SpawnEnabledChanged -=
                ApplyDebugPopVehicleSpawning;
            debugPopVehicleSpawner.SpawnEnabledChanged +=
                ApplyDebugPopVehicleSpawning;
        }
        // Keep listening while this component/canvas is disabled: some managed
        // roots (such as the building-panel canvas) live elsewhere in the scene.
        viewModeManager.ViewModeChanged -= ApplyViewMode;
        viewModeManager.ViewModeChanged += ApplyViewMode;
        ApplyViewMode(viewModeManager.CurrentViewMode);
    }

    private void OnDestroy()
    {
        if (viewModeManager != null)
        {
            viewModeManager.ViewModeChanged -= ApplyViewMode;
            viewModeManager.TrafficNodeViewChanged -= ApplyTrafficNodeView;
            if (trafficNodeButton != null)
                trafficNodeButton.onClick.RemoveListener(viewModeManager.ToggleTrafficNodeView);
        }
        if (debugPopVehicleSpawner != null)
        {
            debugPopVehicleSpawner.SpawnEnabledChanged -=
                ApplyDebugPopVehicleSpawning;
            if (debugPopVehicleButton != null)
            {
                debugPopVehicleButton.onClick.RemoveListener(
                    debugPopVehicleSpawner.ToggleSpawning);
            }
        }
        if (ownsTrafficNodeButton && trafficNodeButton != null)
        {
            if (Application.isPlaying) Destroy(trafficNodeButton.gameObject);
            else DestroyImmediate(trafficNodeButton.gameObject);
        }
        if (ownsDebugPopVehicleButton && debugPopVehicleButton != null)
        {
            if (Application.isPlaying) Destroy(debugPopVehicleButton.gameObject);
            else DestroyImmediate(debugPopVehicleButton.gameObject);
        }
        if (ownsDebugPopVehicleSpawner && debugPopVehicleSpawner != null)
        {
            if (Application.isPlaying) Destroy(debugPopVehicleSpawner);
            else DestroyImmediate(debugPopVehicleSpawner);
        }
        // Scene teardown may already be destroying these roots even while
        // Unity's null check still succeeds. Only a return to Normal restores UI.
        savedVisibility.Clear();
    }

    private void ApplyViewMode(PlayerViewMode mode)
    {
        SetButtonState(overviewButton, mode == PlayerViewMode.Overview);
        SetButtonState(normalButton, mode == PlayerViewMode.Normal);
        SetButtonState(trafficButton, mode == PlayerViewMode.Traffic);
        if (trafficNodeButton != null)
            trafficNodeButton.gameObject.SetActive(mode == PlayerViewMode.Traffic);
        if (debugPopVehicleButton != null)
            debugPopVehicleButton.gameObject.SetActive(mode == PlayerViewMode.Normal);
        if (debugPopVehicleSpawner != null)
            debugPopVehicleSpawner.SetInputAllowed(mode == PlayerViewMode.Normal);
        ApplyTrafficNodeView(viewModeManager.TrafficNodeViewEnabled);
        ApplyDebugPopVehicleSpawning(
            debugPopVehicleSpawner != null &&
            debugPopVehicleSpawner.SpawnEnabled);

        if (mode == PlayerViewMode.Normal)
        {
            RestoreNormalUI();
            return;
        }

        if (normalUIRoots == null) return;
        foreach (GameObject root in normalUIRoots)
        {
            if (root == null) continue;
            // Preserve the original state across Overview <-> Traffic switches.
            if (!savedVisibility.ContainsKey(root))
                savedVisibility.Add(root, root.activeSelf);
            root.SetActive(false);
        }
    }

    private void RestoreNormalUI()
    {
        foreach (var entry in savedVisibility)
            if (entry.Key != null) entry.Key.SetActive(entry.Value);
        savedVisibility.Clear();
    }

    // Build alongside the existing controls so existing scenes gain the option
    // without replacing their authored canvas or persistent button callbacks.
    private void EnsureTrafficNodeButton()
    {
        if (trafficNodeButton != null)
        {
            trafficNodeLabel = trafficNodeButton.GetComponentInChildren<TMP_Text>(true);
            return;
        }
        if (trafficButton == null) return;
        var root = new GameObject("TrafficNodeViewButton", typeof(RectTransform), typeof(Image), typeof(Button));
        root.layer = trafficButton.gameObject.layer;
        var rect = (RectTransform)root.transform;
        var source = (RectTransform)trafficButton.transform;
        rect.SetParent(source.parent, false);
        rect.anchorMin = source.anchorMin;
        rect.anchorMax = source.anchorMax;
        rect.pivot = source.pivot;
        rect.sizeDelta = source.sizeDelta;
        rect.anchoredPosition = source.anchoredPosition + Vector2.down * (source.rect.height + 8f);
        var graphic = root.GetComponent<Image>();
        if (trafficButton.targetGraphic is Image sourceImage)
        {
            graphic.sprite = sourceImage.sprite;
            graphic.type = sourceImage.type;
        }
        trafficNodeButton = root.GetComponent<Button>();
        trafficNodeButton.targetGraphic = graphic;
        trafficNodeButton.colors = trafficButton.colors;
        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.layer = root.layer;
        var labelRect = (RectTransform)labelObject.transform;
        labelRect.SetParent(rect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(6f, 2f);
        labelRect.offsetMax = new Vector2(-6f, -2f);
        trafficNodeLabel = labelObject.GetComponent<TMP_Text>();
        var sourceLabel = trafficButton.GetComponentInChildren<TMP_Text>(true);
        if (sourceLabel != null) trafficNodeLabel.font = sourceLabel.font;
        trafficNodeLabel.fontSize = sourceLabel != null ? sourceLabel.fontSize : 18f;
        trafficNodeLabel.color = sourceLabel != null ? sourceLabel.color : Color.black;
        trafficNodeLabel.alignment = TextAlignmentOptions.Center;
        trafficNodeLabel.raycastTarget = false;
        ownsTrafficNodeButton = true;
    }

    private void ApplyTrafficNodeView(bool enabled)
    {
        if (trafficNodeButton == null) return;
        // A selected toggle remains clickable so it can be turned off.
        trafficNodeButton.interactable = true;
        if (trafficNodeButton.targetGraphic != null)
            trafficNodeButton.targetGraphic.color = enabled ? new Color(0.35f, 0.65f, 1f) : Color.white;
        if (trafficNodeLabel != null)
            trafficNodeLabel.text = enabled ? "Traffic nodes: On" : "Traffic nodes: Off";
    }

    private void EnsureDebugPopVehicleSpawner()
    {
        debugPopVehicleSpawner =
            viewModeManager.GetComponent<DebugPopVehicleSpawner>();
        if (debugPopVehicleSpawner == null)
        {
            debugPopVehicleSpawner =
                viewModeManager.gameObject.AddComponent<DebugPopVehicleSpawner>();
            ownsDebugPopVehicleSpawner = true;
        }

        debugPopVehicleSpawner.Configure(debugPopVehicleData);
    }

    private void EnsureDebugPopVehicleButton()
    {
        if (debugPopVehicleButton != null)
        {
            debugPopVehicleLabel =
                debugPopVehicleButton.GetComponentInChildren<TMP_Text>(true);
            return;
        }

        Button sourceButton = normalButton != null ? normalButton : trafficButton;
        if (sourceButton == null) return;
        Button layoutButton = trafficButton != null ? trafficButton : sourceButton;

        var root = new GameObject(
            "DebugPopVehicleButton",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button));
        root.layer = sourceButton.gameObject.layer;
        var rect = (RectTransform)root.transform;
        var source = (RectTransform)sourceButton.transform;
        var layout = (RectTransform)layoutButton.transform;
        rect.SetParent(source.parent, false);
        rect.anchorMin = layout.anchorMin;
        rect.anchorMax = layout.anchorMax;
        rect.pivot = layout.pivot;
        rect.sizeDelta = new Vector2(
            Mathf.Max(layout.sizeDelta.x, 170f),
            layout.sizeDelta.y);
        // Keep the authored view-mode column intact. The debug control gets
        // its own row beneath Traffic instead of covering that button in Normal.
        rect.anchoredPosition = layout.anchoredPosition +
                                Vector2.down * (layout.rect.height + 8f);

        var graphic = root.GetComponent<Image>();
        if (sourceButton.targetGraphic is Image sourceImage)
        {
            graphic.sprite = sourceImage.sprite;
            graphic.type = sourceImage.type;
        }

        debugPopVehicleButton = root.GetComponent<Button>();
        debugPopVehicleButton.targetGraphic = graphic;
        debugPopVehicleButton.colors = sourceButton.colors;

        var labelObject = new GameObject(
            "Label",
            typeof(RectTransform),
            typeof(TextMeshProUGUI));
        labelObject.layer = root.layer;
        var labelRect = (RectTransform)labelObject.transform;
        labelRect.SetParent(rect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(6f, 2f);
        labelRect.offsetMax = new Vector2(-6f, -2f);
        debugPopVehicleLabel = labelObject.GetComponent<TMP_Text>();
        TMP_Text sourceLabel =
            sourceButton.GetComponentInChildren<TMP_Text>(true);
        if (sourceLabel != null) debugPopVehicleLabel.font = sourceLabel.font;
        debugPopVehicleLabel.fontSize = sourceLabel != null
            ? sourceLabel.fontSize
            : 18f;
        debugPopVehicleLabel.color = sourceLabel != null
            ? sourceLabel.color
            : Color.black;
        debugPopVehicleLabel.alignment = TextAlignmentOptions.Center;
        debugPopVehicleLabel.raycastTarget = false;
        ownsDebugPopVehicleButton = true;
    }

    private void ApplyDebugPopVehicleSpawning(bool enabled)
    {
        if (debugPopVehicleButton == null) return;
        debugPopVehicleButton.interactable = true;
        if (debugPopVehicleButton.targetGraphic != null)
        {
            debugPopVehicleButton.targetGraphic.color = enabled
                ? new Color(0.35f, 0.65f, 1f, 1f)
                : Color.white;
        }
        if (debugPopVehicleLabel != null)
        {
            debugPopVehicleLabel.text = enabled
                ? "Debug car spawns: On"
                : "Debug car spawns: Off";
        }
    }

    private static void SetButtonState(Button button, bool selected)
    {
        if (button == null) return;
        button.interactable = !selected;
        if (button.targetGraphic != null)
            button.targetGraphic.color = selected
                ? new Color(0.35f, 0.65f, 1f, 1f)
                : Color.white;
    }
}
