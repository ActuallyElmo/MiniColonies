using System.Collections.Generic;
using UnityEngine;

public enum TrafficEdgeKind
{
    RoadLane,
    LaneChange,
    RoadTypeTransition,
    IntersectionMovement,
    RoadEndUTurn
}

public enum RoadNodeKind
{
    ThroughRoad,
    Transition,
    RoadEnd,
    Intersection
}

public enum IntersectionRuleType
{
    FreeForAll,
    FIFO,
    TrafficLight,
    PriorityRoad,
    RuleOfWay
}

public enum TrafficTurnType
{
    UTurn,
    Left,
    Straight,
    Right
}

// Structure needed for intersection routing connections
public class LaneEndpoint
{
    public TrafficNode Node;
    public int LocalLaneIndex;       
    public int TotalLanes;           
    public Vector3 Direction;
    public Vector2Int NeighborCell;
}

// A distinct point in space where a vehicle can be.
public class TrafficNode
{
    public Vector3 position;
    public List<TrafficEdge> outgoingEdges = new List<TrafficEdge>();
    public TrafficGraphVersion graphVersion;

    public TrafficNode(Vector3 pos) { position = pos; }
}

public class TrafficEdgeReservation
{
    public VehicleAI vehicle;
    public float minDistance;
    public float maxDistance;
    public int sequence;
}

// A directional link between two nodes, representing a drivable lane.
public class TrafficEdge
{
    public int edgeId = -1;
    public TrafficEdgeKind kind = TrafficEdgeKind.RoadLane;
    public TrafficGraphVersion graphVersion;
    public RoadSectionId stableSectionId;
    public LaneId stableLaneId;
    public LaneSegmentId stableLaneSegmentId;
    public MovementId stableMovementId;
    public ControlledNodeId stableMovementOwnerId;
    public RoadPermissionMask requiredPermissions = RoadPermissionMask.None;
    public VehicleCapabilityMask requiredCapabilities =
        VehicleCapabilityMask.None;

    public TrafficNode startNode;
    public TrafficNode endNode;
    
    // The exact path vehicles will follow along this edge
    public List<Vector3> waypoints = new List<Vector3>();
    public List<float> cumulativeWaypointDistances = new List<float>();
    public float totalLength;
    // Runtime occupants are kept front-most first by ConveyorTrafficManager.
    public List<VehicleAI> occupants = new List<VehicleAI>();
    public List<TrafficEdgeReservation> reservations = new List<TrafficEdgeReservation>();
    
    public float speedLimit;
    public bool isIntersection;
    public Color edgeColor;

    public int laneIndex;       // 0 is Left-most, max is Right-most
    public int totalLanes;
    public bool isMergeEdge;    // True if this edge exists just to switch lanes
    public bool isUTurn;
    public IIntersectionController exitController;

    public Vector2Int controlledNodeCell;
    public bool hasControlledNodeCell;
    public int fromDirectionBit;
    public int toDirectionBit;
    public int fromLaneIndex = -1;
    public int toLaneIndex = -1;
    public TrafficTurnType turnType = TrafficTurnType.Straight;
    public int conflictMask;
    public Vector2Int transitionCell;
    public int transitionPriority;
    public bool isRoadTypeTransition;
    // Managed runtime references used to reserve merge space. Native pathfinding
    // only needs the scalar edge metadata above.
    public TrafficEdge reservedTargetEdge;
    public TrafficEdge conflictingLaneChangeEdge;

    public TrafficEdge(TrafficNode start, TrafficNode end, float limit, bool intersection = false)
    {
        startNode = start;
        endNode = end;
        speedLimit = limit;
        isIntersection = intersection;
        kind = intersection ? TrafficEdgeKind.IntersectionMovement : TrafficEdgeKind.RoadLane;
    }

    public void RecalculateLength()
    {
        cumulativeWaypointDistances.Clear();
        totalLength = 0f;

        if (waypoints == null || waypoints.Count == 0)
        {
            cumulativeWaypointDistances.Add(0f);
            return;
        }

        cumulativeWaypointDistances.Add(0f);
        for (int i = 1; i < waypoints.Count; i++)
        {
            totalLength += Vector3.Distance(waypoints[i - 1], waypoints[i]);
            cumulativeWaypointDistances.Add(totalLength);
        }
    }

    public Vector3 GetPositionAtDistance(float distance)
    {
        if (waypoints == null || waypoints.Count == 0) return startNode != null ? startNode.position : Vector3.zero;
        EnsureLengthData();
        if (waypoints.Count == 1 || totalLength <= 0f) return waypoints[0];

        float clampedDistance = Mathf.Clamp(distance, 0f, totalLength);

        for (int i = 1; i < cumulativeWaypointDistances.Count; i++)
        {
            float segmentEnd = cumulativeWaypointDistances[i];
            if (clampedDistance > segmentEnd) continue;

            float segmentStart = cumulativeWaypointDistances[i - 1];
            float segmentLength = segmentEnd - segmentStart;
            float t = segmentLength > 0f ? (clampedDistance - segmentStart) / segmentLength : 0f;
            return Vector3.Lerp(waypoints[i - 1], waypoints[i], t);
        }

        return waypoints[waypoints.Count - 1];
    }

    public Vector3 GetDirectionAtDistance(float distance)
    {
        if (waypoints == null || waypoints.Count < 2) return Vector3.forward;

        EnsureLengthData();
        if (totalLength <= 0f) return Vector3.forward;

        float clampedDistance = Mathf.Clamp(distance, 0f, totalLength);
        float sampleSpan = Mathf.Min(0.04f, totalLength * 0.5f);
        float backDistance = Mathf.Clamp(clampedDistance - sampleSpan, 0f, totalLength);
        float forwardDistance = Mathf.Clamp(clampedDistance + sampleSpan, 0f, totalLength);
        if (Mathf.Abs(forwardDistance - backDistance) > 0.001f)
        {
            Vector3 sampledDirection = GetPositionAtDistance(forwardDistance) - GetPositionAtDistance(backDistance);
            if (sampledDirection.sqrMagnitude > 0.0001f) return sampledDirection.normalized;
        }

        for (int i = 1; i < cumulativeWaypointDistances.Count; i++)
        {
            if (clampedDistance > cumulativeWaypointDistances[i]) continue;

            Vector3 direction = waypoints[i] - waypoints[i - 1];
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        }

        Vector3 fallback = waypoints[waypoints.Count - 1] - waypoints[waypoints.Count - 2];
        return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector3.forward;
    }

    public float GetClosestDistanceAlongEdge(Vector3 worldPoint)
    {
        if (waypoints == null || waypoints.Count == 0) return 0f;
        EnsureLengthData();
        if (waypoints.Count == 1 || totalLength <= 0f) return 0f;

        float bestSqrDistance = float.MaxValue;
        float bestDistance = 0f;

        for (int i = 1; i < waypoints.Count; i++)
        {
            Vector3 segmentStart = waypoints[i - 1];
            Vector3 segmentEnd = waypoints[i];
            Vector3 segment = segmentEnd - segmentStart;
            float segmentLengthSqr = segment.sqrMagnitude;
            if (segmentLengthSqr <= 0.0001f) continue;

            float t = Mathf.Clamp01(Vector3.Dot(worldPoint - segmentStart, segment) / segmentLengthSqr);
            Vector3 projected = segmentStart + segment * t;
            float sqrDistance = (worldPoint - projected).sqrMagnitude;
            if (sqrDistance >= bestSqrDistance) continue;

            bestSqrDistance = sqrDistance;
            float segmentDistance = Mathf.Sqrt(segmentLengthSqr) * t;
            bestDistance = cumulativeWaypointDistances[i - 1] + segmentDistance;
        }

        return Mathf.Clamp(bestDistance, 0f, totalLength);
    }

    private void EnsureLengthData()
    {
        if (cumulativeWaypointDistances == null)
        {
            cumulativeWaypointDistances = new List<float>();
        }

        if (cumulativeWaypointDistances.Count != waypoints.Count)
        {
            RecalculateLength();
        }
    }

    public override string ToString()
    {
        return $"TrafficEdge {edgeId} ({kind})";
    }
}
