param(
    [string]$WorkspaceRoot,
    [switch]$AsJson
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
    $WorkspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
}
else {
    $WorkspaceRoot = (Resolve-Path -LiteralPath $WorkspaceRoot).Path
}

$findings = [System.Collections.Generic.List[object]]::new()

function Add-Finding {
    param(
        [string]$Id,
        [string]$Category,
        [string]$Observed,
        [string]$Evidence
    )

    $findings.Add([pscustomobject]@{
        Id = $Id
        Category = $Category
        Observed = $Observed
        Evidence = $Evidence
    })
}

function Read-WorkspaceFile {
    param([string]$RelativePath)

    $path = Join-Path $WorkspaceRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required baseline file is missing: $RelativePath"
    }

    return Get-Content -LiteralPath $path -Raw
}

function Get-TransitionTargets {
    param(
        [int]$IncomingLaneCount,
        [int]$OutgoingLaneCount
    )

    $targets = [System.Collections.Generic.List[int]]::new()
    for ($i = 0; $i -lt $IncomingLaneCount; $i++) {
        if ($IncomingLaneCount -eq $OutgoingLaneCount) {
            $target = [Math]::Min([Math]::Max($i, 0), $OutgoingLaneCount - 1)
        }
        elseif ($IncomingLaneCount -gt $OutgoingLaneCount) {
            $target = [Math]::Floor($i * ($OutgoingLaneCount / [double]$IncomingLaneCount))
            $target = [Math]::Min([Math]::Max([int]$target, 0), $OutgoingLaneCount - 1)
        }
        else {
            $scale = $OutgoingLaneCount / [double]$IncomingLaneCount
            $target = [Math]::Round(($i + 0.5) * $scale - 0.5)
            $target = [Math]::Min([Math]::Max([int]$target, 0), $OutgoingLaneCount - 1)
        }

        $targets.Add($target)
    }

    return $targets
}

$roadBackend = Read-WorkspaceFile 'Assets\Scripts\Roads\RoadSystemBackend.cs'
$trafficGeneration = Read-WorkspaceFile 'Assets\Scripts\Roads\Traffic\TrafficGenerationTask.cs'
$trafficNetwork = Read-WorkspaceFile 'Assets\Scripts\Roads\Traffic\TrafficNetworkInfo.cs'
$trafficSystem = Read-WorkspaceFile 'Assets\Scripts\Roads\Traffic\TrafficSystemBackend.cs'
$conveyor = Read-WorkspaceFile 'Assets\Scripts\Roads\Traffic\ConveyorTrafficManager.cs'
$vehicleAi = Read-WorkspaceFile 'Assets\Scripts\Vehicles\VehicleAI\VehicleAI.cs'
$pathfindingTask = Read-WorkspaceFile 'Assets\Scripts\Vehicles\VehicleAI\VehiclePathfindingTask.cs'
$pathfindingJob = Read-WorkspaceFile 'Assets\Scripts\Vehicles\VehicleAI\VehiclePathfindingJob.cs'
$scene = Read-WorkspaceFile 'Assets\Scenes\SampleScene.unity'

$stallMatch = [regex]::Match(
    $conveyor,
    'TrafficStuckSecondsBeforeRespawn\s*=\s*(?<value>[0-9.]+)f')
$stallValue = if ($stallMatch.Success) { $stallMatch.Groups['value'].Value } else { 'not found' }
Add-Finding `
    'B001' `
    'Policy drift' `
    "Prototype traffic-stall recovery is $stallValue seconds; resolved decision D17 is 30 seconds." `
    'ConveyorTrafficManager.cs: TrafficStuckSecondsBeforeRespawn'

$hasFarEndpointRegistration =
    $trafficGeneration -match 'RegisterOutgoingNode\(startCell,[\s\S]*?endCell\)' -and
    $trafficGeneration -match 'RegisterIncomingNode\(endCell,[\s\S]*?startCell\)'
$usesEndpointAsDirectionNeighbor =
    $trafficGeneration -match 'GetDirectionBit\(transitionCell,\s*inLeg\.NeighborCell\)' -and
    $trafficGeneration -match 'GetDirectionBit\(intersectionCell,\s*inLeg\.NeighborCell\)'
$directionHelperRejectsUnknownDelta = $roadBackend -match 'return 0;'
Add-Finding `
    'B002' `
    'Topology risk' `
    "Far road-section endpoints are registered as NeighborCell=$hasFarEndpointRegistration and later used by adjacent-cell direction lookup=$usesEndpointAsDirectionNeighbor; unknown deltas return zero=$directionHelperRejectsUnknownDelta." `
    'TrafficGenerationTask.cs endpoint registration and direction metadata; RoadSystemBackend.GetDirectionBit'

$fourToTwo = (Get-TransitionTargets 4 2) -join ','
$twoToFour = (Get-TransitionTargets 2 4) -join ','
Add-Finding `
    'B003' `
    'Transition mapping' `
    "Current primary index mapping is 4->2 [$fourToTwo] and 2->4 [$twoToFour] before optional adjacent expansion connectors." `
    'TrafficGenerationTask.MapTransitionLanes'

$laneLengthMatch = [regex]::Match($trafficGeneration, 'LaneChangeLength\s*=\s*(?<value>[0-9.]+)f')
$laneSpacingMatch = [regex]::Match($trafficGeneration, 'LaneChangeOpportunitySpacing\s*=\s*(?<value>[0-9.]+)f')
$laneLength = if ($laneLengthMatch.Success) { $laneLengthMatch.Groups['value'].Value } else { 'not found' }
$laneSpacing = if ($laneSpacingMatch.Success) { $laneSpacingMatch.Groups['value'].Value } else { 'not found' }
Add-Finding `
    'B004' `
    'Lane behavior' `
    "Lane changes are discrete generated graph movements with nominal length $laneLength and opportunity spacing $laneSpacing; pathfinding commits them into ManagedEdges." `
    'TrafficGenerationTask.BuildLaneChangeWindows/CreateLaneChangeEdge; TrafficRoute.ManagedEdges'

$hasTacticalPlanner = $vehicleAi -match 'TacticalLanePlanner'
$hasConveyorFeatureFlag = $vehicleAi -match 'useConveyorMovement'
$emptyUpdate = $vehicleAi -match 'private\s+void\s+Update\(\)\s*\{\s*\}'
Add-Finding `
    'B005' `
    'Movement ownership' `
    "Tactical planner present=$hasTacticalPlanner; conveyor feature flag present=$hasConveyorFeatureFlag; VehicleAI.Update is empty=$emptyUpdate. Conveyor movement is the only active vehicle-motion path." `
    'VehicleAI.cs'

$usesManagedOccupants =
    $trafficNetwork -match 'List<VehicleAI>\s+occupants' -and
    $conveyor -match 'edge\.occupants'
$usesInstanceIdOrdering = $conveyor -match 'GetInstanceID\(\)'
$usesFrameDelta = $conveyor -match 'Tick\(Time\.deltaTime\)'
Add-Finding `
    'B006' `
    'Runtime ownership' `
    "Managed TrafficEdge occupants=$usesManagedOccupants; instance-ID tie-break=$usesInstanceIdOrdering; render-frame delta tick=$usesFrameDelta." `
    'TrafficNetworkInfo.cs and ConveyorTrafficManager.cs'

$usesMovingLeaderSpeed =
    $conveyor -match 'sameEdgeVehicle\.conveyorCurrentSpeed' -and
    $conveyor -match 'candidate\.conveyorCurrentSpeed'
$usesStoppingEnvelope = $conveyor -match 'Mathf\.Sqrt\('
$usesSpeedMoveTowards = $conveyor -match 'Mathf\.MoveTowards\(vehicle\.conveyorCurrentSpeed'
$hasJerkLimit = $conveyor -match '(?i)\bjerk\b'
$hasTimeHeadway = $conveyor -match '(?i)timeHeadway|time headway'
Add-Finding `
    'B007' `
    'Longitudinal control' `
    "Moving-leader speed considered=$usesMovingLeaderSpeed; stopping envelope=$usesStoppingEnvelope; speed MoveTowards=$usesSpeedMoveTowards; jerk limit=$hasJerkLimit; time headway=$hasTimeHeadway." `
    'ConveyorTrafficManager.TickVehicle/TryGetFollowingStopDistance'

$usesExactPortCells =
    $pathfindingTask -match '_startPortCell' -and
    $pathfindingTask -match '_targetPortCell' -and
    $pathfindingTask -match 'GetDepartureEdgesFromPortCell' -and
    $pathfindingTask -match 'GetArrivalEdgesToPortCell'
$hasSpatialIndex =
    $trafficSystem -match 'TrafficSpatialIndex' -and
    $trafficSystem -match '_spatialIndex\.GetClosestLanes' -and
    $pathfindingTask -match 'TryGetDepartureEdgesFromPortCell' -and
    $pathfindingTask -match 'TryGetArrivalEdgesToPortCell'
$portFallbackScansAllEdges =
    -not $hasSpatialIndex -and
    $pathfindingTask -match 'TrafficSystemBackend\.Instance\.allEdges'
$closestLaneScansAllEdges =
    -not $hasSpatialIndex -and
    $trafficSystem -match 'foreach\s*\(TrafficEdge edge in allEdges\)'
Add-Finding `
    'B008' `
    'Port and spatial lookup' `
    "Exact start/target port identity carried=$usesExactPortCells; spatial index primary path present=$hasSpatialIndex; port fallback scans all managed edges=$portFallbackScansAllEdges; closest-lane lookup scans all edges=$closestLaneScansAllEdges." `
    'VehiclePathfindingTask.cs and TrafficSystemBackend.cs'

$usesExactEdgeRoute =
    $pathfindingJob -match 'ResultEdgeIndices' -and
    $pathfindingTask -match 'ManagedEdges\.Add'
$hasDynamicOccupancySnapshot = $pathfindingJob -match '(?i)occupancy|congestion|waitTime'
Add-Finding `
    'B009' `
    'Routing' `
    "A* returns an exact edge sequence=$usesExactEdgeRoute; dynamic occupancy/congestion input present=$hasDynamicOccupancySnapshot." `
    'VehiclePathfindingJob.cs and VehiclePathfindingTask.cs'

$hasPenaltyBasedIntersectionFallback =
    $pathfindingJob -match 'sloppy intersection merge' -and
    $pathfindingJob -match 'totalEdgeCost\s*\+=\s*15\.0f'
Add-Finding `
    'B011' `
    'Legality contract' `
    "Pathfinding still contains a high-cost intersection fallback=$hasPenaltyBasedIntersectionFallback. This conflicts with D13 even if current edge metadata makes the branch uncommon or unreachable." `
    'VehiclePathfindingJob.cs intersection fallback branch'

$conveyorMeta = Read-WorkspaceFile 'Assets\Scripts\Roads\Traffic\ConveyorTrafficManager.cs.meta'
$conveyorGuidMatch = [regex]::Match($conveyorMeta, 'guid:\s*(?<guid>[0-9a-f]+)')
$conveyorGuid = if ($conveyorGuidMatch.Success) { $conveyorGuidMatch.Groups['guid'].Value } else { '' }
$conveyorInScene = -not [string]::IsNullOrEmpty($conveyorGuid) -and $scene.Contains($conveyorGuid)
Add-Finding `
    'B010' `
    'Scene dependency' `
    "ConveyorTrafficManager component is present in SampleScene=$conveyorInScene." `
    'ConveyorTrafficManager.cs.meta GUID and SampleScene.unity'

if ($AsJson) {
    $findings | ConvertTo-Json -Depth 4
}
else {
    $findings | Format-Table -AutoSize -Wrap
}
