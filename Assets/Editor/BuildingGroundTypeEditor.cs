using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BuildingGroundType)), CanEditMultipleObjects]
public sealed class BuildingGroundTypeEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("Legacy ground asset. New footprint rules use Optional Types and Required Types; map resources appear in their selectors automatically without a ground asset.", MessageType.Info);
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("kind"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("icon"));
        SerializedProperty kind = serializedObject.FindProperty("kind");
        if (kind.hasMultipleDifferentValues)
        {
            EditorGUILayout.HelpBox("Select a common ground kind to edit its matching rule.", MessageType.Info);
        }
        else switch ((BuildingGroundKind)kind.intValue)
        {
            case BuildingGroundKind.TerrainLayer:
                EditorGUILayout.PropertyField(serializedObject.FindProperty("terrainLayer"));
                EditorGUILayout.HelpBox("Matches a WorldManager layer index, including unbuildable layers such as water. Allows a different elevation for this cell. Cliffs and blocking resources still need their own permission.", MessageType.Info);
                break;
            case BuildingGroundKind.Cliff:
                EditorGUILayout.HelpBox("Matches WorldManager transition cells (slopes/cliffs). Allows the transition and its elevation. Blocking resources still need their own permission.", MessageType.Info);
                break;
            case BuildingGroundKind.MapResource:
                EditorGUILayout.PropertyField(serializedObject.FindProperty("resourceType"));
                EditorGUILayout.HelpBox("Matches logical resource tiles at any depth. Allows that resource's construction blocker, retaining normal terrain rules. An empty icon uses the resource catalog icon.", MessageType.Info);
                break;
        }
        serializedObject.ApplyModifiedProperties();
    }
}
