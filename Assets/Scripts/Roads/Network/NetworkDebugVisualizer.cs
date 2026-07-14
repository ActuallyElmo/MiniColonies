using UnityEngine;
using System.Text;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class NetworkDebugVisualizer : MonoBehaviour
{
    [Header("Gizmo Settings")]
    public bool showNetworkColors = true;
    public bool showConnectedBuildings = true;
    [Tooltip("Draws small spheres on the exact port cells that touch a network.")]
    public bool showBuildingPorts = true; 
    public float gizmoHeightOffset = 0.5f;

    [Header("Controls")]
    [Tooltip("Press this key to print a full network report to the console.")]
    public KeyCode printReportKey = KeyCode.F3;

    private void Update()
    {
        if (Input.GetKeyDown(printReportKey))
        {
            PrintNetworkReport();
        }
    }

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || RoadNetworkManager.Instance == null || WorldManager.Instance == null || BuildingSystemBackend.Instance == null) return;

        // --- PASS 1: DRAW ROAD CELLS ---
        if (showNetworkColors)
        {
            foreach (RoadNetwork network in RoadNetworkManager.Instance.activeNetworks)
            {
                Gizmos.color = network.debugColor;
                foreach (Vector2Int cell in network.roadCells)
                {
                    Vector3 pos = GetWorldPosition(cell);
                    Gizmos.DrawCube(pos, new Vector3(WorldManager.Instance.cellSize * 0.2f, 0.2f, WorldManager.Instance.cellSize * 0.2f));
                }
            }
        }

        // --- PASS 2: DRAW BUILDINGS & PORTS ---
        if (showConnectedBuildings || showBuildingPorts)
        {
            foreach (Building building in BuildingSystemBackend.Instance.GetActiveBuildings())
            {
                Vector3 centerPos = GetWorldPosition(building.originCell);

                // 2A: Visualize individual ports touching a network
                if (showBuildingPorts)
                {
                    foreach (var kvp in building.portNetworks)
                    {
                        Vector2Int portCell = kvp.Key;
                        RoadNetwork net = kvp.Value;
                        
                        Gizmos.color = net.debugColor;
                        Vector3 portWorldPos = GetWorldPosition(portCell);
                        portWorldPos.y += 0.3f; // Float slightly above the road
                        
                        // Draw a smaller sphere for the active port connection
                        Gizmos.DrawSphere(portWorldPos, WorldManager.Instance.cellSize * 0.25f);
                        
                        // Draw a subtle line connecting the port back to the building center
                        Gizmos.DrawLine(centerPos, portWorldPos);
                    }
                }

                // 2B: Visualize FULLY VALID network connections (Entry & Exit satisfied)
                if (showConnectedBuildings && building.validNetworks.Count > 0)
                {
                    float stackHeight = 2.0f; // Starting height above the building

                    foreach (RoadNetwork validNet in building.validNetworks)
                    {
                        Gizmos.color = validNet.debugColor;
                        Vector3 floatingPos = centerPos;
                        floatingPos.y += stackHeight;

                        // Draw the validation ring
                        Gizmos.DrawWireSphere(floatingPos, WorldManager.Instance.cellSize * 0.8f);
                        Gizmos.DrawLine(floatingPos, centerPos); // Tie it back down to the building

                        // If testing in the Unity Editor, draw a crisp text label with the ID!
                        #if UNITY_EDITOR
                        GUIStyle style = new GUIStyle();
                        style.normal.textColor = validNet.debugColor;
                        style.fontStyle = FontStyle.Bold;
                        Handles.Label(floatingPos + (Vector3.up * 0.5f) + (Vector3.right * 0.5f), $"Net ID: {validNet.id}", style);
                        #endif

                        // Push the next valid network ring higher up to prevent overlapping
                        stackHeight += 1.5f; 
                    }
                }
            }
        }
    }

    private Vector3 GetWorldPosition(Vector2Int gridPos)
    {
        float cellSize = WorldManager.Instance.cellSize;
        float x = gridPos.x * cellSize + (cellSize * 0.5f);
        float z = gridPos.y * cellSize + (cellSize * 0.5f);
        float y = WorldManager.Instance.GetPhysicalHeight(gridPos.x + 0.5f, gridPos.y + 0.5f) * WorldManager.Instance.heightStep;
        
        return new Vector3(x, y + gizmoHeightOffset, z);
    }

    private void PrintNetworkReport()
    {
        if (RoadNetworkManager.Instance == null) return;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("<b>--- SYSTEM DATA REPORT ---</b>");
        sb.AppendLine($"Total Active Networks: {RoadNetworkManager.Instance.activeNetworks.Count}");
        sb.AppendLine($"Total Placed Buildings: {BuildingSystemBackend.Instance.GetActiveBuildings().Count}");
        sb.AppendLine("----------------------------");

        foreach (RoadNetwork net in RoadNetworkManager.Instance.activeNetworks)
        {
            sb.AppendLine($"<color=#{ColorUtility.ToHtmlStringRGB(net.debugColor)}>Network ID [{net.id}]</color>: {net.roadCells.Count} Road Cells, {net.connectedBuildings.Count} Valid Buildings.");
            
            if (net.connectedBuildings.Count > 0)
            {
                sb.Append("   -> Connected: ");
                foreach (Building b in net.connectedBuildings)
                {
                    sb.Append($"{b.data.buildingName} at {b.originCell}, ");
                }
                sb.AppendLine();
            }
        }

        Debug.Log(sb.ToString());
    }
}