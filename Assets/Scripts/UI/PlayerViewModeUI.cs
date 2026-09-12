using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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

    private readonly Dictionary<GameObject, bool> savedVisibility = new Dictionary<GameObject, bool>();

    private void OnEnable()
    {
        if (viewModeManager == null) return;
        // Keep listening while this component/canvas is disabled: some managed
        // roots (such as the building-panel canvas) live elsewhere in the scene.
        viewModeManager.ViewModeChanged -= ApplyViewMode;
        viewModeManager.ViewModeChanged += ApplyViewMode;
        ApplyViewMode(viewModeManager.CurrentViewMode);
    }

    private void OnDestroy()
    {
        if (viewModeManager != null)
            viewModeManager.ViewModeChanged -= ApplyViewMode;
        // Scene teardown may already be destroying these roots even while
        // Unity's null check still succeeds. Only a return to Normal restores UI.
        savedVisibility.Clear();
    }

    private void ApplyViewMode(PlayerViewMode mode)
    {
        SetButtonState(overviewButton, mode == PlayerViewMode.Overview);
        SetButtonState(normalButton, mode == PlayerViewMode.Normal);
        SetButtonState(trafficButton, mode == PlayerViewMode.Traffic);

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
