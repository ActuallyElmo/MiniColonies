using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class SimulationSpeedButtonTests
{
    private GameObject clockObject;
    private GameObject buttonObject;
    private GameTimeManager clock;
    private SimulationSpeedButton control;

    [SetUp]
    public void SetUp()
    {
        clockObject = new GameObject("Speed button test clock");
        clock = clockObject.AddComponent<GameTimeManager>();
        InvokeLifecycle(clock, "Awake");
        buttonObject = new GameObject("Speed button test", typeof(RectTransform), typeof(Button));
        control = buttonObject.AddComponent<SimulationSpeedButton>();
        InvokeLifecycle(control, "OnEnable");
    }

    [TearDown]
    public void TearDown()
    {
        InvokeLifecycle(control, "OnDisable");
        Object.DestroyImmediate(buttonObject);
        InvokeLifecycle(clock, "OnDestroy");
        Object.DestroyImmediate(clockObject);
    }

    [TestCase(PointerEventData.InputButton.Left, 0.5f)]
    [TestCase(PointerEventData.InputButton.Right, 2f)]
    [TestCase(PointerEventData.InputButton.Middle, 1f)]
    public void PointerDispatchChangesSpeedExactlyOnceInRequestedDirection(
        PointerEventData.InputButton mouseButton, float expectedSpeed)
    {
        var pointer = new PointerEventData(null) { button = mouseButton };
        ExecuteEvents.Execute(buttonObject, pointer, ExecuteEvents.pointerClickHandler);
        Assert.AreEqual(expectedSpeed, clock.SimulationSpeed);
    }

    [Test]
    public void LeftClickPausesAtHalfSpeedAndRightClickResumesAtHalfSpeed()
    {
        clock.SimulationSpeed = 0.5f;
        var pointer = new PointerEventData(null) { button = PointerEventData.InputButton.Left };
        ExecuteEvents.Execute(buttonObject, pointer, ExecuteEvents.pointerClickHandler);
        Assert.AreEqual(0f, clock.SimulationSpeed);
        ExecuteEvents.Execute(buttonObject, pointer, ExecuteEvents.pointerClickHandler);
        Assert.AreEqual(0f, clock.SimulationSpeed);
        pointer.button = PointerEventData.InputButton.Right;
        ExecuteEvents.Execute(buttonObject, pointer, ExecuteEvents.pointerClickHandler);
        Assert.AreEqual(0.5f, clock.SimulationSpeed);
    }

    [Test]
    public void RebindingDoesNotDuplicateNormalClickAndNoninteractableIgnoresBothButtons()
    {
        InvokeLifecycle(control, "OnDisable");
        InvokeLifecycle(control, "OnEnable");
        clock.SimulationSpeed = 4f;
        Button button = buttonObject.GetComponent<Button>();
        button.onClick.Invoke();
        Assert.AreEqual(2f, clock.SimulationSpeed);

        button.interactable = false;
        button.onClick.Invoke();
        control.OnPointerClick(new PointerEventData(null) { button = PointerEventData.InputButton.Right });
        Assert.AreEqual(2f, clock.SimulationSpeed);
    }

    private static void InvokeLifecycle(object target, string name) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(target, null);
}
