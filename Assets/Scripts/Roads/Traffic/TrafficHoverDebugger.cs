using System.Collections.Generic;
using UnityEngine;

public class TrafficHoverDebugger : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("How close the mouse must be to a lane to lock onto it.")]
    public float hoverRadius = 3f;
    [Tooltip("LayerMask for your terrain/roads so the raycast knows what to hit.")]
    public LayerMask groundLayer;
    [Tooltip("Material used to draw the lines. A standard unlit sprite material works great.")]
    public Material lineMaterial;

    private TrafficEdge _currentHoveredEdge;
    private List<LineRenderer> _activeLines = new List<LineRenderer>();
    private List<GameObject> _activePointers = new List<GameObject>(); // Spheres to mark the end of turns

    void Update()
    {
        if (!Application.isPlaying) return;

        HandleMouseHover();
    }

    private void HandleMouseHover()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, groundLayer))
        {
            // Ask the Backend what lane we are hovering over
            TrafficEdge closestEdge = TrafficSystemBackend.Instance.GetClosestLane(hit.point, hoverRadius);

            if (closestEdge != _currentHoveredEdge)
            {
                _currentHoveredEdge = closestEdge;
                DrawSmartTrace(closestEdge);
            }
        }
        else if (_currentHoveredEdge != null)
        {
            _currentHoveredEdge = null;
            ClearVisuals();
        }
    }

    private void DrawSmartTrace(TrafficEdge startEdge)
    {
        ClearVisuals();
        if (startEdge == null) return;

        // Start recursive trace at depth 0, using Cyan for the initial main trunk
        TracePath(startEdge, 0, Color.cyan);
    }

    // A recursive method that allows us to branch out at intersections
    private void TracePath(TrafficEdge startEdge, int depth, Color pathColor)
    {
        TrafficEdge currentEdge = startEdge;
        int safetyBreaker = 0; // Prevent infinite loops in circular road networks

        while (currentEdge != null && safetyBreaker < 100)
        {
            safetyBreaker++;
            
            // Draw the straight lane. If depth > 0, make it slightly thinner so the main trunk stands out.
            float lineWidth = depth == 0 ? 0.25f : 0.15f;
            DrawLine(currentEdge.waypoints, pathColor, lineWidth);

            TrafficEdge nextStraightEdge = null;
            bool reachedIntersection = false;

            // Look at what connects to the end of this lane
            foreach (TrafficEdge outEdge in currentEdge.endNode.outgoingEdges)
            {
                if (outEdge.isIntersection)
                {
                    reachedIntersection = true;
                    
                    // Draw the turn curve thicker and in its logical color
                    DrawLine(outEdge.waypoints, outEdge.edgeColor, 0.35f);
                    
                    if (depth < 1)
                    {
                        // WE ARE AT THE FIRST INTERSECTION. 
                        // Recursively trace the straight roads that come OUT of this intersection.
                        foreach (TrafficEdge postIntersectionEdge in outEdge.endNode.outgoingEdges)
                        {
                            if (!postIntersectionEdge.isIntersection) 
                            {
                                // Inherit the turn's color so the user can track the whole branch!
                                TracePath(postIntersectionEdge, depth + 1, outEdge.edgeColor);
                            }
                        }
                    }
                    else
                    {
                        // WE ARE AT THE SECOND INTERSECTION. 
                        // Draw a pointer to indicate the end of our preview scope and stop tracing.
                        DrawPointer(outEdge.waypoints[outEdge.waypoints.Count - 1], outEdge.edgeColor);
                    }
                }
                else
                {
                    // It's a standard chunk-boundary continuation
                    nextStraightEdge = outEdge;
                }
            }

            // If we hit an intersection, the recursive calls took over, so we break this loop.
            if (reachedIntersection) break;

            // Otherwise, keep walking forward down the straight road.
            currentEdge = nextStraightEdge;
        }

        // Catch for Dead-Ends: If the road just ends without an intersection, draw a pointer.
        if (currentEdge != null && currentEdge.endNode.outgoingEdges.Count == 0)
        {
            DrawPointer(currentEdge.waypoints[currentEdge.waypoints.Count - 1], pathColor);
        }
    }

    // --- RENDERING HELPERS ---

    private void DrawLine(List<Vector3> points, Color color, float width)
    {
        GameObject lineObj = new GameObject("HoverDebugLine");
        lineObj.transform.SetParent(this.transform);
        
        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        if (lineMaterial != null) lr.material = lineMaterial;
        else lr.material = new Material(Shader.Find("Sprites/Default")); // Fallback

        // Make the line glow nicely
        color.a = 0.8f;
        lr.startColor = color;
        lr.endColor = color;
        lr.startWidth = width;
        lr.endWidth = width;
        lr.numCapVertices = 4;
        lr.numCornerVertices = 4;

        // Lift the line slightly so it doesn't clip into the road mesh
        Vector3[] liftedPoints = new Vector3[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            liftedPoints[i] = points[i] + (Vector3.up * 0.2f);
        }

        lr.positionCount = liftedPoints.Length;
        lr.SetPositions(liftedPoints);

        _activeLines.Add(lr);
    }

    private void DrawPointer(Vector3 position, Color color)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.SetParent(this.transform);
        sphere.transform.position = position + (Vector3.up * 0.2f);
        sphere.transform.localScale = Vector3.one * 0.4f;

        Destroy(sphere.GetComponent<Collider>()); // We don't want physics on this

        MeshRenderer mr = sphere.GetComponent<MeshRenderer>();
        if (lineMaterial != null) mr.material = lineMaterial;
        else mr.material = new Material(Shader.Find("Sprites/Default"));
        
        mr.material.color = color;
        
        _activePointers.Add(sphere);
    }

    private void ClearVisuals()
    {
        foreach (var lr in _activeLines) if (lr != null) Destroy(lr.gameObject);
        foreach (var ptr in _activePointers) if (ptr != null) Destroy(ptr.gameObject);
        
        _activeLines.Clear();
        _activePointers.Clear();
    }
}