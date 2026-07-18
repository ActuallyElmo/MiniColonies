using UnityEngine;

public class GridOverlayHandler : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The GameObject holding the URP Decal Projector")]
    public GameObject gridProjector;
    
    [Tooltip("The Layer your Chunk meshes are placed on (so we don't raycast against UI or other stuff)")]
    public LayerMask terrainLayer;

    [Header("Settings")]
    [Tooltip("How high above the terrain the projector should sit. Keep this high enough to clear cliffs.")]
    public float projectorHeightOffset = 10f;

    private void Update()
    {
        if(gridProjector == null) return; // Safety check in case we forgot to assign the projector prefab
        
        // Cast a ray from the mouse pointer into the world
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, terrainLayer))
        {
            // Enable the projector if we are hitting the terrain
            if (!gridProjector.activeSelf) gridProjector.SetActive(true);

            // Move the projector to the hit point, offset upwards on the Y axis
            // We use the hit point's X and Z to follow the mouse exactly
            Vector3 targetPosition = new Vector3(hit.point.x, hit.point.y + projectorHeightOffset, hit.point.z);
            gridProjector.transform.position = targetPosition;

            // Ensure the projector is always pointing straight down at the terrain
            gridProjector.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }
        else
        {
            // Optional: Hide the projector if the mouse leaves the map
            if (gridProjector.activeSelf) gridProjector.SetActive(false);
        }
    }
}
