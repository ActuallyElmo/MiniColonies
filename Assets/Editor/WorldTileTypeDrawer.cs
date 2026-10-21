using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

[CustomPropertyDrawer(typeof(WorldTileType))]
public sealed class WorldTileTypeDrawer : PropertyDrawer
{
    public override VisualElement CreatePropertyGUI(SerializedProperty property)
    {
        var serialized = property.serializedObject;
        string path = property.propertyPath;
        return new IMGUIContainer(() =>
        {
            if (serialized.targetObject == null) return;
            serialized.UpdateIfRequiredOrScript();
            SerializedProperty current = serialized.FindProperty(path);
            if (current == null) return;
            OnGUI(EditorGUILayout.GetControlRect(), current, new GUIContent(current.displayName));
            serialized.ApplyModifiedProperties();
        });
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        var selected = new WorldTileType
        {
            kind = (WorldTileKind)property.FindPropertyRelative("kind").intValue,
            resource = (ResourceType)property.FindPropertyRelative("resource").intValue
        };
        Rect button = EditorGUI.PrefixLabel(position, label);
        bool mixed = property.FindPropertyRelative("kind").hasMultipleDifferentValues ||
            (selected.IsResource && property.FindPropertyRelative("resource").hasMultipleDifferentValues);
        if (GUI.Button(button, mixed ? "Mixed" : selected.ToString(), EditorStyles.popup))
        {
            var menu = new GenericMenu();
            Object[] targets = property.serializedObject.targetObjects;
            string path = property.propertyPath;
            Add(new WorldTileType { kind = WorldTileKind.FlatGround }, "Flat Ground");
            Add(new WorldTileType { kind = WorldTileKind.Edge }, "Edges (slopes and cliffs)");
            Add(new WorldTileType { kind = WorldTileKind.WaterLevel }, "Water Level");
            var choices = new HashSet<WorldTileType>();
            // Discover on opening the menu, so new/edited definitions appear
            // immediately without scanning assets during Inspector repaint.
            foreach (string guid in AssetDatabase.FindAssets("t:MapResourceDefinition"))
            {
                var definition = AssetDatabase.LoadAssetAtPath<MapResourceDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (definition == null || (int)definition.resourceType < 0 ||
                    (definition.shape != MapResourceShape.Nodes && definition.shape != MapResourceShape.Deposit)) continue;
                var type = new WorldTileType
                {
                    kind = definition.shape == MapResourceShape.Nodes ? WorldTileKind.ResourceNode : WorldTileKind.ResourceDeposit,
                    resource = definition.resourceType
                };
                if (choices.Add(type)) Add(type, (type.kind == WorldTileKind.ResourceNode ? "Resource Nodes/" : "Resource Deposits/") +
                    type.ToString());
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Edit Resource Catalog..."), false, ResourceCatalogSetup.EditCatalog);
            menu.DropDown(button);

            void Add(WorldTileType type, string title)
            {
                menu.AddItem(new GUIContent(title), !mixed && selected.Equals(type), () =>
                {
                    using (var serialized = new SerializedObject(targets))
                    {
                        SerializedProperty current = serialized.FindProperty(path);
                        if (current == null) return;
                        current.FindPropertyRelative("kind").intValue = (int)type.kind;
                        current.FindPropertyRelative("resource").intValue = (int)type.resource;
                        serialized.ApplyModifiedProperties();
                    }
                });
            }
        }
        EditorGUI.EndProperty();
    }
}
