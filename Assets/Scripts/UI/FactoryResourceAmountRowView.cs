using TMPro;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public sealed class FactoryResourceAmountRowView : MonoBehaviour
{
    [SerializeField] private TMP_Text resourceNameText;
    [SerializeField] private TMP_Text amountText;
    [SerializeField] private Button decreaseButton;
    [SerializeField] private Button increaseButton;
    [SerializeField] private Image resourceIcon;
    [SerializeField] private TMP_Dropdown inputResourceDropdown;
    [SerializeField] private GameObject specificInputSpacer;
    [HideInInspector] public int inputDropdownStyleVersion;
    private readonly List<Image> additionalTypeIcons = new List<Image>();
    private bool hasInputChoices;

    public void BindInput(string inputName, IReadOnlyList<ResourceType> allowed, FactoryShiftInput input,
        int requiredAmount, bool usesTypes, bool canSelect, Action<ResourceType> selectResource, UnityAction decrease, UnityAction increase,
        IReadOnlyList<string> requiredTypes = null)
    {
        Bind(!usesTypes && input.hasSelection ? ResourceCatalog.GetDisplayName(input.resourceType) : inputName,
            input.desiredAmount, requiredAmount, decrease, increase);
        BindRequirementIcons(usesTypes, requiredTypes, input);
        if (specificInputSpacer != null) specificInputSpacer.SetActive(!usesTypes);
        if (decreaseButton != null) decreaseButton.interactable = input.hasSelection;
        if (increaseButton != null) increaseButton.interactable = input.hasSelection;
        if (inputResourceDropdown == null) return;
        hasInputChoices = false;
        inputResourceDropdown.onValueChanged.RemoveAllListeners();
        inputResourceDropdown.ClearOptions();
        inputResourceDropdown.gameObject.SetActive(usesTypes);
        if (!usesTypes)
        {
            SetInputSelectionInteractable(false);
            return;
        }
        var options = new List<TMP_Dropdown.OptionData>();
        var choices = new List<ResourceType>(allowed);
        int selectedIndex = input.hasSelection ? choices.IndexOf(input.resourceType) : -1;
        bool includePrompt = selectedIndex < 0;
        if (includePrompt) options.Add(new TMP_Dropdown.OptionData(choices.Count == 0 ? "No matching materials" : "Select material"));
        for (int i = 0; i < choices.Count; i++)
            options.Add(new TMP_Dropdown.OptionData(ResourceCatalog.GetDisplayName(choices[i]), ResourceCatalog.GetIcon(choices[i]), Color.white));
        inputResourceDropdown.AddOptions(options);
        inputResourceDropdown.SetValueWithoutNotify(includePrompt ? 0 : selectedIndex);
        hasInputChoices = choices.Count > 0;
        SetInputSelectionInteractable(canSelect);
        inputResourceDropdown.RefreshShownValue();
        inputResourceDropdown.onValueChanged.AddListener(index =>
        {
            int choice = index - (includePrompt ? 1 : 0);
            if (choice >= 0 && choice < choices.Count) selectResource?.Invoke(choices[choice]);
        });
    }

    public void SetInputSelectionInteractable(bool canSelect)
    {
        if (inputResourceDropdown == null) return;
        inputResourceDropdown.interactable = canSelect && hasInputChoices && inputResourceDropdown.gameObject.activeSelf;
        if (!inputResourceDropdown.interactable && inputResourceDropdown.IsExpanded)
            inputResourceDropdown.Hide();
    }

    private void BindRequirementIcons(bool usesTypes, IReadOnlyList<string> requiredTypes, FactoryShiftInput input)
    {
        if (resourceIcon == null) return;
        ResourceCatalog catalog = ResourceCatalog.Current;
        int typeCount = usesTypes && requiredTypes != null ? requiredTypes.Count : 0;
        SetIcon(resourceIcon, usesTypes
            ? typeCount > 0 ? catalog?.GetTypeIcon(requiredTypes[0]) : catalog?.fallbackIcon
            : input.hasSelection ? ResourceCatalog.GetIcon(input.resourceType) : catalog?.fallbackIcon);
        for (int i = 1; i < typeCount; i++)
        {
            if (additionalTypeIcons.Count < i)
            {
                // Variable-length requirements reuse the authored resource icon's design.
                Image icon = Instantiate(resourceIcon, resourceIcon.transform.parent, false);
                icon.name = "ResourceTypeIcon";
                additionalTypeIcons.Add(icon);
            }
            Image additional = additionalTypeIcons[i - 1];
            additional.transform.SetSiblingIndex(resourceIcon.transform.GetSiblingIndex() + i);
            SetIcon(additional, catalog?.GetTypeIcon(requiredTypes[i]));
        }
        for (int i = Mathf.Max(0, typeCount - 1); i < additionalTypeIcons.Count; i++)
            additionalTypeIcons[i].gameObject.SetActive(false);
    }

    private static void SetIcon(Image image, Sprite sprite)
    {
        image.sprite = sprite;
        image.color = Color.white;
        image.preserveAspect = true;
        image.gameObject.SetActive(sprite != null);
    }

    private void OnDestroy()
    {
        if (inputResourceDropdown != null) inputResourceDropdown.onValueChanged.RemoveAllListeners();
    }

    public void Bind(
        string resourceName,
        int amount,
        int requiredAmount,
        UnityAction decrease,
        UnityAction increase)
    {
        if (resourceNameText != null) resourceNameText.text = resourceName;
        if (amountText != null)
        {
            amountText.text = $"{amount}/{requiredAmount}";
        }

        BindButton(decreaseButton, decrease);
        BindButton(increaseButton, increase);
    }

    private static void BindButton(Button button, UnityAction action)
    {
        if (button == null) return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }
}
