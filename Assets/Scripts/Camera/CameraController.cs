using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class CameraController : MonoBehaviour
{
    private static readonly int CameraZoomPercentageId = Shader.PropertyToID("_CameraZoomPercentage");
    private static readonly int CameraZoomPercentageSetId = Shader.PropertyToID("_CameraZoomPercentageSet");

    public static CameraController Instance { get; private set; }

    [Header("Movement Settings")]
    [Tooltip("Speed when fully zoomed IN.")]
    public float minSpeed = 8f;
    [Tooltip("Speed when fully zoomed OUT.")]
    public float maxSpeed = 35f; 
    [Tooltip("Higher values feel snappier, lower values feel floatier.")]
    public float movementSmoothness = 8f;

    [Header("Zoom Settings")]
    public float zoomSensitivity = 10f;
    [Tooltip("Normal view: minimum orthographic size (most zoomed in).")]
    public float minZoom = 5f;
    [Tooltip("Normal view: maximum orthographic size (most zoomed out).")]
    public float maxZoom = 20f;
    [Header("View Mode Zoom Ranges")]
    [Tooltip("Overview view: minimum orthographic size (most zoomed in).")]
    [Min(0.01f)] public float overviewMinZoom = 12f;
    [Tooltip("Overview view: maximum orthographic size (most zoomed out).")]
    [Min(0.01f)] public float overviewMaxZoom = 40f;
    [Tooltip("Traffic view: minimum orthographic size (most zoomed in).")]
    [Min(0.01f)] public float trafficMinZoom = 8f;
    [Tooltip("Traffic view: maximum orthographic size (most zoomed out).")]
    [Min(0.01f)] public float trafficMaxZoom = 30f;
    public float zoomSmoothTime = 0.1f;
    [Range(0f, 1f)]
    [Tooltip("How strongly zooming moves toward the mouse. Zooming out moves away by the same amount.")]
    public float zoomMouseBias = 0.2f;

    [Header("Zoom State (Read Only)")]
    [SerializeField, Range(0f, 100f)]
    [Tooltip("Current camera zoom. 100% is fully zoomed in and 0% is fully zoomed out.")]
    private float currentZoomPercentage;

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
    private Vector3 zoomBiasScreenPosition;
    private bool hasZoomBiasPosition;
    private WorldManager worldManager;
    private PlayerViewModeManager subscribedViewModeManager;
    private float activeMinZoom;
    private float activeMaxZoom;
    private readonly List<RaycastResult> uiRaycastResults =
        new List<RaycastResult>();

    /// <summary>
    /// Current camera zoom from 0 to 100. A value of 100 is fully zoomed in.
    /// </summary>
    public float CurrentZoomPercentage => currentZoomPercentage;

    /// <summary>
    /// Current camera zoom normalized from 0 to 1. A value of 1 is fully zoomed in.
    /// </summary>
    public float CurrentZoom01 => currentZoomPercentage * 0.01f;

    public float ActiveMinZoom => activeMinZoom;
    public float ActiveMaxZoom => activeMaxZoom;
    public float CurrentMovementSpeed =>
        GetMovementSpeed(cam != null ? cam.orthographicSize : minZoom);

    /// <summary>
    /// Raised whenever the rendered camera zoom percentage changes.
    /// </summary>
    public event Action<float> ZoomPercentageChanged;

    private void Awake()
    {
        Instance = this;

        if (cam == null) cam = GetComponent<Camera>();
        if (cam == null)
        {
            enabled = false;
            Debug.LogError("CameraController requires a Camera reference.", this);
            return;
        }

        NormalizeZoomRange();
        SetActiveZoomRange(PlayerViewMode.Normal, false);
        targetZoom = cam.orthographicSize;
        targetZoom = Mathf.Clamp(targetZoom, activeMinZoom, activeMaxZoom);

        PublishZoomPercentage(true);
    }

    private void Start()
    {
        worldManager = WorldManager.Instance;
        SubscribeToViewModes();
    }

    void Update()
    {
        HandleMovement();
        HandleZoom();
        PublishZoomPercentage();
    }

    private void HandleMovement()
    {
        // 1. Get Input
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        Vector3 inputDir = new Vector3(horizontal, 0, vertical).normalized;

        // Movement depends on the visible camera scale, not the active view
        // mode's limits. The same orthographic size must always feel the same.
        float zoomedOut01 = GetMovementZoomedOut01(cam.orthographicSize);
        
        // Lerp uses that 0 to 1 percentage to find the exact speed we need.
        float currentTargetSpeed = GetMovementSpeed(cam.orthographicSize);

        // 2. Calculate the exact velocity we WANT right now based on input
        Vector3 targetVelocity = inputDir * currentTargetSpeed;

        // 3. Smoothly interpolate our current velocity toward the target velocity.
        currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, movementSmoothness * Time.deltaTime);

        // 4. Calculate new position
        Vector3 newPosition = transform.position + currentVelocity * Time.deltaTime;

        // 5. BOUNDARY CLAMPING
        newPosition = ClampToWorldBounds(newPosition, zoomedOut01);

        // 6. Apply position
        transform.position = newPosition;
    }

    private void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");

        if (Mathf.Abs(scroll) > 0f && !IsPointerOverBuildingPanel())
        {
            targetZoom -= scroll * zoomSensitivity;
            targetZoom = Mathf.Clamp(targetZoom, activeMinZoom, activeMaxZoom);
            zoomBiasScreenPosition = Input.mousePosition;
            hasZoomBiasPosition = true;
        }

        float mouseBias = Mathf.Clamp01(zoomMouseBias);
        float previousZoom = cam.orthographicSize;
        Vector3 mousePointBeforeZoom = default;
        bool hasMousePointBeforeZoom = hasZoomBiasPosition
            && mouseBias > 0f
            && TryGetGroundPoint(zoomBiasScreenPosition, out mousePointBeforeZoom);

        float newZoom = Mathf.SmoothDamp(
            cam.orthographicSize, 
            targetZoom, 
            ref zoomDampVelocity, 
            zoomSmoothTime
        );

        cam.orthographicSize = newZoom;

        if (hasMousePointBeforeZoom
            && !Mathf.Approximately(previousZoom, newZoom)
            && TryGetGroundPoint(zoomBiasScreenPosition, out Vector3 mousePointAfterZoom))
        {
            // The cursor's projected world point moves toward the camera center as the
            // view shrinks. Following part of that displacement biases zoom-in toward
            // the cursor; the displacement naturally reverses while zooming out.
            transform.position += (mousePointBeforeZoom - mousePointAfterZoom) * mouseBias;
            transform.position = ClampToWorldBounds(
                transform.position,
                GetMovementZoomedOut01(newZoom)
            );
        }
    }

    private bool IsPointerOverBuildingPanel()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null) return false;

        var pointerData = new PointerEventData(eventSystem)
        {
            position = Input.mousePosition
        };
        uiRaycastResults.Clear();
        eventSystem.RaycastAll(pointerData, uiRaycastResults);

        for (int i = 0; i < uiRaycastResults.Count; i++)
        {
            GameObject hitObject = uiRaycastResults[i].gameObject;
            if (hitObject != null &&
                hitObject.GetComponentInParent<BuildingInfoPanel>() != null)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetGroundPoint(Vector3 screenPosition, out Vector3 groundPoint)
    {
        Plane groundPlane = new Plane(Vector3.up, transform.position);
        Ray mouseRay = cam.ScreenPointToRay(screenPosition);

        if (groundPlane.Raycast(mouseRay, out float enter))
        {
            groundPoint = mouseRay.GetPoint(enter);
            return true;
        }

        groundPoint = default;
        return false;
    }

    private Vector3 ClampToWorldBounds(Vector3 position, float zoomedOut01)
    {
        if (worldManager == null) return position;

        float physicalWorldSize = worldManager.worldSize * worldManager.cellSize;
        Vector2 currentPadding = Vector2.Lerp(minZoomPadding, maxZoomPadding, zoomedOut01);

        // Vector2.y maps to the 3D Z-axis.
        float minX = currentPadding.x;
        float maxX = physicalWorldSize - currentPadding.x;
        float minZ = currentPadding.y;
        float maxZ = physicalWorldSize - currentPadding.y;
        NormalizeBounds(ref minX, ref maxX, physicalWorldSize * 0.5f);
        NormalizeBounds(ref minZ, ref maxZ, physicalWorldSize * 0.5f);

        // Prevent velocity buildup against the walls to stop the camera feeling sticky.
        if (position.x <= minX || position.x >= maxX) currentVelocity.x = 0f;
        if (position.z <= minZ || position.z >= maxZ) currentVelocity.z = 0f;

        position.x = Mathf.Clamp(position.x, minX, maxX);
        position.z = Mathf.Clamp(position.z, minZ, maxZ);
        return position;
    }

    /// <summary>
    /// Converts an orthographic size to a zoom percentage using this controller's range.
    /// </summary>
    public float GetZoomPercentage(float orthographicSize)
    {
        if (Mathf.Approximately(activeMinZoom, activeMaxZoom)) return 100f;

        float zoomedOut01 = Mathf.InverseLerp(
            activeMinZoom,
            activeMaxZoom,
            orthographicSize);
        return (1f - zoomedOut01) * 100f;
    }

    /// <summary>
    /// Returns camera movement speed for a visible orthographic size. This uses
    /// the Normal range as a shared reference so view modes cannot change speed
    /// without changing the camera's actual zoom.
    /// </summary>
    public float GetMovementSpeed(float orthographicSize)
    {
        return Mathf.Lerp(
            minSpeed,
            maxSpeed,
            GetMovementZoomedOut01(orthographicSize));
    }

    private float GetMovementZoomedOut01(float orthographicSize)
    {
        return Mathf.InverseLerp(minZoom, maxZoom, orthographicSize);
    }

    private void PublishZoomPercentage(bool forceNotification = false)
    {
        float newZoomPercentage = GetZoomPercentage(cam.orthographicSize);
        bool changed = !Mathf.Approximately(currentZoomPercentage, newZoomPercentage);

        currentZoomPercentage = newZoomPercentage;
        Shader.SetGlobalFloat(CameraZoomPercentageId, currentZoomPercentage);
        Shader.SetGlobalFloat(CameraZoomPercentageSetId, 1f);

        if (changed || forceNotification)
        {
            ZoomPercentageChanged?.Invoke(currentZoomPercentage);
        }
    }

    private void OnDisable()
    {
        Shader.SetGlobalFloat(CameraZoomPercentageSetId, 0f);
    }

    private void OnDestroy()
    {
        if (subscribedViewModeManager != null)
            subscribedViewModeManager.ViewModeChanged -= HandleViewModeChanged;
        if (Instance == this) Instance = null;
    }

    private void NormalizeZoomRange()
    {
        NormalizeZoomRange(ref minZoom, ref maxZoom);
        NormalizeZoomRange(ref overviewMinZoom, ref overviewMaxZoom);
        NormalizeZoomRange(ref trafficMinZoom, ref trafficMaxZoom);
    }

    private void SubscribeToViewModes()
    {
        if (subscribedViewModeManager != null)
            subscribedViewModeManager.ViewModeChanged -= HandleViewModeChanged;

        subscribedViewModeManager = PlayerViewModeManager.Instance;
        if (subscribedViewModeManager == null)
        {
            SetActiveZoomRange(PlayerViewMode.Normal, true);
            return;
        }

        subscribedViewModeManager.ViewModeChanged += HandleViewModeChanged;
        SetActiveZoomRange(subscribedViewModeManager.CurrentViewMode, true);
    }

    private void HandleViewModeChanged(PlayerViewMode mode)
    {
        SetActiveZoomRange(mode, true);
    }

    private void SetActiveZoomRange(PlayerViewMode mode, bool clampCamera)
    {
        NormalizeZoomRange();
        switch (mode)
        {
            case PlayerViewMode.Overview:
                activeMinZoom = overviewMinZoom;
                activeMaxZoom = overviewMaxZoom;
                break;
            case PlayerViewMode.Traffic:
                activeMinZoom = trafficMinZoom;
                activeMaxZoom = trafficMaxZoom;
                break;
            default:
                activeMinZoom = minZoom;
                activeMaxZoom = maxZoom;
                break;
        }

        if (!clampCamera || cam == null) return;

        targetZoom = Mathf.Clamp(targetZoom, activeMinZoom, activeMaxZoom);
        // Let HandleZoom ease the rendered camera toward the newly allowed
        // range. This makes view changes feel like a natural centered zoom.
        zoomDampVelocity = 0f;
        hasZoomBiasPosition = false;
        PublishZoomPercentage(true);
    }

    private static void NormalizeZoomRange(ref float minimum, ref float maximum)
    {
        minimum = Mathf.Max(0.01f, minimum);
        maximum = Mathf.Max(minimum, maximum);
    }

    private static void NormalizeBounds(
        ref float minimum,
        ref float maximum,
        float fallback)
    {
        if (minimum <= maximum) return;

        minimum = fallback;
        maximum = fallback;
    }
}
