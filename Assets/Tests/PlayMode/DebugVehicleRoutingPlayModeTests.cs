using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class DebugVehicleRoutingPlayModeTests
{
    private GameObject root;
    private ScriptableObject data;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static Type GameType(string name) => Type.GetType(name + ", Assembly-CSharp", true);

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (root != null) Object.Destroy(root);
        if (data != null) Object.Destroy(data);
        yield return null;
    }

    [UnityTest]
    public IEnumerator UnreachableDebugCarStaysVisibleRetriesOnPublicationAndUnsubscribesOnDisable()
    {
        root = new GameObject("Debug routing lifecycle test");
        var backend = root.AddComponent(GameType("TrafficSystemBackend"));
        var tooltip = new GameObject("Debug routing tooltip");
        tooltip.transform.SetParent(root.transform);
        tooltip.AddComponent(GameType("TooltipManager"));
        var vehicleObject = new GameObject("Parked debug vehicle");
        vehicleObject.transform.SetParent(root.transform);
        var vehicle = (MonoBehaviour)vehicleObject.AddComponent(GameType("VehicleAI"));
        data = ScriptableObject.CreateInstance(GameType("PopTransportVehicleData"));
        vehicle.GetType().GetMethod("InitializeDebugVehicle").Invoke(vehicle, new object[] { data });
        var begin = vehicle.GetType().GetMethod("BeginDebugFactoryTrip");
        var search = vehicle.GetType().GetField("_debugRouteSearch", PrivateInstance);
        var eventField = backend.GetType().GetField("OnTrafficNetworkReady", PrivateInstance);

        LogAssert.Expect(LogType.Warning, new Regex("Debug car: no reachable factory entrance"));
        begin.Invoke(vehicle, null);
        for (int i = 0; i < 4; i++) yield return null;
        Assert.IsTrue(vehicle != null && vehicleObject.activeInHierarchy);
        Assert.IsNull(search.GetValue(vehicle));
        Assert.IsNotNull(eventField.GetValue(backend));

        LogAssert.Expect(LogType.Warning, new Regex("Debug car: no reachable factory entrance"));
        ((Action)eventField.GetValue(backend)).Invoke();
        Assert.IsNotNull(search.GetValue(vehicle));
        for (int i = 0; i < 4; i++) yield return null;
        Assert.IsTrue(vehicle != null && vehicleObject.activeInHierarchy);

        begin.Invoke(vehicle, null);
        vehicle.enabled = false;
        yield return null;
        Assert.IsNull(search.GetValue(vehicle));
        Assert.IsNull(eventField.GetValue(backend));
    }
}
