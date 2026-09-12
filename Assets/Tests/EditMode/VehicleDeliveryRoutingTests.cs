using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class VehicleDeliveryRoutingTests
{
    [Test]
    public void PriorityBalancesAgainstDistance()
    {
        Assert.That(VehicleDeliveryRouting.Score(100f, 9), Is.LessThan(VehicleDeliveryRouting.Score(75f, 5)));
        Assert.That(VehicleDeliveryRouting.Score(200f, 9), Is.GreaterThan(VehicleDeliveryRouting.Score(75f, 5)));
        Assert.That(VehicleDeliveryRouting.Score(100f, 1), Is.GreaterThan(VehicleDeliveryRouting.Score(100f, 5)));
        Assert.That(VehicleDeliveryRouting.Score(100f, 5), Is.EqualTo(100f));
    }

    [Test]
    public void RoadDistanceHandlesDetoursDirectionAndUnreachableNodes()
    {
        var start = new TrafficNode(Vector3.zero);
        var near = new TrafficNode(Vector3.right);
        var far = new TrafficNode(Vector3.right * 10f);
        var detour = new TrafficNode(Vector3.forward * 50f);
        var unreachable = new TrafficNode(Vector3.left);
        Connect(start, detour, 50f);
        Connect(detour, near, 50f);
        Connect(start, far, 10f);
        Connect(unreachable, start, 1f);
        var distances = VehicleDeliveryRouting.Search(
            new Dictionary<TrafficNode, float> { [start] = 0f }, edge => true);
        Assert.That(distances[near], Is.EqualTo(100f));
        Assert.That(distances[far], Is.EqualTo(10f));
        Assert.That(distances.ContainsKey(unreachable), Is.False);
    }

    [Test]
    public void SearchRelaxesExistingRoutesAndExcludesIllegalEdges()
    {
        var start = new TrafficNode(Vector3.zero);
        var middle = new TrafficNode(Vector3.right);
        var target = new TrafficNode(Vector3.right * 2f);
        Connect(start, target, 100f);
        Connect(start, middle, 5f);
        Connect(middle, target, 5f);
        TrafficEdge forbidden = Connect(start, target, 1f);
        var distances = VehicleDeliveryRouting.Search(
            new Dictionary<TrafficNode, float> { [start] = 0f }, edge => edge != forbidden);
        Assert.That(distances[target], Is.EqualTo(10f));
    }

    [Test]
    public void FactoryPriorityDefaultsToFiveAndClampsPerBuilding()
    {
        var firstObject = new GameObject("Priority test");
        var secondObject = new GameObject("Other priority test");
        var data = ScriptableObject.CreateInstance<ResourceProductionBuildingData>();
        try
        {
            var first = firstObject.AddComponent<Building>();
            var second = secondObject.AddComponent<Building>();
            first.Initialize(data, Vector2Int.zero, 0, new List<BuildingTile>());
            second.Initialize(data, Vector2Int.one, 0, new List<BuildingTile>());
            Assert.That(first.FactoryPriority, Is.EqualTo(5));
            first.SetFactoryPriority(20);
            Assert.That(first.FactoryPriority, Is.EqualTo(9));
            Assert.That(second.FactoryPriority, Is.EqualTo(5));
            first.SetFactoryPriority(-1);
            Assert.That(first.FactoryPriority, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(firstObject);
            Object.DestroyImmediate(secondObject);
            Object.DestroyImmediate(data);
        }
    }

    [Test]
    public void FactoryPriorityButtonColorRunsFromBlueThroughYellowToRed()
    {
        Assert.That(
            FactoryPriorityButton.GetPriorityColor(0),
            Is.EqualTo(Color.blue));
        Assert.That(
            FactoryPriorityButton.GetPriorityColor(5),
            Is.EqualTo(Color.yellow));
        Assert.That(
            FactoryPriorityButton.GetPriorityColor(9),
            Is.EqualTo(Color.red));

        Color low = FactoryPriorityButton.GetPriorityColor(2);
        Color high = FactoryPriorityButton.GetPriorityColor(7);
        Assert.That(low.b, Is.GreaterThan(low.r));
        Assert.That(high.r, Is.GreaterThan(high.b));
    }

    [Test]
    public void FactoryPriorityButtonUsesLeftAndRightClicksWithinBounds()
    {
        var factoryObject = new GameObject("Priority factory");
        var buttonObject = new GameObject(
            "Priority button",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button),
            typeof(FactoryPriorityButton));
        var labelObject = new GameObject(
            "Label",
            typeof(RectTransform),
            typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(buttonObject.transform, false);
        var data = ScriptableObject.CreateInstance<
            ResourceProductionBuildingData>();

        try
        {
            var factory = factoryObject.AddComponent<Building>();
            factory.Initialize(
                data,
                Vector2Int.zero,
                0,
                new List<BuildingTile>());

            Button button = buttonObject.GetComponent<Button>();
            Image background = buttonObject.GetComponent<Image>();
            TMP_Text label = labelObject.GetComponent<TMP_Text>();
            FactoryPriorityButton priorityButton =
                buttonObject.GetComponent<FactoryPriorityButton>();
            priorityButton.Configure(button, background, label);
            priorityButton.Bind(factory);

            button.onClick.Invoke();
            Assert.That(factory.FactoryPriority, Is.EqualTo(6));
            Assert.That(label.text, Is.EqualTo("6"));

            priorityButton.OnPointerClick(new PointerEventData(null)
            {
                button = PointerEventData.InputButton.Right
            });
            Assert.That(factory.FactoryPriority, Is.EqualTo(5));
            Assert.That(label.text, Is.EqualTo("5"));

            factory.SetFactoryPriority(9);
            button.onClick.Invoke();
            Assert.That(factory.FactoryPriority, Is.EqualTo(9));

            factory.SetFactoryPriority(1);
            priorityButton.DecreasePriority();
            Assert.That(factory.FactoryPriority, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(buttonObject);
            Object.DestroyImmediate(factoryObject);
            Object.DestroyImmediate(data);
        }
    }

    private static TrafficEdge Connect(TrafficNode from, TrafficNode to, float length)
    {
        var edge = new TrafficEdge(from, to, 10f) { totalLength = length };
        from.outgoingEdges.Add(edge);
        return edge;
    }
}
