using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MapResourceDefinition)), CanEditMultipleObjects]
public sealed class MapResourceDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        if (!serializedObject.isEditingMultipleObjects && ResourceCatalog.Current != null)
        {
            var definition = ResourceCatalog.Current.Find(((MapResourceDefinition)target).resourceType);
            if (definition != null)
                EditorGUILayout.HelpBox($"Resource: {definition.displayName}\nTypes: " +
                    (definition.types != null ? string.Join(", ", definition.types) : "None") +
                    "\nEdit shared types in Tools > Mini Colonies > Resources > Edit Resource Catalog.", MessageType.Info);
        }
        serializedObject.Update();
        var property = serializedObject.GetIterator();
        bool enterChildren = true;
        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (!IsVisible(property.name)) continue;
            using (new EditorGUI.DisabledScope(property.name == "m_Script"))
                EditorGUILayout.PropertyField(property, true);

            if (property.name == nameof(MapResourceDefinition.depositGrowth) &&
                !property.hasMultipleDifferentValues && property.intValue == (int)ResourceDepositGrowth.ResourceDefault)
            {
                var pattern = SelectedGrowth();
                if (pattern.HasValue) EditorGUILayout.LabelField("Default Growth", pattern.Value.ToString());
            }
        }
        if (serializedObject.FindProperty(nameof(MapResourceDefinition.shape)).hasMultipleDifferentValues)
            EditorGUILayout.HelpBox("Only common settings are shown while node and deposit resources are selected together.", MessageType.Info);
        else if (serializedObject.FindProperty(nameof(MapResourceDefinition.shape)).intValue == (int)MapResourceShape.Deposit &&
                 !SelectedGrowth().HasValue)
            EditorGUILayout.HelpBox("Select a common growth mode to edit growth parameters across these resources.", MessageType.Info);
        serializedObject.ApplyModifiedProperties();
    }

    private bool IsVisible(string field)
    {
        var shape = serializedObject.FindProperty(nameof(MapResourceDefinition.shape));
        bool deposit = !shape.hasMultipleDifferentValues && shape.intValue == (int)MapResourceShape.Deposit;
        var growth = SelectedGrowth();
        switch (field)
        {
            case nameof(MapResourceDefinition.depth):
            case nameof(MapResourceDefinition.depositGrowth): return deposit;
            case nameof(MapResourceDefinition.depositCompactness):
                return deposit && growth.HasValue && growth != ResourceDepositGrowth.Branching;
            case nameof(MapResourceDefinition.depositIrregularity): return deposit && growth == ResourceDepositGrowth.Organic;
            case nameof(MapResourceDefinition.depositElongation):
            case nameof(MapResourceDefinition.randomizeDepositDirection): return deposit && growth == ResourceDepositGrowth.Vein;
            case nameof(MapResourceDefinition.depositGrowthAngle):
                var randomDirection = serializedObject.FindProperty(nameof(MapResourceDefinition.randomizeDepositDirection));
                return deposit && growth == ResourceDepositGrowth.Vein &&
                    !randomDirection.hasMultipleDifferentValues && !randomDirection.boolValue;
            case nameof(MapResourceDefinition.depositBranchChance): return deposit && growth == ResourceDepositGrowth.Branching;
            case nameof(MapResourceDefinition.depositPrefabChance): return deposit && HasPrefabVariants();
            case nameof(MapResourceDefinition.prefabScaleRange):
            case nameof(MapResourceDefinition.randomYaw):
            case nameof(MapResourceDefinition.prefabHeightOffset): return HasPrefabVariants();
            case nameof(MapResourceDefinition.terrainTint):
            case nameof(MapResourceDefinition.terrainTransitionStyle):
            case nameof(MapResourceDefinition.terrainBlendWidth):
            case nameof(MapResourceDefinition.terrainBlendAmount):
            case nameof(MapResourceDefinition.terrainBlendRandomness):
            case nameof(MapResourceDefinition.terrainBlendNoiseSize):
            case nameof(MapResourceDefinition.terrainEdgeRoundness):
                var material = serializedObject.FindProperty(nameof(MapResourceDefinition.terrainMaterial));
                return material.hasMultipleDifferentValues || material.objectReferenceValue != null;
            default: return true;
        }
    }

    private ResourceDepositGrowth? SelectedGrowth()
    {
        var growth = serializedObject.FindProperty(nameof(MapResourceDefinition.depositGrowth));
        var type = serializedObject.FindProperty(nameof(MapResourceDefinition.resourceType));
        if (growth.hasMultipleDifferentValues ||
            (growth.intValue == (int)ResourceDepositGrowth.ResourceDefault && type.hasMultipleDifferentValues)) return null;
        return MapResourceDepositGrowth.ResolvePattern((ResourceDepositGrowth)growth.intValue, (ResourceType)type.intValue);
    }

    private bool HasPrefabVariants()
    {
        var variants = serializedObject.FindProperty(nameof(MapResourceDefinition.prefabVariants));
        if (variants.hasMultipleDifferentValues) return true;
        for (int i = 0; i < variants.arraySize; i++)
            if (variants.GetArrayElementAtIndex(i).objectReferenceValue != null) return true;
        return false;
    }
}
