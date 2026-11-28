using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>Softens menu artwork while keeping controls and text sharp.</summary>
public sealed class MenuSoftEffects : MonoBehaviour
{
    [SerializeField] private Shader effectShader;
    [SerializeField, Range(0f, 8f)] private float backgroundBlur = 2.5f;
    [SerializeField, Range(0f, 1f)] private float titleGlowOpacity = .3f;
    [SerializeField, Range(0f, 60f)] private float titleGlowRadius = 30f;
    [SerializeField, Range(0f, 30f)] private float buttonTextGlowRadius = 15f;
    [SerializeField, Range(0f, 1f)] private float buttonTextGlowOpacity = .15f;

    public void ApplyTitleEffect(Image image)
    {
        if (effectShader == null || image == null) return;
        var effect = image.GetComponent<MenuImageSoftEffect>() ?? image.gameObject.AddComponent<MenuImageSoftEffect>();
        effect.Configure(effectShader, 0f, titleGlowRadius, titleGlowOpacity);
    }

    private void Awake()
    {
        foreach (var label in GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            var button = label.GetComponentInParent<Button>();
            if (button == null || (button.name != "Start" && button.name != "Quit")) continue;
            label.gameObject.AddComponent<MenuTextGlow>().Configure(buttonTextGlowRadius, buttonTextGlowOpacity);
        }
        if (effectShader == null) return;
        foreach (var image in GetComponentsInChildren<Image>(true))
        {
            bool title = image.name == "Title";
            bool background = image.name.StartsWith("Main_");
            if ((!title && !background) || image.type != Image.Type.Simple) continue;
            var effect = image.gameObject.AddComponent<MenuImageSoftEffect>();
            effect.Configure(effectShader, background ? backgroundBlur : 0f,
                title ? titleGlowRadius : 0f, title ? titleGlowOpacity : 0f);
        }
    }
}
