using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(PlayerHealth))]
public sealed class PlayerHealthHUD : MonoBehaviour
{
    private PlayerHealth playerHealth;
    private TMP_Text healthText;
    private GameObject canvasRoot;

    private void Awake()
    {
        playerHealth = GetComponent<PlayerHealth>();
        CreateHud();
    }

    private void Start()
    {
        Refresh(playerHealth.CurrentHealth, playerHealth.MaxHealth);
    }

    private void OnEnable()
    {
        if (playerHealth != null) playerHealth.HealthChanged += Refresh;
    }

    private void OnDisable()
    {
        if (playerHealth != null) playerHealth.HealthChanged -= Refresh;
    }

    private void OnDestroy()
    {
        if (canvasRoot != null) Destroy(canvasRoot);
    }

    private void CreateHud()
    {
        canvasRoot = new GameObject("Player Health UI", typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);

        GameObject textObject = new GameObject("Health Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(canvasRoot.transform, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-32f, -24f);
        rect.sizeDelta = new Vector2(320f, 64f);

        healthText = textObject.GetComponent<TextMeshProUGUI>();
        healthText.font = TMP_Settings.defaultFontAsset;
        healthText.fontSize = 32f;
        healthText.alignment = TextAlignmentOptions.TopRight;
        healthText.color = Color.white;
    }

    private void Refresh(int currentHealth, int maxHealth)
    {
        healthText.text = $"HP {currentHealth} / {maxHealth}";
    }
}
