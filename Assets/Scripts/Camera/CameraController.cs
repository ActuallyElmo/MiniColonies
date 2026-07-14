using UnityEngine;

public class CameraController : MonoBehaviour
{

    [Header("Movement Settings")]
    [Tooltip("Speed when fully zoomed IN.")]
    public float minSpeed = 8f;
    [Tooltip("Speed when fully zoomed OUT.")]
    public float maxSpeed = 35f; 
    [Tooltip("Higher values feel snappier, lower values feel floatier.")]
    public float movementSmoothness = 8f;

    [Header("Zoom Settings")]
    public float zoomSensitivity = 10f;
    public float minZoom = 5f;
    public float maxZoom = 20f;
    public float zoomSmoothTime = 0.1f;

    [Header("Boundary Settings")]
    [Tooltip("Padding (X = Horizontal, Y = Vertical/Z-axis) when fully zoomed IN.")]
    public Vector2 minZoomPadding = new Vector2(5f, 5f);
    
    [Tooltip("Padding (X = Horizontal, Y = Vertical/Z-axis) when fully zoomed OUT.")]
    public Vector2 maxZoomPadding = new Vector2(25f, 15f);

    public Camera cam;
    private Vector3 currentVelocity;
    
    // Zoom specific variables
    private float targetZoom;
    private float zoomDampVelocity;
    private WorldManager worldManager;

    void Start()
    {
        targetZoom = cam.orthographicSize;

        worldManager = WorldManager.Instance;
    }

    void Update()
    {
        HandleMovement();
        HandleZoom();
    }

    private void HandleMovement()
    {
        // 1. Get Input
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        Vector3 inputDir = new Vector3(horizontal, 0, vertical).normalized;

        // Calculate how zoomed in/out we are (0.0 to 1.0)
        float zoomPercentage = Mathf.InverseLerp(minZoom, maxZoom, cam.orthographicSize);
        
        // Lerp uses that 0 to 1 percentage to find the exact speed and padding we need.
        float currentTargetSpeed = Mathf.Lerp(minSpeed, maxSpeed, zoomPercentage);
        Vector2 currentPadding = Vector2.Lerp(minZoomPadding, maxZoomPadding, zoomPercentage);

        // 2. Calculate the exact velocity we WANT right now based on input
        Vector3 targetVelocity = inputDir * currentTargetSpeed;

        // 3. Smoothly interpolate our current velocity toward the target velocity.
        currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, movementSmoothness * Time.deltaTime);

        // 4. Calculate new position
        Vector3 newPosition = transform.position + currentVelocity * Time.deltaTime;

        // 5. BOUNDARY CLAMPING
        if (worldManager != null)
        {
            float physicalWorldSize = worldManager.worldSize * worldManager.cellSize;

            // Apply the dynamic Vector2 padding. 
            // Note: Vector2.y maps to the 3D Z-axis.
            float minX = currentPadding.x;
            float maxX = physicalWorldSize - currentPadding.x;
            float minZ = currentPadding.y; 
            float maxZ = physicalWorldSize - currentPadding.y;

            // Prevent velocity buildup against the walls to stop the camera from feeling "sticky"
            if (newPosition.x <= minX || newPosition.x >= maxX) currentVelocity.x = 0;
            if (newPosition.z <= minZ || newPosition.z >= maxZ) currentVelocity.z = 0;

            // Physically clamp the position
            newPosition.x = Mathf.Clamp(newPosition.x, minX, maxX);
            newPosition.z = Mathf.Clamp(newPosition.z, minZ, maxZ);
        }

        // 6. Apply position
        transform.position = newPosition;
    }

    private void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");

        if (Mathf.Abs(scroll) > 0f)
        {
            targetZoom -= scroll * zoomSensitivity;
            targetZoom = Mathf.Clamp(targetZoom, minZoom, maxZoom);
        }

        cam.orthographicSize = Mathf.SmoothDamp(
            cam.orthographicSize, 
            targetZoom, 
            ref zoomDampVelocity, 
            zoomSmoothTime
        );
    }
}