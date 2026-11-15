using System;
using BehindTheGear.HideOut;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>One-slot menu on the existing control-selection entry scene.</summary>
public sealed class ManualSaveMenu : MonoBehaviour
{
    public HideOutSettings settings;
    public GameObject continuePlayerPrefab;
    [SerializeField] private Sprite titleSprite;
    [SerializeField] private MenuSoftEffects titleStyle;
    [SerializeField] private Sprite buttonPlateSprite;
    [SerializeField] private InventoryItemDefinition buttonGear;
    private GameObject panel, confirmation;
    private TMP_Text status, question, guidance;
    private Button newGameButton, continueButton, deleteButton, confirmButton, cancelButton;
    private GameObject selectionBeforeConfirmation;
    private Action confirmed;
    private bool visible = true;
    public void SetVisible(bool value)
    {
        visible = value;
        if (panel != null) panel.SetActive(value && !ChoosingControls);
        if (value && panel != null && panel.activeInHierarchy) SelectPreferredButton();
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
        CreateTitle(panel.transform);
        status = Label(panel.transform, "", 230, 24);
        continueButton = Button(panel.transform, "이어하기", 446, Continue, true);
        newGameButton = Button(panel.transform, "새 게임", 350, NewGame, true);
        deleteButton = Button(panel.transform, "삭제", 542, Delete, true, true);
        deleteButton.GetComponentInChildren<TMP_Text>().text = "저장 삭제";
        guidance = Label(panel.transform, "", 670, 22);
        confirmation = new GameObject("Confirmation", typeof(RectTransform), typeof(Image)); confirmation.transform.SetParent(panel.transform, false);
        rect = (RectTransform)confirmation.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        confirmation.GetComponent<Image>().color = new Color(.11f, .055f, .04f, .99f);
        question = Label(confirmation.transform, "", 260, 25);
        confirmButton = Button(confirmation.transform, "확인", 430, Confirm);
        cancelButton = Button(confirmation.transform, "취소", 530, Cancel);
        confirmation.SetActive(false); Refresh();
        panel.SetActive(visible);
        if (visible) SelectPreferredButton();
    }
    private void CreateTitle(Transform parent)
    {
        if (titleSprite == null)
        {
            Debug.LogError("[Save Menu] Assign the title artwork.", this);
            return;
        }
        var title = new GameObject("Save Title", typeof(RectTransform), typeof(Image));
        title.transform.SetParent(parent, false);
        var image = title.GetComponent<Image>();
        image.sprite = titleSprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = new Vector2(0, -160);
        float width = 640f;
        rect.sizeDelta = new Vector2(width, width * titleSprite.rect.height / titleSprite.rect.width);
        titleStyle?.ApplyTitleEffect(image);
    }

    public void Refresh()
    {
        bool exists = ManualSaveStore.Exists;
        if (guidance != null) guidance.text = exists
            ? "침대에서 마지막으로 저장한 지점부터 이어집니다."
            : "게임 진행 상황은 침대에서 저장할 수 있습니다.";
        if (continueButton != null) continueButton.interactable = exists;
        if (deleteButton != null) deleteButton.interactable = exists;
        if (newGameButton != null) newGameButton.GetComponent<SaveSlotButtonStyle>().SetPrimary(!exists);
        if (continueButton != null) continueButton.GetComponent<SaveSlotButtonStyle>().SetPrimary(exists);
        ConfigureNavigation(exists);
        if (visible && panel != null && panel.activeInHierarchy && confirmation != null && !confirmation.activeSelf)
        {
            var current = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            var selected = current != null ? current.GetComponent<Button>() : null;
            if (selected == null || !selected.IsInteractable()) SelectPreferredButton();
        }
        if (status == null) return;
        try
        {
            if (exists) HideOutPlayerBridge.ValidateSnapshot(ManualSaveStore.ReadSlot(), settings);
            status.text = SaveGameSession.MenuMessage ?? (exists ? "저장된 게임" : "저장된 게임이 없습니다");
        }
        catch (Exception error) { status.text = "저장된 게임을 불러올 수 없습니다.\n" + error.Message; }
    }
    public void Continue()
    {
        try { SaveGameSession.Continue(ManualSaveStore.ReadSlot(), settings, continuePlayerPrefab); }
        catch (Exception error) { if (status != null) status.text = "불러오기 실패: " + error.Message; }
    }
    public void NewGame()
    {
        if (SceneFadeTransition.IsRunning) return;
        if (ManualSaveStore.Exists) Ask("새 게임을 시작할까요?\n침대에서 처음 저장하면 기존 진행 기록을 덮어씁니다.", Begin, "시작하기");
        else Begin();
    }
    private void Begin() { SaveGameSession.BeginNewGame(); ChoosingControls = true; panel.SetActive(false); GetComponent<ControlSchemeSelectScene>()?.StartNewGame(); }
    public void Delete() => Ask("저장 데이터를 삭제할까요?\n삭제한 진행 상황은 복구할 수 없습니다.", () =>
    {
        try { ManualSaveStore.DeleteSlot(); Refresh(); }
        catch (Exception error) { status.text = "삭제 실패: " + error.Message; }
    }, "삭제");
    private void Ask(string text, Action action, string confirmText = "확인")
    {
        selectionBeforeConfirmation = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        confirmed = action; question.text = text; confirmation.SetActive(true);
        confirmButton.GetComponentInChildren<TMP_Text>().text = confirmText;
        SelectButton(cancelButton);
    }
    public void Confirm()
    {
        var action = confirmed; confirmed = null; confirmation.SetActive(false); action?.Invoke();
        if (panel.activeInHierarchy) SelectPreferredButton();
    }
    public void Cancel()
    {
        confirmed = null; confirmation.SetActive(false);
        var previous = selectionBeforeConfirmation != null ? selectionBeforeConfirmation.GetComponent<Button>() : null;
        if (previous != null && previous.IsInteractable() && previous.gameObject.activeInHierarchy) SelectButton(previous);
        else SelectPreferredButton();
    }

    private void SelectPreferredButton() => SelectButton(ManualSaveStore.Exists ? continueButton : newGameButton);
    private static void SelectButton(Button button)
    {
        if (EventSystem.current != null && button != null && button.IsInteractable() && button.gameObject.activeInHierarchy)
            EventSystem.current.SetSelectedGameObject(button.gameObject);
    }
    private void ConfigureNavigation(bool exists)
    {
        if (newGameButton == null || continueButton == null || deleteButton == null) return;
        NavigationFor(newGameButton, exists ? deleteButton : newGameButton, exists ? continueButton : newGameButton);
        NavigationFor(continueButton, newGameButton, deleteButton);
        NavigationFor(deleteButton, continueButton, newGameButton);
        NavigationFor(confirmButton, cancelButton, cancelButton);
        NavigationFor(cancelButton, confirmButton, confirmButton);
    }
    private static void NavigationFor(Button button, Button up, Button down)
    {
        if (button == null) return;
        button.navigation = new Navigation { mode = Navigation.Mode.Explicit,
            selectOnUp = up, selectOnDown = down, selectOnLeft = up, selectOnRight = down };
    }
    private static TMP_Text Label(Transform parent, string value, float top, int size)
    {
        var go = new GameObject(value, typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1); rect.pivot = new Vector2(.5f, 1);
        rect.anchoredPosition = new Vector2(0, -top); rect.sizeDelta = new Vector2(1250, 100);
        var label = go.GetComponent<TMP_Text>(); UIFontTheme.Apply(label, UIFontTheme.Normal);
        label.text = value; label.fontSize = size; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false; return label;
    }
    private Button Button(Transform parent, string text, float top, UnityEngine.Events.UnityAction action,
        bool metalPlate = false, bool destructive = false)
    {
        var go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1); rect.pivot = new Vector2(.5f, 1);
        rect.anchoredPosition = new Vector2(0, -top); rect.sizeDelta = new Vector2(420, metalPlate ? 72 : 65);
        go.GetComponent<Image>().color = new Color(.34f, .17f, .12f);
        var label = Label(go.transform, text, 14, 26); label.rectTransform.sizeDelta = new Vector2(400, 40);
        var button = go.GetComponent<Button>(); button.onClick.AddListener(action);
        if (metalPlate) go.AddComponent<SaveSlotButtonStyle>().Configure(label, buttonPlateSprite,
            buttonGear != null ? buttonGear.Icon : null, destructive);
        return button;
    }
}
