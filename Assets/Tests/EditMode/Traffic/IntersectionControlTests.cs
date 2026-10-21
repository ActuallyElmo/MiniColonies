using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class IntersectionControlTests
{
    [Test]
    public void DefaultGroupsApplyToEachLaneOfOpposingRoads()
    {
        var controller = new TrafficLightIntersectionController();
        controller.Initialize(Vector2Int.zero, null, new[]
        {
            Movement(1, 0), Movement(1, 1), Movement(16, 0), Movement(16, 1),
            Movement(4, 0), Movement(4, 1), Movement(64, 0), Movement(64, 1)
        });
        Assert.IsTrue(controller.IsGreenForLane(1, 0));
        Assert.IsTrue(controller.IsGreenForLane(1, 1));
        Assert.IsTrue(controller.IsGreenForLane(16, 1));
        Assert.IsFalse(controller.IsGreenForLane(4, 0));
        Assert.IsFalse(controller.IsGreenForLane(4, 1));
        controller.Tick(IntersectionControlUtility.DefaultLightDurationSeconds);
        Assert.IsFalse(controller.IsGreenForLane(1, 1));
        Assert.IsTrue(controller.IsGreenForLane(4, 0));
        Assert.IsTrue(controller.IsGreenForLane(64, 1));
    }

    [Test]
    public void CustomIndicesSplitOneRoadAndGroupDifferentRoadsInNumericOrder()
    {
        var data = new IntersectionData(Vector2Int.zero);
        data.TrafficLightCycleSeconds = 5f;
        data.SetTrafficLightIndex(1, 0, 9);
        data.SetTrafficLightIndex(1, 1, 2);
        data.SetTrafficLightIndex(4, 0, 2);
        data.SetTrafficLightIndex(16, 0, 5);
        // Overrides for absent lanes must not create empty phases.
        data.SetTrafficLightIndex(64, 0, 0);
        var controller = new TrafficLightIntersectionController();
        controller.Initialize(Vector2Int.zero, data, new[]
        {
            Movement(1, 0), Movement(1, 1), Movement(4, 0), Movement(16, 0)
        });
        Assert.AreEqual(2, controller.ActiveSignalIndex);
        Assert.IsTrue(controller.IsGreenForLane(1, 1));
        Assert.IsTrue(controller.IsGreenForLane(4, 0));
        Assert.IsFalse(controller.IsGreenForLane(1, 0));
        Assert.IsFalse(controller.IsGreenForLane(1, 3));
        controller.Tick(5f);
        Assert.AreEqual(5, controller.ActiveSignalIndex);
        controller.Tick(5f);
        Assert.AreEqual(9, controller.ActiveSignalIndex);
        Assert.IsTrue(controller.IsGreenForLane(1, 0));
        Assert.IsFalse(controller.IsGreenForLane(1, 1));
        controller.Tick(5f);
        Assert.AreEqual(2, controller.ActiveSignalIndex);
    }

    [TestCase(IntersectionRuleType.TrafficLight)]
    [TestCase(IntersectionRuleType.SmartTrafficLight)]
    public void SharedGreenIndexStillBlocksConflictingMovementsAndRedSiblingLane(IntersectionRuleType mode)
    {
        var firstObject = new GameObject("First signal test vehicle");
        var secondObject = new GameObject("Second signal test vehicle");
        var conveyorObject = new GameObject("Signal test conveyor");
        try
        {
            conveyorObject.AddComponent<ConveyorTrafficManager>();
            var first = firstObject.AddComponent<VehicleAI>();
            var second = secondObject.AddComponent<VehicleAI>();
            var data = new IntersectionData(Vector2Int.zero);
            data.RuleType = mode;
            data.SetTrafficLightIndex(1, 0, 0);
            data.SetTrafficLightIndex(1, 1, 9);
            data.SetTrafficLightIndex(4, 0, 0);
            var a = Movement(1, 0);
            a.waypoints.Add(Vector3.left);
            a.waypoints.Add(Vector3.right);
            var b = Movement(4, 0);
            b.waypoints.Add(Vector3.back);
            b.waypoints.Add(Vector3.forward);
            var red = Movement(1, 1);
            var approach = new TrafficEdge(new TrafficNode(Vector3.zero),
                new TrafficNode(Vector3.one), 1f);
            second.currentEdge = approach;
            var controller = new TrafficLightIntersectionController();
            controller.Initialize(Vector2Int.zero, data, new[] { a, b, red });
            Assert.IsTrue(controller.CanEnter(second, approach, b, null));
            Assert.IsFalse(controller.CanEnter(second, approach, red, null),
                "Another green lane on the same road must not admit a red lane.");
            controller.NotifyEntered(first, a);
            // Clear the earlier reservation so this exercises the active conflict.
            controller.Tick(0f);
            controller.Tick(0f);
            Assert.IsFalse(controller.CanEnter(second, approach, b, null));
            controller.NotifyExited(first, a);
            Assert.IsTrue(controller.CanEnter(second, approach, b, null));
        }
        finally
        {
            Object.DestroyImmediate(firstObject);
            Object.DestroyImmediate(secondObject);
            Object.DestroyImmediate(conveyorObject);
        }
    }

    [Test]
    public void SignalIndexEditsValidateInputsAndNotifyOnceWithoutChangingConnections()
    {
        var data = new IntersectionData(Vector2Int.zero);
        data.AddCustomRule(1, 0, 4, 0);
        int changes = 0;
        System.Action<IntersectionData> changed = _ => changes++;
        data.Changed += changed;
        try
        {
            Assert.IsFalse(data.SetTrafficLightIndex(3, 0, 0));
            Assert.IsFalse(data.SetTrafficLightIndex(1, -1, 0));
            Assert.IsFalse(data.SetTrafficLightIndex(1, 0, -1));
            Assert.IsFalse(data.SetTrafficLightIndex(1, 0, 10));
            Assert.AreEqual(0, changes);
            Assert.IsTrue(data.SetTrafficLightIndex(1, 0, 9));
            Assert.IsTrue(data.SetTrafficLightIndex(1, 0, 9));
            Assert.AreEqual(1, changes);
            Assert.IsTrue(data.SetTrafficLightIndex(1, 0, 0));
            Assert.AreEqual(2, changes);
            Assert.AreEqual(1, data.TrafficLightIndices.Count);
            Assert.AreEqual(1, data.CustomRules.Count);
        }
        finally { data.Changed -= changed; }
    }

    [Test]
    public void SingleIndexEmptyMovementsAndInvalidTimeAreHandledSafely()
    {
        var data = new IntersectionData(Vector2Int.zero);
        data.SetTrafficLightIndex(1, 0, 9);
        var controller = new TrafficLightIntersectionController();
        controller.Initialize(Vector2Int.zero, data, new TrafficEdge[0],
            new[] { new LaneConnectionSource(1, 0) });
        controller.Tick(1000f);
        Assert.AreEqual(9, controller.ActiveSignalIndex);
        Assert.IsTrue(controller.IsGreenForLane(1, 0));
        controller.Initialize(Vector2Int.zero, data, new TrafficEdge[0]);
        Assert.AreEqual(-1, controller.ActiveSignalIndex);
        Assert.IsFalse(controller.IsGreenForLane(1, 0));
        controller.Initialize(Vector2Int.zero, null, new[] { Movement(1), Movement(4) });
        controller.Tick(float.NaN);
        controller.Tick(float.PositiveInfinity);
        controller.Tick(-1f);
        Assert.AreEqual(0, controller.ActiveSignalIndex);
        controller.Tick(30f * 10001 + 2f);
        Assert.AreEqual(1, controller.ActiveSignalIndex);
        Assert.AreEqual(2f / 30f, controller.PhaseProgress, 0.0001f);
    }

    [Test]
    public void RebuildRestoresIndexAndProgressWhenGroupMembershipChanges()
    {
        var data = new IntersectionData(Vector2Int.zero);
        data.TrafficLightCycleSeconds = 10f;
        data.SetTrafficLightIndex(1, 0, 2);
        data.SetTrafficLightIndex(1, 1, 9);
        var edges = new[] { Movement(1, 0), Movement(1, 1) };
        var original = new TrafficLightIntersectionController();
        original.Initialize(Vector2Int.zero, data, edges);
        original.Tick(16f);
        data.SetTrafficLightIndex(1, 0, 9);
        data.TrafficLightCycleSeconds = 20f;
        var rebuilt = new TrafficLightIntersectionController();
        rebuilt.Initialize(Vector2Int.zero, data, edges);
        Assert.IsTrue(rebuilt.RestorePhaseFrom(original));
        Assert.AreEqual(9, rebuilt.ActiveSignalIndex);
        Assert.AreEqual(0.6f, rebuilt.PhaseProgress, 0.0001f);
        data.SetTrafficLightIndex(1, 0, 0);
        data.SetTrafficLightIndex(1, 1, 0);
        rebuilt.Initialize(Vector2Int.zero, data, edges);
        Assert.IsFalse(rebuilt.RestorePhaseFrom(original));
        Assert.AreEqual(0, rebuilt.ActiveSignalIndex);
        Assert.AreEqual(0f, rebuilt.PhaseProgress);
    }

    [Test]
    public void LanePhaseChangePublishesEvenWhenItsRoadDirectionMaskIsUnchanged()
    {
        var backendObject = new GameObject("Lane phase notification backend");
        var backend = backendObject.AddComponent<TrafficSystemBackend>();
        int changedIndex = -1;
        System.Action<Vector2Int, int> changed = (cell, index) => changedIndex = index;
        backend.OnTrafficLightStateChanged += changed;
        try
        {
            var data = new IntersectionData(Vector2Int.zero);
            data.TrafficLightCycleSeconds = 5f;
            data.SetTrafficLightIndex(1, 0, 0);
            data.SetTrafficLightIndex(1, 1, 9);
            var controller = new TrafficLightIntersectionController();
            controller.Initialize(Vector2Int.zero, data, new[] { Movement(1, 0), Movement(1, 1) });
            var controllers = (Dictionary<Vector2Int, IIntersectionController>)typeof(TrafficSystemBackend)
                .GetField("_intersectionControllers", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(backend);
            controllers.Add(Vector2Int.zero, controller);
            backend.TickIntersectionControllers(5f);
            Assert.AreEqual(1, controller.ActiveDirectionMask);
            Assert.AreEqual(9, changedIndex);
        }
        finally
        {
            backend.OnTrafficLightStateChanged -= changed;
            Object.DestroyImmediate(backendObject);
        }
    }

    [Test]
    public void TrafficLightsAlternateOpposingApproachesAtConfiguredDuration()
    {
        var data = new IntersectionData(Vector2Int.zero);
        data.TrafficLightCycleSeconds = 10f;
        var controller = new TrafficLightIntersectionController();
        controller.Initialize(Vector2Int.zero, data, new List<TrafficEdge>
        {
            Movement(1), Movement(16), Movement(4), Movement(64)
        });

        Assert.AreEqual(1 | 16, controller.ActiveDirectionMask);
        Assert.IsTrue(controller.IsGreenForDirection(1));
        Assert.IsFalse(controller.IsGreenForDirection(4));
        controller.Tick(9.9f);
        Assert.AreEqual(1 | 16, controller.ActiveDirectionMask);
        controller.Tick(0.2f);
        Assert.AreEqual(4 | 64, controller.ActiveDirectionMask);
    }

    [Test]
    public void RebuiltTrafficLightsKeepTheirPhaseAndRelativeProgress()
    {
        var originalData = new IntersectionData(Vector2Int.zero);
        originalData.TrafficLightCycleSeconds = 10f;
        var original = new TrafficLightIntersectionController();
        original.Initialize(Vector2Int.zero, originalData, new List<TrafficEdge>
        {
            Movement(1), Movement(16), Movement(4), Movement(64)
        });
        original.Tick(6f);

        var rebuiltData = new IntersectionData(Vector2Int.zero);
        rebuiltData.TrafficLightCycleSeconds = 20f;
        var rebuilt = new TrafficLightIntersectionController();
        rebuilt.Initialize(Vector2Int.zero, rebuiltData, new List<TrafficEdge>
        {
            Movement(1), Movement(16), Movement(4), Movement(64)
        });

        Assert.IsTrue(rebuilt.RestorePhaseFrom(original));
        Assert.AreEqual(original.ActiveDirectionMask, rebuilt.ActiveDirectionMask);
        Assert.AreEqual(0.6f, rebuilt.PhaseProgress, 0.0001f);
        rebuilt.Tick(7.9f);
        Assert.AreEqual(1 | 16, rebuilt.ActiveDirectionMask);
        rebuilt.Tick(0.2f);
        Assert.AreEqual(4 | 64, rebuilt.ActiveDirectionMask);
    }

    [Test]
    public void PriorityRoadUsesItsConfiguredApproachPair()
    {
        var data = new IntersectionData(Vector2Int.zero);
        data.SetPriorityDirections(2, 32);
        var controller = new PriorityRoadIntersectionController();
        controller.Initialize(Vector2Int.zero, data, new[] { Movement(2), Movement(32), Movement(4) });

        Assert.IsTrue(controller.IsPriorityDirection(2));
        Assert.IsTrue(controller.IsPriorityDirection(32));
        Assert.IsFalse(controller.IsPriorityDirection(4));
    }

    [Test]
    public void ControlSettingsValidateDurationsAndOppositeDirections()
    {
        var data = new IntersectionData(Vector2Int.zero);
        data.TrafficLightCycleSeconds = -20f;
        Assert.AreEqual(
            IntersectionControlUtility.MinimumLightDurationSeconds,
            data.TrafficLightCycleSeconds);
        data.TrafficLightCycleSeconds = float.NaN;
        Assert.AreEqual(
            IntersectionControlUtility.DefaultLightDurationSeconds,
            data.TrafficLightCycleSeconds);
        Assert.AreEqual(16, IntersectionControlUtility.OppositeDirectionBit(1));
        Assert.AreEqual(32, IntersectionControlUtility.FindMostOppositeDirection(
            2,
            new[] { 4, 32, 64 }));
    }

    [Test]
    public void SignalPopCurveOvershootsThenSettles()
    {
        Assert.AreEqual(0f, IntersectionTrafficLightOverlay.Smooth01(-1f));
        Assert.AreEqual(0.5f, IntersectionTrafficLightOverlay.Smooth01(0.5f), 0.0001f);
        Assert.AreEqual(1f, IntersectionTrafficLightOverlay.Smooth01(2f));
        Assert.AreEqual(0f, IntersectionTrafficLightOverlay.PopIn01(0f, 1f));
        Assert.Greater(IntersectionTrafficLightOverlay.PopIn01(0.7f, 1f), 1f);
        Assert.AreEqual(1f, IntersectionTrafficLightOverlay.PopIn01(1f, 1f));
    }

    [Test]
    public void IndexGradientUsesTheFullZeroToNineRangeAndHoverLightensWithoutChangingAlpha()
    {
        var style = IntersectionBuilderStyle.Default;
        style.trafficLightIndexHoverLightness = 0.25f;
        style.trafficLightIndexColorGradient.SetKeys(new[]
        {
            new GradientColorKey(new Color(0.1f, 0.2f, 0.3f), 0f),
            new GradientColorKey(new Color(0.7f, 0.6f, 0.5f), 1f)
        }, new[] { new GradientAlphaKey(0.4f, 0f), new GradientAlphaKey(0.8f, 1f) });
        Assert.AreEqual(style.trafficLightIndexColorGradient.Evaluate(0f),
            style.GetTrafficLightIndexColor(0));
        Assert.AreEqual(style.trafficLightIndexColorGradient.Evaluate(1f),
            style.GetTrafficLightIndexColor(9));
        Assert.AreEqual(style.trafficLightIndexColorGradient.Evaluate(4f / 9f),
            style.GetTrafficLightIndexColor(4));
        Assert.AreEqual(style.GetTrafficLightIndexColor(0), style.GetTrafficLightIndexColor(-1));
        Assert.AreEqual(style.GetTrafficLightIndexColor(9), style.GetTrafficLightIndexColor(10));
        Color color = style.GetTrafficLightIndexColor(4);
        Color hovered = style.GetTrafficLightIndexColor(4, true);
        Assert.AreEqual(Color.Lerp(color, new Color(1f, 1f, 1f, color.a), 0.25f), hovered);
        Assert.Greater(hovered.r, color.r);
        Assert.Greater(hovered.g, color.g);
        Assert.Greater(hovered.b, color.b);
        Assert.AreEqual(color.a, hovered.a);
        Assert.AreEqual(color, style.GetTrafficLightIndexColor(4),
            "Hover must not modify the authored gradient.");
    }

    [Test]
    public void TrafficLightSignalUsesAnExistingMarkerSpriteAsFallback()
    {
        var texture = new Texture2D(2, 2);
        Sprite entry = Sprite.Create(
            texture,
            new Rect(0f, 0f, 2f, 2f),
            new Vector2(0.5f, 0.5f));
        try
        {
            IntersectionBuilderStyle style = IntersectionBuilderStyle.Default;
            style.entrySprite = entry;
            Assert.AreSame(entry, style.GetTrafficLightSignalSprite());
        }
        finally
        {
            Object.DestroyImmediate(entry);
            Object.DestroyImmediate(texture);
        }
    }

    private static TrafficEdge Movement(int fromDirectionBit, int laneIndex = 0)
    {
        var edge = new TrafficEdge(
            new TrafficNode(Vector3.zero),
            new TrafficNode(Vector3.one),
            1f,
            true);
        edge.fromDirectionBit = fromDirectionBit;
        edge.fromLaneIndex = laneIndex;
        return edge;
    }
}
