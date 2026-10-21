using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[Flags]
public enum ResourceOrigin { None = 0, World = 1, Factory = 2 }

[Serializable]
public sealed class ResourceCategory
{
    [Tooltip("Stable, case-sensitive type ID used by resource memberships and future recipes. Renaming requires updating references.")]
    public string id;
    public string displayName;
    [TextArea] public string description;
    [Tooltip("Icon for this resource type. Uses the catalog's shared placeholder when unassigned.")]
    public Sprite icon;
}

[Serializable]
public sealed class ResourceDefinition
{
    [HideInInspector]
    public ResourceType resource;
    public string displayName;
    [TextArea] public string description;
    public ResourceOrigin availableFrom;
    [Tooltip("Shared icon used by factory controls and any other resource presentation.")]
    public Sprite icon;
    [Tooltip("Shared world tile icon for this resource's nodes/deposits in building footprints. Falls back to Icon, then the catalog Default Icon.")]
    public Sprite worldTileIcon;
    [Tooltip("Type IDs from this catalog. A resource can belong to any number of types.")]
    public List<string> types = new List<string>();
}

/// <summary>Authored identity metadata and many-to-many type membership; never stores runtime quantities.</summary>
[CreateAssetMenu(menuName = "Mini Colonies/Resource Catalog", fileName = "ResourceCatalog")]
public sealed class ResourceCatalog : ScriptableObject
{
    public const string DefaultResourcePath = "ResourceCatalog";
    public List<ResourceCategory> types = new List<ResourceCategory>();
    public List<ResourceDefinition> resources = new List<ResourceDefinition>();
    public Sprite fallbackIcon;
    // Keep the existing integer serialization used by factories, maps and cargo.
    // Enum names are compatibility aliases; custom identities are allocated here.
    [SerializeField, HideInInspector] private long nextResourceId = 1024;
    [SerializeField, HideInInspector] private long nextTypeId = 1;
    private static ResourceCatalog current;

    public static ResourceCatalog Current
    {
        get
        {
            if (current == null) current = Resources.Load<ResourceCatalog>(DefaultResourcePath);
            return current;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCurrent() => current = null;

    public ResourceDefinition Find(ResourceType resource)
    {
        if ((int)resource < 0 || resources == null) return null;
        for (int i = 0; i < resources.Count; i++)
            if (resources[i] != null && resources[i].resource == resource) return resources[i];
        return null;
    }

    public static bool IsKnownResource(ResourceType resource, ResourceCatalog catalog) =>
        catalog != null ? catalog.Find(resource) != null : Enum.IsDefined(typeof(ResourceType), resource);

    public ResourceDefinition AddResource(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Supply a resource name.", nameof(displayName));
        long candidate = Math.Max(1024, nextResourceId);
        if (resources != null)
            foreach (ResourceDefinition definition in resources)
                if (definition != null) candidate = Math.Max(candidate, (long)(int)definition.resource + 1);
        if (candidate > int.MaxValue) throw new InvalidOperationException("No resource IDs remain available.");
        var resource = new ResourceDefinition
        {
            resource = (ResourceType)(int)candidate,
            displayName = displayName.Trim(),
            availableFrom = ResourceOrigin.World | ResourceOrigin.Factory
        };
        if (resources == null) resources = new List<ResourceDefinition>();
        resources.Add(resource);
        nextResourceId = candidate + 1;
        return resource;
    }

    public ResourceCategory AddType(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Supply a type name.", nameof(displayName));
        long candidate = Math.Max(1, nextTypeId);
        string id;
        do
        {
            if (candidate == long.MaxValue) throw new InvalidOperationException("No type IDs remain available.");
            id = "custom-type-" + candidate++;
        }
        while (FindType(id) != null);
        var type = new ResourceCategory { id = id, displayName = displayName.Trim() };
        if (types == null) types = new List<ResourceCategory>();
        types.Add(type);
        nextTypeId = candidate;
        return type;
    }

    public ResourceCategory FindType(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || types == null) return null;
        for (int i = 0; i < types.Count; i++)
            if (types[i] != null && string.Equals(types[i].id, id, StringComparison.Ordinal)) return types[i];
        return null;
    }

    public bool HasType(ResourceType resource, string typeId)
    {
        if (FindType(typeId) == null) return false;
        ResourceDefinition definition = Find(resource);
        return definition != null && definition.types != null && definition.types.Contains(typeId);
    }

    public bool HasAllTypes(ResourceType resource, IReadOnlyList<string> requiredTypes)
    {
        // An empty or broken filter must never accept every resource by accident.
        if (requiredTypes == null || requiredTypes.Count == 0) return false;
        ResourceDefinition definition = Find(resource);
        if (definition?.types == null) return false;
        for (int i = 0; i < requiredTypes.Count; i++)
            if (FindType(requiredTypes[i]) == null || !definition.types.Contains(requiredTypes[i])) return false;
        return true;
    }

    public void GetResourcesMatchingTypes(IReadOnlyList<string> requiredTypes, List<ResourceType> results)
    {
        if (results == null) throw new ArgumentNullException(nameof(results));
        results.Clear();
        if (resources == null) return;
        for (int i = 0; i < resources.Count; i++)
        {
            ResourceDefinition definition = resources[i];
            if (definition != null && !results.Contains(definition.resource) && HasAllTypes(definition.resource, requiredTypes))
                results.Add(definition.resource);
        }
    }

    public Sprite GetResourceIcon(ResourceType resource)
    {
        Sprite icon = Find(resource)?.icon;
        return icon != null ? icon : fallbackIcon;
    }
    public static Sprite GetIcon(ResourceType resource) => Current != null ? Current.GetResourceIcon(resource) : null;

    public Sprite GetResourceWorldTileIcon(ResourceType resource)
    {
        Sprite icon = Find(resource)?.worldTileIcon;
        return icon != null ? icon : GetResourceIcon(resource);
    }
    public static Sprite GetWorldTileIcon(ResourceType resource) =>
        Current != null ? Current.GetResourceWorldTileIcon(resource) : null;

    public Sprite GetTypeIcon(string typeId)
    {
        Sprite icon = FindType(typeId)?.icon;
        return icon != null ? icon : fallbackIcon;
    }

    /// <summary>Replaces results in catalog order, without duplicate identities.</summary>
    public void GetResourcesWithType(string typeId, List<ResourceType> results)
    {
        if (results == null) throw new ArgumentNullException(nameof(results));
        results.Clear();
        if (resources == null || FindType(typeId) == null) return;
        for (int i = 0; i < resources.Count; i++)
        {
            ResourceDefinition definition = resources[i];
            if (definition != null && !results.Contains(definition.resource) && HasType(definition.resource, typeId))
                results.Add(definition.resource);
        }
    }

    public string GetResourceName(ResourceType resource)
    {
        ResourceDefinition definition = Find(resource);
        if (definition == null) return FallbackName(resource);
        if (!string.IsNullOrWhiteSpace(definition.displayName)) return definition.displayName.Trim();
        return Enum.IsDefined(typeof(ResourceType), resource) ? FallbackName(resource) : "Resource #" + (int)resource;
    }

    public static string GetDisplayName(ResourceType resource) =>
        Current != null ? Current.GetResourceName(resource) : FallbackName(resource);

    private static string FallbackName(ResourceType resource)
    {
        if (!Enum.IsDefined(typeof(ResourceType), resource)) return "Unknown resource";
        string name = resource.ToString();
        var result = new StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && char.IsLower(name[i - 1])) result.Append(' ');
            result.Append(name[i]);
        }
        return result.ToString();
    }

    // Validation reports mistakes without rewriting IDs, silently dropping memberships,
    // or depending on OnValidate to make runtime queries safe.
    public void GetValidationErrors(List<string> errors)
    {
        if (errors == null) throw new ArgumentNullException(nameof(errors));
        errors.Clear();
        var typeIds = new HashSet<string>(StringComparer.Ordinal);
        if (types != null)
        {
            for (int i = 0; i < types.Count; i++)
            {
                ResourceCategory type = types[i];
                if (type == null || string.IsNullOrWhiteSpace(type.id) || type.id != type.id.Trim())
                    errors.Add($"Type {i}: supply a non-blank ID without surrounding spaces.");
                else if (!typeIds.Add(type.id)) errors.Add($"Duplicate type ID: {type.id}.");
                if (type != null && string.IsNullOrWhiteSpace(type.displayName))
                    errors.Add($"Type {i}: supply a display name.");
            }
        }
        var identities = new HashSet<ResourceType>();
        if (resources == null) return;
        for (int i = 0; i < resources.Count; i++)
        {
            ResourceDefinition definition = resources[i];
            if (definition == null) { errors.Add($"Resource {i} is missing."); continue; }
            if ((int)definition.resource < 0)
                errors.Add($"Resource {i} has an invalid negative identity.");
            if (!identities.Add(definition.resource)) errors.Add($"Duplicate resource: {definition.resource}.");
            if (string.IsNullOrWhiteSpace(definition.displayName)) errors.Add($"{definition.resource}: supply a display name.");
            if (definition.availableFrom == ResourceOrigin.None ||
                (definition.availableFrom & ~(ResourceOrigin.World | ResourceOrigin.Factory)) != 0)
                errors.Add($"{definition.resource}: choose World, Factory or both origins.");
            var memberships = new HashSet<string>(StringComparer.Ordinal);
            if (definition.types == null) continue;
            for (int j = 0; j < definition.types.Count; j++)
            {
                string id = definition.types[j];
                if (id == null || !typeIds.Contains(id)) errors.Add($"{definition.resource}: unknown type '{id}'.");
                if (!memberships.Add(id)) errors.Add($"{definition.resource}: duplicate type '{id}'.");
            }
        }
    }
}
