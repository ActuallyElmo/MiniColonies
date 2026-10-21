using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class TrafficNodeViewGeometryTests
{
    [Test]
    public void StyledLaneUsesOuterLineAndBranchColoredInnerLine()
    {
        var style = TrafficNodeViewStyle.Default;
        style.lineWidth = 0.4f;
        style.innerLineWidth = 0.2f;
        style.heightOffset = 0.5f;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var colors = new List<Color>();

        TrafficNodeViewOverlay.AppendTrafficSegment(
            vertices,
            triangles,
            colors,
            Vector3.zero,
            Vector3.forward,
            Color.red,
            Color.blue,
            style.Validated());

        Assert.AreEqual(8, vertices.Count);
        Assert.AreEqual(12, triangles.Count);
        Assert.AreEqual(8, colors.Count);
        Assert.AreEqual(0.4f, Vector3.Distance(vertices[0], vertices[1]), 0.0001f);
        Assert.AreEqual(0.2f, Vector3.Distance(vertices[4], vertices[5]), 0.0001f);
        Assert.Greater(vertices[4].y, vertices[0].y);
        CollectionAssert.AreEqual(
            new[] { style.lineColor, style.lineColor, style.lineColor, style.lineColor,
                Color.red, Color.red, Color.blue, Color.blue },
            colors);
    }

    [Test]
    public void BranchColorIsSharedByAFlowAndChangesByDirectionAndBranch()
    {
        var style = TrafficNodeViewStyle.Default;
        Color forward = TrafficNodeViewOverlay.GetBranchColor(
            style, new RoadSectionId(10UL), TrafficLaneFlowDirection.SectionStartToEnd);
        Color sameBranch = TrafficNodeViewOverlay.GetBranchColor(
            style, new RoadSectionId(10UL), TrafficLaneFlowDirection.SectionStartToEnd);
        Color reverse = TrafficNodeViewOverlay.GetBranchColor(
            style, new RoadSectionId(10UL), TrafficLaneFlowDirection.SectionEndToStart);
        Color otherBranch = TrafficNodeViewOverlay.GetBranchColor(
            style, new RoadSectionId(11UL), TrafficLaneFlowDirection.SectionStartToEnd);

        Assert.AreEqual(forward, sameBranch);
        Assert.AreNotEqual(forward, reverse);
        Assert.AreNotEqual(forward, otherBranch);
        Assert.AreEqual(style.startToEndInnerColor.a, forward.a);
    }

    [Test]
    public void ZeroVariationUsesEachWaysExactBaseColor()
    {
        var style = TrafficNodeViewStyle.Default;
        style.startToEndInnerColor = Color.red;
        style.endToStartInnerColor = Color.blue;
        style.branchColorVariation = 0f;

        Color forward = TrafficNodeViewOverlay.GetBranchColor(
            style, new RoadSectionId(10UL), TrafficLaneFlowDirection.SectionStartToEnd);
        Color reverse = TrafficNodeViewOverlay.GetBranchColor(
            style, new RoadSectionId(10UL), TrafficLaneFlowDirection.SectionEndToStart);

        Assert.AreEqual(Color.red, forward);
        Assert.AreEqual(Color.blue, reverse);
    }

    [Test]
    public void OverlaySelectsRequestedMovementConnectionsOnly()
    {
        Assert.IsTrue(TrafficNodeViewOverlay.ShouldDisplayMovement(TrafficMovementKind.Intersection));
        Assert.IsTrue(TrafficNodeViewOverlay.ShouldDisplayMovement(TrafficMovementKind.MandatoryMerge));
        Assert.IsTrue(TrafficNodeViewOverlay.ShouldDisplayMovement(TrafficMovementKind.LaneExpansion));
        Assert.IsTrue(TrafficNodeViewOverlay.ShouldDisplayMovement(TrafficMovementKind.RoadEndUTurn));
        Assert.IsFalse(TrafficNodeViewOverlay.ShouldDisplayMovement(TrafficMovementKind.LaneContinuation));
        Assert.IsFalse(TrafficNodeViewOverlay.ShouldDisplayMovement(TrafficMovementKind.OptionalLaneChange));
    }

    [Test]
    public void AppearanceControlsLineAndArrowDimensionsAndColors()
    {
        var style = TrafficNodeViewStyle.Default;
        style.lineWidth = 0.4f;
        style.heightOffset = 0.8f;
        style.arrowLength = 1.2f;
        style.arrowWidth = 0.6f;
        style.lineColor = Color.red;
        style.arrowColor = Color.yellow;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var colors = new List<Color>();
        TrafficNodeViewOverlay.AppendSegment(vertices, triangles, Vector3.zero, Vector3.forward * 3f,
            true, style, colors);
        Assert.AreEqual(0.4f, Vector3.Distance(vertices[0], vertices[1]), 0.0001f);
        Assert.AreEqual(0.8f, vertices[0].y, 0.0001f);
        Assert.AreEqual(0.6f, Vector3.Distance(vertices[5], vertices[6]), 0.0001f);
        Assert.AreEqual(1.2f, Vector3.Distance(vertices[4], (vertices[5] + vertices[6]) * 0.5f), 0.0001f);
        Assert.AreEqual(Color.red, colors[0]);
        Assert.AreEqual(Color.yellow, colors[4]);
        style.showArrows = false;
        vertices.Clear(); triangles.Clear(); colors.Clear();
        TrafficNodeViewOverlay.AppendSegment(vertices, triangles, Vector3.zero, Vector3.forward,
            true, style, colors);
        Assert.AreEqual(4, vertices.Count);
        Assert.AreEqual(4, colors.Count);
    }

    [Test]
    public void InvalidAppearanceIsClampedAndNonFiniteValuesUseDefaults()
    {
        var style = TrafficNodeViewStyle.Default;
        style.lineWidth = -1f;
        style.heightOffset = -1f;
        style.arrowSpacing = 0f;
        style.arrowWidth = float.NaN;
        style.arrowLength = float.PositiveInfinity;
        style = style.Validated();
        Assert.Greater(style.lineWidth, 0f);
        Assert.AreEqual(0f, style.heightOffset);
        Assert.Greater(style.arrowSpacing, 0f);
        Assert.AreEqual(TrafficNodeViewStyle.Default.arrowWidth, style.arrowWidth);
        Assert.AreEqual(TrafficNodeViewStyle.Default.arrowLength, style.arrowLength);
    }

    [Test]
    public void LegacyAppearanceReceivesVisibleBranchDefaults()
    {
        var style = new TrafficNodeViewStyle
        {
            lineColor = Color.white,
            lineWidth = 0.2f,
            heightOffset = 0.1f,
            showArrows = true,
            arrowColor = Color.white,
            arrowLength = 0.4f,
            arrowWidth = 0.3f,
            arrowSpacing = 2f
        }.Validated();

        Assert.AreEqual(
            TrafficNodeViewStyle.Default.startToEndInnerColor,
            style.startToEndInnerColor);
        Assert.AreEqual(
            TrafficNodeViewStyle.Default.endToStartInnerColor,
            style.endToStartInnerColor);
        Assert.AreEqual(0.035f, style.innerLineWidth, 0.0001f);
        Assert.AreEqual(
            TrafficNodeViewStyle.Default.branchColorVariation,
            style.branchColorVariation);
    }

    [TestCase(1f)]
    [TestCase(-1f)]
    public void LaneRibbonFollowsHeightAndArrowPointsInTravelDirection(float direction)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        Vector3 from = new Vector3(0f, 2f, 0f);
        Vector3 to = new Vector3(direction * 3f, 3f, 0f);
        TrafficNodeViewOverlay.AppendSegment(vertices, triangles, from, to, true);
        Assert.AreEqual(7, vertices.Count);
        Assert.AreEqual(9, triangles.Count);
        Assert.AreEqual(2.12f, vertices[0].y, 0.0001f);
        Assert.AreEqual(3.12f, vertices[2].y, 0.0001f);
        Vector3 arrowBase = (vertices[5] + vertices[6]) * 0.5f;
        Assert.Greater(Vector3.Dot(vertices[4] - arrowBase, to - from), 0f);
        foreach (int index in triangles) Assert.That(index, Is.InRange(0, vertices.Count - 1));
    }

    [Test]
    public void InvalidOrDegenerateSegmentsDoNotCreateGeometry()
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        TrafficNodeViewOverlay.AppendSegment(vertices, triangles, Vector3.zero, Vector3.zero, true);
        TrafficNodeViewOverlay.AppendSegment(vertices, triangles, Vector3.zero, Vector3.up, true);
        TrafficNodeViewOverlay.AppendSegment(vertices, triangles, new Vector3(float.NaN, 0f, 0f), Vector3.one, true);
        TrafficNodeViewOverlay.AppendSegment(vertices, triangles, Vector3.zero, new Vector3(float.PositiveInfinity, 0f, 0f), true);
        Assert.IsEmpty(vertices);
        Assert.IsEmpty(triangles);
    }
}
