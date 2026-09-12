using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public sealed class FactoryResourceAmountRowView : MonoBehaviour
{
    [SerializeField] private TMP_Text resourceNameText;
    [SerializeField] private TMP_Text amountText;
    [SerializeField] private Button decreaseButton;
    [SerializeField] private Button increaseButton;

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
