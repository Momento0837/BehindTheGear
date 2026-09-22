using TMPro;
using UnityEngine;

public sealed class InteractionPromptUI : MonoBehaviour
{
    [SerializeField] private TMP_Text promptText;

    public void Configure(TMP_Text text)
    {
        promptText = text;
        promptText.text = "[F] Interact";
        SetVisible(false);
    }

    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
    }
}
