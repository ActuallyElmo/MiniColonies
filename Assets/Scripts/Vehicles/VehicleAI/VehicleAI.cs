using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class VehicleAI : MonoBehaviour
{
    public enum VehicleState { Idle, Outbound, Inbound }
    
    [Header("State Data")]
    public VehicleState currentState = VehicleState.Idle;
    public Building homeBuilding;
    public Building targetBuilding;
    public VehicleData vehicleData;

    [Header("Navigation")]
    private Queue<Vector3> _activePath;
    private Vector3 _currentWaypoint;
    private bool _isMoving = false;

    // Movement smoothing
    private float _currentSpeed = 0f;
    private float _acceleration = 12f;
    private float _deceleration = 15f;

    private Vector3[] _cachedOutboundPath;
    private Vector3[] _cachedInboundPath;
    private Building _cachedTargetBuilding;
    private RoadNetwork _activeNetwork;

    public void Initialize(Building home, VehicleData data)
    {
        homeBuilding = home;
        vehicleData = data;
        gameObject.SetActive(false); 
    }

    private void Update()
    {
        if (_isMoving) MoveAlongPath();
    }

    // --- NEW: LATE-BINDING PATHING ORIGIN ---
    public Vector3 GetPathingOrigin()
    {
        // If we are actively driving, our next logical starting point for a seamless new path 
        // is the waypoint we are currently heading towards.
        if (_isMoving && _activePath != null)
        {
            return _currentWaypoint;
        }
        return transform.position;
    }

    public void DispatchTo(Building target, RoadNetwork network)
    {
        if (_cachedTargetBuilding != target || _activeNetwork != network)
        {
            _cachedTargetBuilding = target;
            _activeNetwork = network;
            ClearPathCaches();
        }

        // Debug.Log("dispatch");

        targetBuilding = target;
        currentState = VehicleState.Outbound;
        
        Vector2Int exitPort = homeBuilding.GetClosestPort(PortType.Exit, _activeNetwork, targetBuilding.originCell);
        TeleportToPort(exitPort);
        gameObject.SetActive(true);

        if (_cachedOutboundPath == null || _cachedOutboundPath.Length == 0)
        {
            Vector2Int entryPort = targetBuilding.GetClosestPort(PortType.Entry, _activeNetwork, exitPort);
            Vector3 targetWorldPos = GetWorldPositionOfPort(entryPort);
            
            VehiclePathRequestManager.Instance.RequestPath(this, targetWorldPos, (newPath) => 
            {
                if (newPath != null && newPath.Count > 0)
                {
                    _cachedOutboundPath = newPath.ToArray();
                    _activePath = new Queue<Vector3>(_cachedOutboundPath);
                    StartDriving();
                }
                else
                {
                    RecallAndDeactivate();
                }
            });
        }
        else
        {
            _activePath = new Queue<Vector3>(_cachedOutboundPath);
            StartDriving();
        }
    }

    public void ReturnHome()
    {
        currentState = VehicleState.Inbound;

        // Debug.Log("returnhome");
        
        Vector2Int exitPort = targetBuilding.GetClosestPort(PortType.Exit, _activeNetwork, homeBuilding.originCell);
        TeleportToPort(exitPort);

        if (_cachedInboundPath == null || _cachedInboundPath.Length == 0)
        {
            Vector2Int entryPort = homeBuilding.GetClosestPort(PortType.Entry, _activeNetwork, exitPort);
            Vector3 targetWorldPos = GetWorldPositionOfPort(entryPort);
            
            VehiclePathRequestManager.Instance.RequestPath(this, targetWorldPos, (newPath) => 
            {
                if (newPath != null && newPath.Count > 0)
                {
                    _cachedInboundPath = newPath.ToArray();
                    _activePath = new Queue<Vector3>(_cachedInboundPath);
                    StartDriving();
                }
                else
                {
                    // Debug.Log("nopath");
                    RecallAndDeactivate();
                }
            });
        }
        else
        {
            _activePath = new Queue<Vector3>(_cachedInboundPath);
            StartDriving();
        }
    }

    public void Reroute(Building newTarget, RoadNetwork network)
    {
        ClearPathCaches(); 

        if (currentState == VehicleState.Idle || !gameObject.activeSelf)
        {
            if (newTarget != null) DispatchTo(newTarget, network);
            return;
        }

        targetBuilding = newTarget;
        _activeNetwork = network; 
        Vector2Int destinationPort;

        Vector2Int currentCell = new Vector2Int(
            Mathf.FloorToInt(transform.position.x / WorldManager.Instance.cellSize), 
            Mathf.FloorToInt(transform.position.z / WorldManager.Instance.cellSize)
        );

        if (currentState == VehicleState.Outbound)
        {
            if (targetBuilding != null) destinationPort = targetBuilding.GetClosestPort(PortType.Entry, _activeNetwork, currentCell);
            else
            {
                currentState = VehicleState.Inbound;
                destinationPort = homeBuilding.GetClosestPort(PortType.Entry, _activeNetwork, currentCell);
            }
        }
        else 
        {
            destinationPort = homeBuilding.GetClosestPort(PortType.Entry, _activeNetwork, currentCell);
        }

        Vector3 targetWorldPos = GetWorldPositionOfPort(destinationPort);
        
        VehiclePathRequestManager.Instance.RequestPath(this, targetWorldPos, (newPath) => 
        {
            if (newPath != null && newPath.Count > 0)
            {
                _activePath = newPath;
                _currentWaypoint = _activePath.Dequeue(); 
                _isMoving = true;
            }
            else
            {
                Debug.LogWarning($"{vehicleData.vehicleName} path obliterated. Emergency reset to home.");
                RecallAndDeactivate();
                if (targetBuilding != null) DispatchTo(targetBuilding, _activeNetwork);
            }
        });
    }

    public void RecallAndDeactivate()
    {
        currentState = VehicleState.Idle;
        targetBuilding = null;
        _cachedTargetBuilding = null;
        _isMoving = false;
        ClearPathCaches();

        _currentSpeed = 0f;
        StopAllCoroutines();
        gameObject.SetActive(false); 
    }

    // --- MOVEMENT LOGIC ---

    private void StartDriving()
    {
        if (_activePath != null && _activePath.Count > 0)
        {
            _currentWaypoint = _activePath.Dequeue();
            _isMoving = true;
        }
        else
        {
            Debug.LogWarning($"{vehicleData.vehicleName} pathfinding failed. Cannot reach target.");
            RecallAndDeactivate();
        }
    }

    private void MoveAlongPath()
    {
        float targetSpeed = vehicleData.maximumVehicleSpeed;

        if (_activePath.Count == 0)
        {
            float distToFinal = Vector3.Distance(transform.position, _currentWaypoint);
            targetSpeed = Mathf.Lerp(0f, vehicleData.maximumVehicleSpeed, distToFinal / 3.0f);
            if (targetSpeed < 1.0f) targetSpeed = 1.0f; 
        }

        float accelRate = (_currentSpeed < targetSpeed) ? _acceleration : _deceleration;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, accelRate * Time.deltaTime);

        float step = _currentSpeed * Time.deltaTime;
        transform.position = Vector3.MoveTowards(transform.position, _currentWaypoint, step);

        Vector3 direction = (_currentWaypoint - transform.position).normalized;
        if (direction != Vector3.zero && direction.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, Time.deltaTime * 500f); 
        }

        if (Vector3.Distance(transform.position, _currentWaypoint) < 0.05f)
        {
            if (_activePath.Count > 0) _currentWaypoint = _activePath.Dequeue();
            else
            {
                _isMoving = false;
                _currentSpeed = 0f; 
                OnDestinationReached();
            }
        }
    }

    private void OnDestinationReached()
    {
        if (currentState == VehicleState.Outbound) StartCoroutine(WaitAtTarget());
        else if (currentState == VehicleState.Inbound) StartCoroutine(WaitAtHome());
    }

    private IEnumerator WaitAtTarget()
    {
        yield return new WaitForSeconds(3f); 
        ReturnHome();
    }

    private IEnumerator WaitAtHome()
    {
        yield return new WaitForSeconds(3f);
        if (targetBuilding != null) DispatchTo(targetBuilding, _activeNetwork);
        else RecallAndDeactivate();
    }

    // --- HELPERS ---

    private void TeleportToPort(Vector2Int portCell)
    {
        transform.position = GetWorldPositionOfPort(portCell) + (Vector3.up * 0.2f); 
    }

    private Vector3 GetWorldPositionOfPort(Vector2Int portCell)
    {
        if (WorldManager.Instance == null) return transform.position;

        float x = portCell.x * WorldManager.Instance.cellSize + (WorldManager.Instance.cellSize * 0.5f);
        float z = portCell.y * WorldManager.Instance.cellSize + (WorldManager.Instance.cellSize * 0.5f);
        float y = WorldManager.Instance.GetPhysicalHeight(portCell.x + 0.5f, portCell.y + 0.5f) * WorldManager.Instance.heightStep;
        
        return new Vector3(x, y, z);
    }
    
    private void ClearPathCaches()
    {
        _cachedOutboundPath = null;
        _cachedInboundPath = null;
    }
}