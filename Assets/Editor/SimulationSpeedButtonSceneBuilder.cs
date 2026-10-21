using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Installs editable speed controls in loaded game scenes, preserving authored overrides.</summary>
public static class SimulationSpeedButtonSceneBuilder
{
    private const string ButtonName = "GameClock";
    private const string GameFontPath = "Assets/Fonts/ari-w9500-condensed-display.asset";

    [InitializeOnLoadMethod]
    private static void QueueInstallation()
    {
        EditorApplication.delayCall += InstallMissingButtons;
        EditorSceneManager.sceneOpened -= OnSceneOpened;
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode) => InstallMissingButtons();

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += InstallMissingButtons;
    }

    [MenuItem("Tools/Mini Colonies/Install Simulation Speed Button")]
    [MenuItem("Tools/Mini Colonies/Install Game Clock")]
    public static void Install()
    {
        InstallMissingButtons();
        GameClockUI button = Resources.FindObjectsOfTypeAll<GameClockUI>()
            .FirstOrDefault(candidate => IsSceneObject(candidate));
        if (button != null)
        {
            Selection.activeGameObject = button.gameObject;
            Debug.Log("The editable game clock and speed buttons are ready. Save the scene to retain them.", button);
        }
        else
            Debug.LogWarning("Open a scene containing GameTimeManager and a screen-space Canvas first.");
    }

    private static void InstallMissingButtons()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        foreach (GameTimeManager manager in Resources.FindObjectsOfTypeAll<GameTimeManager>())
        {
            if (!IsSceneObject(manager)) continue;
            Scene scene = manager.gameObject.scene;
            bool wasDirty = scene.isDirty;
            if (!EnsureButton(manager)) continue;
            EditorSceneManager.MarkSceneDirty(scene);
            // Persist newly authored controls only when there are no unrelated pending edits.
            if (!wasDirty && !string.IsNullOrEmpty(scene.path)) EditorSceneManager.SaveScene(scene);
        }
    }

    private static bool IsSceneObject(Component component) =>
        component != null && !EditorUtility.IsPersistent(component) &&
        component.gameObject.scene.IsValid() && component.gameObject.scene.isLoaded &&
        !EditorSceneManager.IsPreviewScene(component.gameObject.scene);

    private static bool EnsureButton(GameTimeManager manager)
    {
        Scene scene = manager.gameObject.scene;
        GameClockUI existing = Resources.FindObjectsOfTypeAll<GameClockUI>()
            .FirstOrDefault(candidate => IsSceneObject(candidate) && candidate.gameObject.scene == scene);
        if (existing != null)
        {
            // Add the day label once, preserving subsequent designer edits.
            var existingSerialized = new SerializedObject(existing);
            SerializedProperty managerProperty = existingSerialized.FindProperty("gameTimeManager");
            bool changed = false;
            if (managerProperty.objectReferenceValue == null)
            {
                managerProperty.objectReferenceValue = manager;
                changed = true;
            }
            SerializedProperty dayProperty = existingSerialized.FindProperty("dayLabel");
            TMP_Text hour = existingSerialized.FindProperty("hourLabel").objectReferenceValue as TMP_Text;
            if (dayProperty.objectReferenceValue == null && hour != null)
            {
                Transform dayTransform = hour.transform.parent.Find("Day");
                TMP_Text day = dayTransform != null ? dayTransform.GetComponent<TMP_Text>() : null;
                if (day == null) day = CreateDayLabel(hour, manager);
                dayProperty.objectReferenceValue = day;
                hour.text = GameClockUI.FormatHour(
                    new SerializedObject(manager).FindProperty("startingHour").intValue);
                changed = true;
            }
            if (!changed) return false;
            existingSerialized.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        PlayerViewModeUI viewUI = Resources.FindObjectsOfTypeAll<PlayerViewModeUI>()
            .FirstOrDefault(candidate => IsSceneObject(candidate) && candidate.gameObject.scene == scene);
        Canvas canvas = viewUI != null ? viewUI.GetComponentInParent<Canvas>() : null;
        if (canvas != null) canvas = canvas.rootCanvas;
        if (canvas == null || canvas.renderMode == RenderMode.WorldSpace)
            canvas = Resources.FindObjectsOfTypeAll<Canvas>().FirstOrDefault(candidate =>
                IsSceneObject(candidate) && candidate.gameObject.scene == scene &&
                candidate.isRootCanvas && candidate.renderMode != RenderMode.WorldSpace);
        if (canvas == null) return false;

        GameObject root = new GameObject(ButtonName, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image));
        root.SetActive(false);
        root.layer = canvas.gameObject.layer;
        root.transform.SetParent(canvas.transform, false);
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(20f, -20f);
        rect.sizeDelta = new Vector2(280f, 126f);
        Image background = root.GetComponent<Image>();
        background.color = new Color32(22, 27, 36, 255);
        Outline outline = root.AddComponent<Outline>();
        outline.effectColor = new Color32(59, 70, 88, 220);
        outline.effectDistance = new Vector2(1f, -1f);

        var managerSerialized = new SerializedObject(manager);
        TMP_Text label = CreateText(root.transform, "Hour", GameClockUI.FormatHour(
            managerSerialized.FindProperty("startingHour").intValue), 32f,
            new Color32(240, 243, 248, 255), new Vector2(0f, 0.59f), Vector2.one);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.rectTransform.offsetMin = new Vector2(14f, 3f);
        TMP_Text dayLabel = CreateDayLabel(label, manager);
        Image divider = CreateImage(root.transform, "Underbar", new Color32(102, 126, 160, 255));
        SetRect(divider.rectTransform, new Vector2(14f, -52f), new Vector2(252f, 2f));

        var buttons = new Button[5];
        var indicators = new GameObject[5];
        string[] names = { "Pause", "HalfSpeed", "NormalSpeed", "DoubleSpeed", "QuadrupleSpeed" };
        string[] captions = { "Pause", "x0.5", "x1", "x2", "x4" };
        float[] speeds = { 0f, 0.5f, 1f, 2f, 4f };
        for (int i = 0; i < buttons.Length; i++)
        {
            Image face = CreateImage(root.transform, names[i], new Color32(42, 49, 61, 255));
            SetRect(face.rectTransform, new Vector2(14f + i * 51f, -64f), new Vector2(48f, 48f));
            face.raycastTarget = true;
            Button speedButton = face.gameObject.AddComponent<Button>();
            speedButton.targetGraphic = face;
            buttons[i] = speedButton;
            CreateText(face.transform, "Caption", captions[i], 12f,
                new Color32(240, 243, 248, 255), Vector2.zero, new Vector2(1f, 0.46f));

            // Editable Image placeholders avoid font-specific icon glyphs. Swap
            // the Icon sprite later and remove these authored bars if desired.
            Image icon = CreateImage(face.transform, "Icon", Color.clear);
            SetRect(icon.rectTransform, new Vector2(10f, -7f), new Vector2(28f, 16f));
            int bars = i == 0 ? 2 : i;
            for (int bar = 0; bar < bars; bar++)
            {
                Image mark = CreateImage(icon.transform, "Mark" + (bar + 1),
                    new Color32(218, 229, 244, 255));
                float width = i == 0 ? 5f : 4f;
                float height = i == 0 ? 14f : 6f + bar * 3f;
                SetRect(mark.rectTransform,
                    new Vector2((28f - bars * 7f) * 0.5f + bar * 7f, -(16f - height) * 0.5f),
                    new Vector2(width, height));
            }
            Image indicator = CreateImage(face.transform, "Selected", new Color32(119, 191, 255, 255));
            SetRect(indicator.rectTransform, new Vector2(0f, -45f), new Vector2(48f, 3f));
            indicators[i] = indicator.gameObject;
            indicators[i].SetActive(manager.SimulationSpeed == speeds[i]);
        }

        GameClockUI controller = root.AddComponent<GameClockUI>();
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("gameTimeManager").objectReferenceValue = manager;
        serialized.FindProperty("hourLabel").objectReferenceValue = label;
        serialized.FindProperty("dayLabel").objectReferenceValue = dayLabel;
        SerializedProperty buttonArray = serialized.FindProperty("speedButtons");
        SerializedProperty indicatorArray = serialized.FindProperty("selectedIndicators");
        buttonArray.arraySize = indicatorArray.arraySize = 5;
        for (int i = 0; i < 5; i++)
        {
            buttonArray.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
            indicatorArray.GetArrayElementAtIndex(i).objectReferenceValue = indicators[i];
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        root.SetActive(true);
        // Retain the old authored control for reference, but replace its visible
        // role once. Subsequent installs preserve all edits to the new clock.
        foreach (SimulationSpeedButton legacy in Resources.FindObjectsOfTypeAll<SimulationSpeedButton>())
            if (IsSceneObject(legacy) && legacy.gameObject.scene == scene)
                legacy.gameObject.SetActive(false);
        return true;
    }

    private static TMP_Text CreateDayLabel(TMP_Text hourLabel, GameTimeManager manager)
    {
        RectTransform hourRect = hourLabel.rectTransform;
        TMP_Text day = CreateText(hourLabel.transform.parent, "Day",
            "Day " + Mathf.Max(1, new SerializedObject(manager).FindProperty("startingDay").intValue),
            hourLabel.fontSize * 0.55f, hourLabel.color,
            hourRect.anchorMin, new Vector2(hourRect.anchorMin.x, hourRect.anchorMax.y));
        day.font = hourLabel.font;
        day.alignment = TextAlignmentOptions.MidlineLeft;
        day.rectTransform.pivot = hourRect.pivot;
        day.rectTransform.offsetMin = hourRect.offsetMin;
        day.rectTransform.offsetMax = new Vector2(hourRect.offsetMin.x + 80f, hourRect.offsetMax.y);
        hourRect.offsetMin += new Vector2(92f, 0f);
        return day;
    }

    private static Image CreateImage(Transform parent, string name, Color color)
    {
        var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false);
        Image image = child.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static TMP_Text CreateText(Transform parent, string name, string value, float size,
        Color color, Vector2 anchorMin, Vector2 anchorMax)
    {
        var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false);
        TMP_Text text = child.GetComponent<TMP_Text>();
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(GameFontPath);
        if (font != null) text.font = font;
        text.rectTransform.anchorMin = anchorMin;
        text.rectTransform.anchorMax = anchorMax;
        text.rectTransform.offsetMin = new Vector2(6f, 3f);
        text.rectTransform.offsetMax = new Vector2(-6f, -3f);
        return text;
    }
}
