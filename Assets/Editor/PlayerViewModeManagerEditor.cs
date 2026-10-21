using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlayerViewModeManager)), CanEditMultipleObjects]
public sealed class PlayerViewModeManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        if (DrawDefaultInspector() && Application.isPlaying)
            RefreshAppearance();
    }

    private void OnEnable() => Undo.undoRedoPerformed += RefreshAppearance;
    private void OnDisable() => Undo.undoRedoPerformed -= RefreshAppearance;

    private void RefreshAppearance()
    {
        foreach (var inspected in targets)
        {
            if (inspected == null) continue;
            var manager = (PlayerViewModeManager)inspected;
            manager.RefreshTrafficNodeAppearance();
            manager.RefreshIntersectionBuilderAppearance();
        }
    }
}
