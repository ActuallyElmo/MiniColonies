using UnityEngine;

// Resolves prefab collider hits to the same source as terrain hits.
public sealed class MapResourceMarker : MonoBehaviour
{
    public MapResourceSource Source { get; internal set; }
}
