using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class CornerSpeedPlannerTests
{
    [Test]
    public void StraightRoadAndHillsRetainCruisingSpeed()
    {
        var edge = Edge(Vector3.zero, new Vector3(0f, 1f, 2f), new Vector3(0f, 0f, 4f));
        Assert.AreEqual(4f, Speed(new[] { edge }, 0, 1f));
    }

    [Test]
    public void TightCornerBrakesInAdvanceThenReleasesAfterTheTurn()
    {
        var edge = Edge(Vector3.zero, Vector3.forward * 4f, new Vector3(4f, 0f, 4f));
        Assert.AreEqual(4f, Speed(new[] { edge }, 0, 0f));
        Assert.Less(Speed(new[] { edge }, 0, 3f), 4f);
        Assert.AreEqual(2.8f, Speed(new[] { edge }, 0, 4f), 0.001f);
        Assert.AreEqual(4f, Speed(new[] { edge }, 0, 5f));
    }

    [Test]
    public void BroadCurveIsFasterThanTheSameAngleInASmallSpace()
    {
        var broad = Arc(4f, 24);
        var tight = Arc(0.3f, 24);
        Assert.AreEqual(4f, Speed(new[] { broad }, 0, broad.totalLength * 0.5f), 0.01f);
        Assert.AreEqual(2.8f, Speed(new[] { tight }, 0, tight.totalLength * 0.5f), 0.01f);
        Assert.AreEqual(Speed(new[] { Arc(0.3f, 12) }, 0, 0.2f),
            Speed(new[] { tight }, 0, 0.2f), 0.01f, "Curve tessellation should not change a tight turn's target.");
    }

    [Test]
    public void CornersAcrossEdgeBoundariesIncludeApproachAndExit()
    {
        var approach = Edge(Vector3.zero, Vector3.forward * 4f);
        var exit = Edge(Vector3.forward * 4f, new Vector3(4f, 0f, 4f));
        var route = new[] { approach, exit };
        Assert.Less(Speed(route, 0, 3f), 4f);
        Assert.AreEqual(2.8f, Speed(route, 1, 0.05f), 0.001f);
        Assert.AreEqual(4f, Speed(route, 1, 1f));
    }

    [Test]
    public void RebuildingGeometryClearsOldCornersAndDuplicatePointsAreSafe()
    {
        var edge = Edge(Vector3.zero, Vector3.forward, Vector3.forward,
            Vector3.forward + Vector3.right);
        Assert.AreEqual(2.8f, Speed(new[] { edge }, 0, 1f), 0.001f);
        edge.waypoints = new List<Vector3> { Vector3.zero, Vector3.forward * 4f };
        edge.RecalculateLength();
        Assert.AreEqual(4f, Speed(new[] { edge }, 0, 1f));
        edge.waypoints.Clear();
        edge.RecalculateLength();
        Assert.AreEqual(4f, Speed(new[] { edge }, 0, 0f));
    }

    [Test]
    public void InvalidInputsAndDisabledSlowdownStaySafe()
    {
        Assert.AreEqual(0f, CornerSpeedPlanner.GetDesiredSpeed(null, 0, 0f, 4f, 4f, 20f));
        var route = new[] { Arc(0.3f, 12) };
        Assert.AreEqual(4f, CornerSpeedPlanner.GetDesiredSpeed(route, 0, 0.2f, 4f, 4f, 20f, 0f));
        Assert.AreEqual(2.8f, CornerSpeedPlanner.GetDesiredSpeed(route, 0, 0.2f, 4f,
            float.NaN, float.NaN, float.NaN), 0.01f);
        Assert.AreEqual(0f, CornerSpeedPlanner.GetDesiredSpeed(route, 0, 0f, float.NaN, 4f, 20f));
    }

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    [TestCase(4f)]
    public void FixedSimulationStepsSlowThenAccelerateAtEverySpeedAndPause(float speedMultiplier)
    {
        var route = new[] { Edge(Vector3.zero, Vector3.forward * 4f, new Vector3(12f, 0f, 4f)) };
        var clock = new TrafficSimulationClock(0.05f, 16);
        float distance = 0f, speed = 4f, acceleration = 0f;
        float speedAtCorner = 4f;
        bool reachedCorner = false;
        // Change speed during the approach; feed explicit simulation deltas as the conveyor does.
        for (int frame = 0; frame < 120; frame++)
        {
            float multiplier = frame < 5 ? 1f : frame < 10 ? 0f : speedMultiplier;
            int steps = clock.Accumulate(0.05f * multiplier);
            if (multiplier == 0f) Assert.AreEqual(0, steps);
            for (int i = 0; i < steps; i++)
            {
                var control = LongitudinalController.Step(new LongitudinalControlInput(
                    speed, Speed(route, 0, distance), 0.05f, acceleration, 4f, 4f, 8f, 20f));
                speed = control.SpeedUnitsPerSecond;
                acceleration = control.AccelerationUnitsPerSecondSquared;
                distance += speed * 0.05f;
                if (!reachedCorner && distance >= 4f)
                {
                    reachedCorner = true;
                    speedAtCorner = speed;
                }
            }
        }
        Assert.IsTrue(reachedCorner);
        Assert.Less(speedAtCorner, 3.8f);
        Assert.Greater(speed, speedAtCorner);
        Assert.Greater(distance, 6f);
    }

    private static float Speed(IReadOnlyList<TrafficEdge> route, int index, float distance) =>
        CornerSpeedPlanner.GetDesiredSpeed(route, index, distance, 4f, 4f, 20f);

    private static TrafficEdge Arc(float radius, int segments)
    {
        var points = new Vector3[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 0.5f / segments;
            points[i] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }
        return Edge(points);
    }

    private static TrafficEdge Edge(params Vector3[] points)
    {
        var edge = new TrafficEdge(new TrafficNode(points[0]), new TrafficNode(points[points.Length - 1]), 4f)
        { waypoints = new List<Vector3>(points) };
        edge.RecalculateLength();
        return edge;
    }
}
