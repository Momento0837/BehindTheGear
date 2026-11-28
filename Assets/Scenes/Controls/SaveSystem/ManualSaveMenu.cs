using System;
using System.Collections;
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
    private SaveConfirmGraphic confirmationPanel;
    private SaveConfirmButtonStyle confirmStyle, cancelStyle;
    private TMP_Text status, question, guidance;
    private Button newGameButton, continueButton, deleteButton, confirmButton, cancelButton;
    private CloudAccountPanel cloudPanel; // Optional account cloud save (Cloud/ folder).
    private GameObject selectionBeforeConfirmation;
    private Action confirmed, cancelled;
    private bool visible = true;
    private bool departing;
    public void SetVisible(bool value)
    {
        visible = value;
        if (panel != null) panel.SetActive(value && !ChoosingControls);
        if (value && panel != null && panel.activeInHierarchy)
        {
            if (confirmation != null && confirmation.activeSelf) SelectButton(cancelButton);
            else SelectPreferredButton();
        }
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
        SaveMenuBackdrop.Build(panel.transform); // clock-tower interior behind the title and buttons
        CreateTitle(panel.transform);
        status = Label(panel.transform, "", 230, 24);
        continueButton = Button(panel.transform, "이어하기", 446, Continue, true);
        newGameButton = Button(panel.transform, "새 게임", 350, NewGame, true);
        deleteButton = Button(panel.transform, "삭제", 542, Delete, true, true);
        deleteButton.GetComponentInChildren<TMP_Text>().text = "저장 삭제";
        guidance = Label(panel.transform, "", 670, 22);
        confirmation = new GameObject("Confirmation", typeof(RectTransform), typeof(Image)); confirmation.transform.SetParent(panel.transform, false);
        rect = (RectTransform)confirmation.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        // Dim (not hide) the slot menu so the dialog reads as a notice laid over it.
        confirmation.GetComponent<Image>().color = new Color(.022f, .012f, .009f, .86f);
        var dialog = CreateConfirmationPanel(confirmation.transform);
        question = Label(dialog, "", 78, 25);
        question.rectTransform.sizeDelta = new Vector2(760, 110);
        question.lineSpacing = 18f;
        question.color = new Color(.90f, .84f, .74f);
        confirmButton = ConfirmationButton(dialog, "확인", -140, Confirm, true, out confirmStyle);
        cancelButton = ConfirmationButton(dialog, "취소", 140, Cancel, false, out cancelStyle);
        cloudPanel = gameObject.AddComponent<CloudAccountPanel>();
        cloudPanel.Build(this, panel.transform, buttonPlateSprite, buttonGear != null ? buttonGear.Icon : null);
        confirmation.SetActive(false); Refresh();
        panel.SetActive(visible);
        if (visible) SelectPreferredButton();
        DiscardLeftoverQuitAutoSave();
    }
    // The quit auto-save was removed (disk saves only at the bed). A file left by an older build is never offered or
    // loaded; it is simply deleted so it cannot bring back unsaved progress.
    private static void DiscardLeftoverQuitAutoSave()
    {
        try { if (QuitAutoSave.Exists) QuitAutoSave.Delete(); }
        catch (Exception error) { Debug.LogWarning("[Save Menu] 남은 자동 저장 삭제 실패: " + error.Message); }
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
            ? ""
            : "게임 진행 상황은 세이브 포인트에서 저장할 수 있습니다.";
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
            string summary = "";
            if (exists)
            {
                var save = ManualSaveStore.ReadSlot();
                HideOutPlayerBridge.ValidateSnapshot(save, settings);
                summary = SaveSummary(save);
            }
            // The record is shown quietly inside 이어하기; the line under the title is only for messages.
            if (continueButton != null) continueButton.GetComponent<SaveSlotButtonStyle>().SetDetail(summary);
            status.text = SaveGameSession.MenuMessage ?? (exists ? "" : "저장된 게임이 없습니다");
        }
        catch (Exception error)
        {
            if (continueButton != null) continueButton.GetComponent<SaveSlotButtonStyle>().SetDetail("");
            status.text = "저장된 게임을 불러올 수 없습니다.\n" + error.Message;
        }
    }
    // What 이어하기 will load: when it was saved and how far the player was.
    private static string SaveSummary(HideOutSave save)
    {
        string when = "";
        try
        {
            // The time stored in the save (a cloud download or file copy does not change it); older saves use the file time.
            DateTime saved;
            if (save.savedAtUtcTicks > DateTime.MinValue.Ticks && save.savedAtUtcTicks <= DateTime.MaxValue.Ticks)
                saved = new DateTime(save.savedAtUtcTicks, DateTimeKind.Utc).ToLocalTime();
            else
            {
                string path = System.IO.File.Exists(ManualSaveStore.SlotPath) ? ManualSaveStore.SlotPath : ManualSaveStore.LegacyPath;
                saved = System.IO.File.GetLastWriteTime(path);
            }
            when = $"{saved:M월 d일 HH:mm} · ";
        }
        catch (Exception) { }
        return $"{when}Lv {PlayerLevel.LevelIn(save)}";
    }
    public void Continue()
    {
        if (departing || SceneFadeTransition.IsRunning) return;
        if (CloudBusy()) return; // A download may be replacing the slot right now.
        try
        {
            var save = ManualSaveStore.ReadSlot();
            HideOutPlayerBridge.ValidateSnapshot(save, settings);
            if (continuePlayerPrefab == null) throw new InvalidOperationException("이어하기 플레이어 프리팹이 연결되지 않았습니다.");
            StartCoroutine(Depart(() => SaveGameSession.Continue(save, settings, continuePlayerPrefab)));
        }
        catch (Exception error) { if (status != null) status.text = "불러오기 실패: " + error.Message; }
    }
    public void NewGame()
    {
        if (departing || SceneFadeTransition.IsRunning) return;
        if (CloudBusy()) return;
        bool includeCloud = CloudAccountService.SignedIn && CloudSaveSync.ProductionSlotActive;
        if (ManualSaveStore.Exists) Ask("새 게임을 시작할까요?\n기존 저장 데이터는 바로 삭제되며 복구할 수 없습니다."
            + (includeCloud ? "\n(로그인한 계정의 저장도 함께 삭제됩니다)" : ""), Begin, "시작하기", true);
        else Begin();
    }
    private void Begin()
    {
        if (departing) return;
        // A new game replaces the old progress right away, so Continue cannot bring back the previous save.
        try { QuitAutoSave.Delete(); }
        catch (Exception error) { Debug.LogWarning("[Save Menu] 자동 저장 삭제 실패: " + error.Message); }
        if (ManualSaveStore.Exists)
        {
            bool includeCloud = CloudAccountService.SignedIn && CloudSaveSync.ProductionSlotActive;
            try { ManualSaveStore.DeleteSlot(); }
            catch (Exception error) { Refresh(); if (status != null) status.text = "기존 저장 삭제 실패: " + error.Message; return; }
            if (includeCloud) CloudSaveSync.DeleteCloudSave((ok, message) =>
            {
                if (!ok) Debug.LogWarning("[ManualSaveMenu] 새 게임: 계정 저장 삭제 실패 · " + message);
            });
            Refresh();
        }
        StartCoroutine(Depart(() =>
        {
            SaveGameSession.BeginNewGame();
            ChoosingControls = true;
            GetComponent<ControlSchemeSelectScene>()?.StartNewGame();
        }));
    }

    private IEnumerator Depart(Action transition)
    {
        departing = true;
        var group = panel.GetComponent<CanvasGroup>();
        if (group == null) group = panel.AddComponent<CanvasGroup>();
        group.interactable = false;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        var startup = GetComponent<ControlSchemeSelectScene>();
        if (startup != null) yield return startup.FadeOutMenuMusic();
        // The save screen is already black; keep it covered while starting scene travel.
        try { transition(); }
        catch (Exception error) { if (status != null) status.text = "전환 실패: " + error.Message; }
        yield return null;
        while (SceneFadeTransition.IsRunning) yield return null;
        // Successful travel destroys this menu; only failed/cancelled travel reaches here.
        departing = false;
        ChoosingControls = false;
        group.interactable = true;
        startup?.ResumeMenuMusic();
        SelectPreferredButton();
    }
    public void Delete()
    {
        if (CloudBusy()) return;
        bool includeCloud = CloudAccountService.SignedIn && CloudSaveSync.ProductionSlotActive;
        Ask(includeCloud ? "이 PC와 계정에 보관된 저장 데이터를 모두 삭제할까요?\n삭제한 진행 상황은 복구할 수 없습니다."
            : "저장 데이터를 삭제할까요?\n삭제한 진행 상황은 복구할 수 없습니다.", () =>
        {
            try { ManualSaveStore.DeleteSlot(); QuitAutoSave.Delete(); Refresh(); }
            catch (Exception error) { status.text = "삭제 실패: " + error.Message; return; }
            if (includeCloud) CloudSaveSync.DeleteCloudSave((ok, message) =>
            {
                Refresh();
                if (!ok && status != null) status.text = "이 PC 저장은 삭제했지만 계정 저장 삭제에 실패했습니다: " + message;
            });
        }, "삭제", true);
    }
    private bool CloudBusy()
    {
        // Editor save checks use an isolated Temp slot that the cloud never touches.
        if (!CloudSaveSync.ProductionSlotActive || !CloudSaveSync.Busy && !CloudAccountService.Busy) return false;
        if (status != null) status.text = "계정과 동기화하는 중입니다. 잠시 후 다시 시도하세요.";
        return true;
    }
    private void Ask(string text, Action action, string confirmText = "확인", bool destructive = false,
        string cancelText = "취소", Action onCancel = null)
    {
        if (departing) return;
        selectionBeforeConfirmation = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        var tone = destructive ? SaveConfirmGraphic.Tone.Danger : SaveConfirmGraphic.Tone.Caution;
        confirmationPanel?.SetTone(tone); confirmStyle?.SetTone(tone); cancelStyle?.SetTone(tone);
        confirmed = action; cancelled = onCancel; question.text = text;
        confirmation.transform.SetAsLastSibling(); // above the account button too, so it cannot be clicked behind the dialog
        confirmation.SetActive(true);
        confirmationPanel?.PlayIntro();
        confirmButton.GetComponentInChildren<TMP_Text>().text = confirmText;
        cancelButton.GetComponentInChildren<TMP_Text>().text = cancelText;
        SelectButton(cancelButton);
    }
    public void Confirm()
    {
        if (departing) return;
        var action = confirmed; confirmed = null; cancelled = null; confirmation.SetActive(false); action?.Invoke();
        if (panel.activeInHierarchy) SelectPreferredButton();
    }
    public void Cancel()
    {
        if (departing) return;
        var onCancel = cancelled; confirmed = null; cancelled = null; confirmation.SetActive(false);
        onCancel?.Invoke();
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
        // The account corner button joins the keyboard cycle after the slot buttons.
        var account = cloudPanel != null ? cloudPanel.CornerButton : null;
        var last = exists ? deleteButton : newGameButton;
        NavigationFor(newGameButton, account ?? last, exists ? continueButton : account ?? newGameButton);
        NavigationFor(continueButton, newGameButton, deleteButton);
        NavigationFor(deleteButton, continueButton, account ?? newGameButton);
        if (account != null) NavigationFor(account, last, newGameButton);
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
    private Transform CreateConfirmationPanel(Transform parent)
    {
        var go = new GameObject("Confirmation Panel", typeof(RectTransform), typeof(CanvasGroup), typeof(SaveConfirmGraphic));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = new Vector2(0, -10); rect.sizeDelta = new Vector2(820, 330);
        confirmationPanel = go.GetComponent<SaveConfirmGraphic>();
        confirmationPanel.Configure(SaveConfirmGraphic.Mode.Panel);
        var icon = buttonGear != null ? buttonGear.Icon : null;
        if (icon != null)
        {
            // Reuse the existing copper gear icon as a seal between the header rules.
            var seal = new GameObject("Gear Seal", typeof(RectTransform), typeof(Image));
            seal.transform.SetParent(go.transform, false);
            var image = seal.GetComponent<Image>(); image.sprite = icon; image.preserveAspect = true; image.raycastTarget = false;
            image.color = new Color(.92f, .76f, .52f);
            var sealRect = image.rectTransform; sealRect.anchorMin = sealRect.anchorMax = new Vector2(.5f, 1);
            sealRect.anchoredPosition = new Vector2(0, -45); sealRect.sizeDelta = new Vector2(30, 30);
        }
        return go.transform;
    }
    private static Button ConfirmationButton(Transform parent, string text, float x, UnityEngine.Events.UnityAction action,
        bool isConfirm, out SaveConfirmButtonStyle style)
    {
        var go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0); rect.pivot = new Vector2(.5f, 0);
        rect.anchoredPosition = new Vector2(x, 34); rect.sizeDelta = new Vector2(240, 58);
        var label = Label(go.transform, text, 0, 24); label.rectTransform.sizeDelta = new Vector2(220, 40);
        var button = go.GetComponent<Button>(); button.onClick.AddListener(action);
        MenuButtonSound.Attach(button);
        style = go.AddComponent<SaveConfirmButtonStyle>(); style.Configure(label, isConfirm);
        return button;
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
        MenuButtonSound.Attach(button);
        if (metalPlate)
        {
            var style = go.AddComponent<SaveSlotButtonStyle>();
            style.Configure(label, buttonPlateSprite, buttonGear != null ? buttonGear.Icon : null, destructive);
            style.SetCornerRadius(30f); // nameplate: almost a capsule
        }
        return button;
    }
}
