using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;

public sealed class FactoryWorkerRoleTests
{
    private readonly List<Object> _objects = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        for (int i = _objects.Count - 1; i >= 0; i--)
        {
            if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
        }
        _objects.Clear();
    }

    [Test]
    public void ExistingFactoryDataWithoutRoleFieldsUsesFactoryWorkers()
    {
        ResourceProductionBuildingData data = CreateData();
        JsonUtility.FromJsonOverwrite("{\"popRequiredForMaxEfficiency\":12}", data);

        Assert.AreEqual("factory worker", data.WorkerRoleName);
        Assert.AreEqual("factory workers", data.WorkerRoleNamePlural);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \t ")]
    public void InvalidNamesFallBackBeforeAndAfterInspectorValidation(string invalidName)
    {
        ResourceProductionBuildingData data = CreateData();
        data.workerRoleName = invalidName;
        data.workerRoleNamePlural = invalidName;

        Assert.AreEqual("factory worker", data.WorkerRoleName);
        Assert.AreEqual("factory workers", data.WorkerRoleNamePlural);

        Invoke(data, "OnValidate");

        Assert.AreEqual("factory worker", data.workerRoleName);
        Assert.AreEqual("factory workers", data.workerRoleNamePlural);
    }

    [TestCase("engineer", "engineers")]
    [TestCase("scientist", "scientists")]
    [TestCase("miner", "miners")]
    [TestCase("factory worker", "factory workers")]
    public void FactoryViewsUseTheSelectedTypesRoleAndReplaceItWhenRebound(
        string singular, string plural)
    {
        ResourceProductionBuildingData data = CreateData();
        data.workerRoleName = " " + singular + " ";
        data.workerRoleNamePlural = " " + plural + " ";
        Building building = CreateBuilding(data);
        FactoryOperationsTabView view = CreateObject("Status")
            .AddComponent<FactoryOperationsTabView>();
        TMP_Text prepared = BindText(view, "preparedPopsText");
        TMP_Text working = BindText(view, "currentWorkersText");
        TMP_Text efficiency = BindText(view, "workerEfficiencyText");
        TMP_Text throughput = BindText(view, "throughputLabel");

        view.Bind(building);

        Assert.AreEqual($"Prepared {plural}: --", prepared.text);
        Assert.AreEqual($"Working {plural}: --", working.text);
        Assert.AreEqual($"Efficiency ({plural}): --", efficiency.text);
        Assert.AreEqual(
            $"Throughput per {singular} / hour (100% efficiency)", throughput.text);

        IndustrialBuildingInfoPanel panel = CreateObject("Panel")
            .AddComponent<IndustrialBuildingInfoPanel>();
        TMP_Text summary = BindText(panel, "summaryText");
        TMP_Text incoming = BindText(panel, "incomingPopulationText");
        panel.Initialize(building);
        StringAssert.StartsWith($"Assigned {plural}: 0/12", summary.text);
        Assert.AreEqual($"Incoming {plural}: 0", incoming.text);

        Assert.IsTrue(building.TryAddFactoryShift(out FactoryShift shift));
        GameObject rowObject = CreateObject("Shift row");
        FactoryShiftRowView row = rowObject.AddComponent<FactoryShiftRowView>();
        GameObject populationRow = CreateObject("PopulationRow");
        populationRow.transform.SetParent(rowObject.transform, false);
        TMP_Text roleLabel = CreateText("Label", populationRow.transform);
        row.Bind(building, shift, 0, null, null);
        Assert.AreEqual(char.ToUpperInvariant(plural[0]) + plural.Substring(1), roleLabel.text);

        ResourceProductionBuildingData otherData = CreateData();
        otherData.workerRoleName = "technician";
        otherData.workerRoleNamePlural = "technicians";
        Building otherBuilding = CreateBuilding(otherData);
        Assert.IsTrue(otherBuilding.TryAddFactoryShift(out FactoryShift otherShift));
        view.Bind(otherBuilding);
        panel.Initialize(otherBuilding);
        row.Bind(otherBuilding, otherShift, 0, null, null);

        Assert.AreEqual("Prepared technicians: --", prepared.text);
        Assert.AreEqual("Working technicians: --", working.text);
        StringAssert.Contains("per technician / hour", throughput.text);
        StringAssert.StartsWith("Assigned technicians:", summary.text);
        Assert.AreEqual("Technicians", roleLabel.text);
        Assert.AreEqual(singular, data.WorkerRoleName);
        Assert.AreEqual(plural, data.WorkerRoleNamePlural);

        view.Bind(null);
        Assert.IsEmpty(prepared.text);
        Assert.IsEmpty(working.text);
        Assert.IsEmpty(throughput.text);
    }

    [Test]
    public void RoleNamesRemainVisibleDuringPreparationAndProduction()
    {
        GameTimeManager clock = CreateObject("Clock").AddComponent<GameTimeManager>();
        Invoke(clock, "Awake");
        ResourceProductionBuildingData data = CreateData();
        data.workerRoleName = "scientist";
        data.workerRoleNamePlural = "scientists";
        Building building = CreateBuilding(data);
        Assert.IsTrue(building.TryAddFactoryShift(out FactoryShift shift));
        Assert.IsTrue(building.TrySetFactoryShiftStartHour(shift, 2));
        Assert.IsTrue(building.SetFactoryShiftPreparationHours(shift, 1));
        Assert.IsTrue(building.SetFactoryShiftDesiredPops(shift, 6));
        Assert.IsTrue(building.SetFactoryShiftActive(shift, true));
        FactoryOperationsTabView view = CreateObject("Status")
            .AddComponent<FactoryOperationsTabView>();
        TMP_Text prepared = BindText(view, "preparedPopsText");
        TMP_Text working = BindText(view, "currentWorkersText");
        TMP_Text efficiency = BindText(view, "workerEfficiencyText");
        view.Bind(building);

        clock.AdvanceTime(clock.RealSecondsPerGameHour);
        view.Refresh();
        Assert.AreSame(shift, building.PreparingFactoryShift);
        StringAssert.StartsWith("Arrived scientists: 0/6", prepared.text);

        clock.AdvanceTime(clock.RealSecondsPerGameHour);
        view.Refresh();
        Assert.AreSame(shift, building.CurrentFactoryShift);
        StringAssert.StartsWith("Working scientists: 0/6", working.text);
        StringAssert.StartsWith("Efficiency (scientists):", efficiency.text);
    }

    private ResourceProductionBuildingData CreateData()
    {
        var data = ScriptableObject.CreateInstance<ResourceProductionBuildingData>();
        data.popRequiredForMaxEfficiency = 12;
        _objects.Add(data);
        return data;
    }

    private Building CreateBuilding(ResourceProductionBuildingData data)
    {
        Building building = CreateObject("Factory").AddComponent<Building>();
        building.Initialize(data, Vector2Int.zero, 0, new List<BuildingTile>());
        return building;
    }

    private GameObject CreateObject(string name)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        _objects.Add(obj);
        return obj;
    }

    private TMP_Text CreateText(string name, Transform parent)
    {
        GameObject obj = CreateObject(name);
        obj.transform.SetParent(parent, false);
        return obj.AddComponent<TextMeshProUGUI>();
    }

    private TMP_Text BindText(Component target, string fieldName)
    {
        TMP_Text label = CreateText(fieldName, target.transform);
        target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(target, label);
        return label;
    }

    private static void Invoke(Object target, string methodName)
    {
        target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(target, null);
    }
}
