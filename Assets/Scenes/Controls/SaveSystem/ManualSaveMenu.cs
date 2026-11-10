using System;
using BehindTheGear.HideOut;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One-slot menu on the existing control-selection entry scene.</summary>
public sealed class ManualSaveMenu : MonoBehaviour
{
    public HideOutSettings settings;
    public GameObject continuePlayerPrefab;
    private GameObject panel, confirmation;
    private TMP_Text status, question;
    private Button continueButton, deleteButton;
    private Action confirmed;
    private bool visible = true;
    public void SetVisible(bool value)
    {
        visible = value;
        if (panel != null) panel.SetActive(value && !ChoosingControls);
    }
    public bool ChoosingControls { get; private set; }
    private void Start()
    {
        SaveGameSession.Configure(settings);
        var root = new GameObject("Manual Save Menu", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        root.GetComponent<Canvas>().sortingOrder = 400;
        var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900);
        panel = new GameObject("Slot 1", typeof(RectTransform), typeof(Image)); panel.transform.SetParent(root.transform, false);
        var rect = (RectTransform)panel.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(.09f, .045f, .035f, 1);
        Label(panel.transform, "BEHIND THE GEAR", 110, 46);
        status = Label(panel.transform, "", 230, 24);
        continueButton = Button(panel.transform, "이어하기", 440, Continue);
        Button(panel.transform, "새 게임", 350, NewGame);
        deleteButton = Button(panel.transform, "삭제", 530, Delete);
        Label(panel.transform, "슬롯 1 · 침대에서 저장한 진행 상황만 이어집니다.", 670, 22);
        confirmation = new GameObject("Confirmation", typeof(RectTransform), typeof(Image)); confirmation.transform.SetParent(panel.transform, false);
        rect = (RectTransform)confirmation.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        confirmation.GetComponent<Image>().color = new Color(.11f, .055f, .04f, .99f);
        question = Label(confirmation.transform, "", 260, 25);
        Button(confirmation.transform, "확인", 430, Confirm);
        Button(confirmation.transform, "취소", 530, Cancel);
        confirmation.SetActive(false); Refresh();
        panel.SetActive(visible);
    }
    public void Refresh()
    {
        bool exists = ManualSaveStore.Exists;
        if (continueButton != null) continueButton.interactable = exists;
        if (deleteButton != null) deleteButton.interactable = exists;
        foreach (var button in new[] { continueButton, deleteButton })
        {
            if (button == null) continue;
            var group = button.GetComponent<CanvasGroup>();
            if (group == null) group = button.gameObject.AddComponent<CanvasGroup>();
            group.alpha = button.interactable ? 1f : .5f;
        }
        if (status == null) return;
        try
        {
            if (exists) HideOutPlayerBridge.ValidateSnapshot(ManualSaveStore.ReadSlot(), settings);
            status.text = SaveGameSession.MenuMessage ?? (exists ? "슬롯 1 · 저장 있음" : "슬롯 1 · 비어 있음");
        }
        catch (Exception error) { status.text = "슬롯 1 · 불러오기 불가\n" + error.Message; }
    }
    public void Continue()
    {
        try { SaveGameSession.Continue(ManualSaveStore.ReadSlot(), settings, continuePlayerPrefab); }
        catch (Exception error) { if (status != null) status.text = "불러오기 실패: " + error.Message; }
    }
    public void NewGame()
    {
        if (SceneFadeTransition.IsRunning) return;
        if (ManualSaveStore.Exists) Ask("기존 슬롯으로 새 게임을 시작할까요?\n처음 침대 저장이 성공할 때 기존 저장을 덮어씁니다.", Begin);
        else Begin();
    }
    private void Begin() { SaveGameSession.BeginNewGame(); ChoosingControls = true; panel.SetActive(false); GetComponent<ControlSchemeSelectScene>()?.ShowControlsForNewGame(); }
    public void Delete() => Ask("슬롯 1과 백업을 삭제할까요?", () =>
    {
        try { ManualSaveStore.DeleteSlot(); Refresh(); }
        catch (Exception error) { status.text = "삭제 실패: " + error.Message; }
    });
    private void Ask(string text, Action action) { confirmed = action; question.text = text; confirmation.SetActive(true); }
    public void Confirm() { var action = confirmed; confirmed = null; confirmation.SetActive(false); action?.Invoke(); }
    public void Cancel() { confirmed = null; confirmation.SetActive(false); }
    private static TMP_Text Label(Transform parent, string value, float top, int size)
    {
        var go = new GameObject(value, typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1); rect.pivot = new Vector2(.5f, 1);
        rect.anchoredPosition = new Vector2(0, -top); rect.sizeDelta = new Vector2(1250, 100);
        var label = go.GetComponent<TMP_Text>(); UIFontTheme.Apply(label, UIFontTheme.Normal);
        label.text = value; label.fontSize = size; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false; return label;
    }
    private static Button Button(Transform parent, string text, float top, UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1); rect.pivot = new Vector2(.5f, 1);
        rect.anchoredPosition = new Vector2(0, -top); rect.sizeDelta = new Vector2(420, 65);
        go.GetComponent<Image>().color = new Color(.34f, .17f, .12f);
        var label = Label(go.transform, text, 14, 26); label.rectTransform.sizeDelta = new Vector2(400, 40);
        var button = go.GetComponent<Button>(); button.onClick.AddListener(action); return button;
    }
}
