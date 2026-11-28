using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// "계정 연동" button (verdigris capsule with a cloud icon, under the slot buttons) + dialog on the existing save menu. Built at runtime like ManualSaveMenu,
/// reusing its dialog/button styles. Owns no save data: it only drives CloudAccountService/CloudSaveSync
/// and asks the menu to Refresh() after the local slot may have changed.
/// </summary>
public sealed class CloudAccountPanel : MonoBehaviour
{
    private enum View { SignedOut, SignedIn, Conflict }

    private ManualSaveMenu menu;
    private Button cornerButton;
    private TMP_Text cornerLabel;
    private SaveSlotButtonStyle cornerStyle;
    private GameObject overlay;
    private SaveConfirmGraphic dialogGraphic;
    private TMP_Text title, body, feedback;
    private TMP_InputField usernameInput, passwordInput;
    private GameObject signedOutGroup, signedInGroup, conflictGroup;
    private Button signInButton, signUpButton, closeOutButton;
    private Button syncButton, signOutButton, closeInButton;
    private Button useCloudButton, useLocalButton, laterButton;
    private View view;
    private bool built;
    private string cornerNotice; // e.g. offline at startup while a stored login exists.
    private string flashText;    // Short corner message ("계정 연동 완료", "로그아웃 완료") shown for a few seconds.
    private float flashUntil;
    private bool announceLink;   // Show "계정 연동 완료" once the first sync after sign-in finishes.
    private GameObject lastDialogSelection;

    public Button CornerButton => cornerButton;
    public bool IsOpen => overlay != null && overlay.activeSelf;

    /// <summary>Called by ManualSaveMenu.Start after its own panel exists.</summary>
    public void Build(ManualSaveMenu owner, Transform menuPanel, Sprite plateSprite, Sprite gearSprite)
    {
        if (built) return;
        built = true; menu = owner;
        BuildCorner(menuPanel, plateSprite, gearSprite);
        BuildDialog(menuPanel);
        CloudAccountService.Changed += StateChanged;
        CloudSaveSync.Changed += StateChanged;
        StateChanged();
        if (!CloudSaveSync.ProductionSlotActive) return; // Editor save checks: no network, no account work.
        if (CloudSaveConfig.Current.autoSyncOnMenu && !CloudAccountService.SignedIn && CloudAccountService.HasStoredSession)
            CloudAccountService.ResumeStoredSession((ok, message) =>
            {
                if (ok) RunSync();
                else if (!string.IsNullOrEmpty(message)) { SetFeedback(message); cornerNotice = message; StateChanged(); }
            });
        else if (CloudAccountService.SignedIn && CloudSaveConfig.Current.autoSyncOnMenu) RunSync();
    }

    private void OnDestroy()
    {
        CloudAccountService.Changed -= StateChanged;
        CloudSaveSync.Changed -= StateChanged;
    }

    public void Open()
    {
        if (overlay == null || Working) return;
        overlay.transform.SetAsLastSibling();
        overlay.SetActive(true);
        lastDialogSelection = null;
        dialogGraphic.PlayIntro();
        SetFeedback(CloudSaveSync.StatusText);
        ShowView(CloudSaveSync.PendingConflict != null && CloudAccountService.SignedIn ? View.Conflict
            : CloudAccountService.SignedIn ? View.SignedIn : View.SignedOut);
    }

    public void Close()
    {
        if (overlay == null || Working) return;
        if (view == View.Conflict) CloudSaveSync.PostponeConflict();
        overlay.SetActive(false);
        if (passwordInput != null) passwordInput.text = "";
        if (EventSystem.current != null && cornerButton != null && cornerButton.gameObject.activeInHierarchy)
            EventSystem.current.SetSelectedGameObject(cornerButton.gameObject);
        menu?.Refresh();
    }

    private static bool Working => CloudAccountService.Busy || CloudSaveSync.Busy;

    // ---------- Actions ----------
    private void SignIn() => Credential(false);
    private void SignUp() => Credential(true);
    private void Credential(bool signUp)
    {
        if (Working) return;
        string username = usernameInput.text, password = passwordInput.text;
        Action<bool, string> done = (ok, message) =>
        {
            SetFeedback(message);
            if (!ok) { Select(passwordInput); return; }
            passwordInput.text = "";
            announceLink = true;
            RunSync();
        };
        SetFeedback(signUp ? "계정을 만드는 중..." : "로그인하는 중...");
        if (signUp) CloudAccountService.SignUp(username, password, done);
        else CloudAccountService.SignIn(username, password, done);
        StateChanged();
    }

    private void RunSync()
    {
        CloudSaveSync.SyncNow((action, message) =>
        {
            SetFeedback(message);
            if (announceLink) { announceLink = false; Flash("계정 연동 완료"); }
            menu?.Refresh(); // A download replaces the local slot that Continue reads.
            if (action == CloudSyncAction.AskUser)
            {
                if (!IsOpen) Open();
                else ShowView(View.Conflict);
            }
            else if (IsOpen) ShowView(CloudAccountService.SignedIn ? View.SignedIn : View.SignedOut);
        });
        StateChanged();
    }

    private void SignOut()
    {
        if (Working) return;
        CloudAccountService.SignOut();
        SetFeedback("로그아웃 완료 · 이 기기의 진행 기록은 그대로 남아 있습니다.");
        Flash("로그아웃 완료");
        ShowView(View.SignedOut);
    }

    private void Resolve(bool useCloud)
    {
        if (Working) return;
        SetFeedback(useCloud ? "계정 기록을 내려받는 중..." : "이 PC 기록을 계정에 올리는 중...");
        CloudSaveSync.ResolveConflict(useCloud, (action, message) =>
        {
            SetFeedback(message);
            menu?.Refresh();
            ShowView(CloudSaveSync.PendingConflict != null ? View.Conflict : View.SignedIn);
        });
        StateChanged();
    }

    // ---------- State → UI ----------
    private void StateChanged()
    {
        if (!built) return;
        bool working = Working;
        if (CloudAccountService.SignedIn) cornerNotice = null;
        if (cornerLabel != null) cornerLabel.text = CornerText(working);
        if (cornerStyle != null) // amber when the account needs the player: a conflict to resolve or a failed sign-in
            cornerStyle.SetAttention(!working && (CloudAccountService.SignedIn ? CloudSaveSync.PendingConflict != null
                : !string.IsNullOrEmpty(cornerNotice)));
        foreach (var button in new[] { signInButton, signUpButton, syncButton, signOutButton, useCloudButton, useLocalButton, laterButton, closeOutButton, closeInButton })
            if (button != null) button.interactable = !working;
        if (usernameInput != null) usernameInput.interactable = !working;
        if (passwordInput != null) passwordInput.interactable = !working;
        if (!working && view == View.Conflict) ApplyConflictText();
        if (IsOpen && view == View.SignedIn && body != null) body.text = SignedInText();
    }

    private void ShowView(View next)
    {
        view = next;
        signedOutGroup.SetActive(next == View.SignedOut);
        signedInGroup.SetActive(next == View.SignedIn);
        conflictGroup.SetActive(next == View.Conflict);
        dialogGraphic.SetTone(next == View.Conflict ? SaveConfirmGraphic.Tone.Danger : SaveConfirmGraphic.Tone.Caution);
        switch (next)
        {
            case View.SignedOut:
                title.text = "계정 연동";
                body.text = "계정에 로그인하면 세이브 포인트에서 저장한 진행이 계정에도 보관됩니다.\n다른 PC에서 같은 계정으로 로그인하면 그대로 이어집니다.";
                Select(string.IsNullOrEmpty(usernameInput.text) ? usernameInput : passwordInput);
                break;
            case View.SignedIn:
                title.text = "계정 연동";
                body.text = SignedInText();
                Select(syncButton);
                break;
            case View.Conflict:
                title.text = "어느 기록으로 이어갈까요?";
                ApplyConflictText();
                Select(laterButton);
                break;
        }
        StateChanged();
    }

    private static string SignedInText()
        => $"<b>{CloudAccountService.Username}</b> 계정과 연동되어 있습니다.\n세이브 포인트에서 저장할 때마다 계정에도 자동으로 올라갑니다.";

    private void ApplyConflictText()
    {
        var conflict = CloudSaveSync.PendingConflict;
        if (conflict == null || body == null) return;
        string cloud = !conflict.cloudExists ? "계정: 저장 없음 (다른 PC에서 삭제되었을 수 있음)"
            : conflict.cloudUsable ? $"계정: {CloudSaveSync.FormatLocal(conflict.cloudSavedUtc)} 저장" + (conflict.cloudDevice.Length > 0 ? $" · {conflict.cloudDevice}" : "")
            : "계정: " + conflict.cloudProblem;
        string local = !conflict.localExists ? "이 PC: 저장 없음 (이 PC에서 삭제됨)"
            : conflict.localUsable ? $"이 PC: {CloudSaveSync.FormatLocal(conflict.localSavedUtc)} 저장"
            : "이 PC: " + conflict.localProblem;
        body.text = $"{cloud}\n{local}\n<size=80%>고르지 않은 쪽 기록은 덮어씁니다. 이 PC 기록은 백업 폴더에 남겨 둡니다.</size>";
        if (useCloudButton != null) useCloudButton.gameObject.SetActive(conflict.CanUseCloud);
        if (useLocalButton != null) useLocalButton.gameObject.SetActive(conflict.CanUseLocal);
        LinkRow(conflictGroup.GetComponentsInChildren<Button>(false));
    }

    private void SetFeedback(string text) { if (feedback != null) feedback.text = text ?? ""; }

    /// <summary>One short game-style word on the corner button; details stay inside the dialog.</summary>
    private string CornerText(bool working)
    {
        if (working) return "연동 중...";
        if (Time.unscaledTime < flashUntil) return flashText;
        if (CloudAccountService.SignedIn) return CloudSaveSync.PendingConflict != null ? "계정 확인 필요" : "계정 연동됨";
        return string.IsNullOrEmpty(cornerNotice) ? "계정 연동하기" : "계정 연결 실패";
    }

    private void Flash(string text)
    {
        flashText = text; flashUntil = Time.unscaledTime + 3f;
        StateChanged();
    }

    private void Update()
    {
        if (flashUntil > 0 && Time.unscaledTime >= flashUntil) { flashUntil = 0; StateChanged(); }
        if (!IsOpen) return;
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { Close(); return; }
        // Keep keyboard/gamepad focus inside the dialog; the menu behind may re-select its buttons on Refresh().
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected != null && selected.activeInHierarchy && selected.transform.IsChildOf(overlay.transform)) { lastDialogSelection = selected; return; }
        var previous = lastDialogSelection != null && lastDialogSelection.activeInHierarchy ? lastDialogSelection.GetComponent<Selectable>() : null;
        Select(previous != null && previous.IsInteractable() ? previous : DefaultSelectable());
    }

    private Selectable DefaultSelectable() => view switch
    {
        View.SignedOut => usernameInput,
        View.SignedIn => syncButton,
        _ => laterButton,
    };

    private static void Select(Selectable target)
    {
        if (EventSystem.current != null && target != null && target.gameObject.activeInHierarchy)
            EventSystem.current.SetSelectedGameObject(target.gameObject);
    }

    // ---------- Construction ----------
    private void BuildCorner(Transform parent, Sprite plateSprite, Sprite gearSprite)
    {
        var go = new GameObject("Cloud Account Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        // Under the three slot buttons (they end at 614 in the 1600x900 menu), centred: inside the calm middle column of
        // the backdrop, where players look anyway, and next in the keyboard order after 저장 삭제.
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1);
        rect.anchoredPosition = new Vector2(0, -638); rect.sizeDelta = new Vector2(284, 54);
        go.GetComponent<Image>().color = new Color(.34f, .17f, .12f);
        cornerLabel = Label(go.transform, "계정 연동하기", 20, TextAlignmentOptions.Center);
        cornerButton = go.GetComponent<Button>();
        cornerButton.onClick.AddListener(Open);
        cornerStyle = go.AddComponent<SaveSlotButtonStyle>();
        cornerStyle.ConfigureAccount(cornerLabel, CloudIcon());
    }

    private static Sprite cloudIcon;
    // 15x9 pixel cloud with an upload arrow; white so the button style can tint it.
    private static Sprite CloudIcon()
    {
        if (cloudIcon != null) return cloudIcon;
        string[] rows =
        {
            ".....ooooo.....",
            "....o#####o....",
            "....o##o##o....",
            "..oo##ooo##oo..",
            ".o###o#o#o###o.",
            ".o#####o#####o.",
            ".o#####o#####o.",
            "..o####o####o..",
            "..ooooooooooo..",
        };
        int width = rows[0].Length, height = rows.Length;
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                char c = rows[height - 1 - y][x]; // texture rows are bottom-up
                pixels[y * width + x] = c == '#' ? new Color32(255, 255, 255, 255)
                    : c == 'o' ? new Color32(70, 92, 88, 255) : new Color32(0, 0, 0, 0);
            }
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Account Cloud Icon" };
        texture.SetPixels32(pixels);
        texture.Apply(false);
        cloudIcon = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f), 1f);
        cloudIcon.name = "Account Cloud Icon";
        return cloudIcon;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => cloudIcon = null; // domain reload is disabled in this project

    private void BuildDialog(Transform parent)
    {
        overlay = new GameObject("Cloud Account Dialog", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(parent, false);
        Stretch((RectTransform)overlay.transform);
        overlay.GetComponent<Image>().color = new Color(.022f, .012f, .009f, .88f); // Blocks clicks to the menu.

        var dialog = new GameObject("Cloud Account Panel", typeof(RectTransform), typeof(CanvasGroup), typeof(SaveConfirmGraphic));
        dialog.transform.SetParent(overlay.transform, false);
        var rect = (RectTransform)dialog.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(860, 560);
        dialogGraphic = dialog.GetComponent<SaveConfirmGraphic>();
        dialogGraphic.Configure(SaveConfirmGraphic.Mode.Panel);

        title = Label(dialog.transform, "계정 연동", 28, TextAlignmentOptions.Center);
        Place(title.rectTransform, 0, -44, 760, 44);
        title.color = new Color(.93f, .86f, .74f);
        body = Label(dialog.transform, "", 21, TextAlignmentOptions.Center);
        Place(body.rectTransform, 0, -100, 760, 120);
        body.color = new Color(.86f, .80f, .70f);
        body.lineSpacing = 12f;
        body.textWrappingMode = TextWrappingModes.Normal;
        feedback = Label(dialog.transform, "", 19, TextAlignmentOptions.Center);
        Place(feedback.rectTransform, 0, -390, 760, 60);
        feedback.color = new Color(.92f, .70f, .45f);
        feedback.textWrappingMode = TextWrappingModes.Normal;

        signedOutGroup = Group(dialog.transform, "Signed Out");
        usernameInput = CreateInput(signedOutGroup.transform, "아이디 (영문 소문자·숫자 3~20자)", -232, false);
        passwordInput = CreateInput(signedOutGroup.transform, "비밀번호 (8~30자, 대·소문자·숫자·특수문자)", -298, true);
        signInButton = DialogButton(signedOutGroup.transform, "로그인", -250, SignIn, true);
        signUpButton = DialogButton(signedOutGroup.transform, "계정 만들기", 0, SignUp, false);
        closeOutButton = DialogButton(signedOutGroup.transform, "닫기", 250, Close, false);
        passwordInput.onSubmit.AddListener(_ => { if (view == View.SignedOut) SignIn(); });
        LinkColumn(usernameInput, passwordInput, signInButton);
        LinkRow(new[] { signInButton, signUpButton, closeOutButton }, passwordInput);

        signedInGroup = Group(dialog.transform, "Signed In");
        syncButton = DialogButton(signedInGroup.transform, "지금 동기화", -250, RunSync, true);
        signOutButton = DialogButton(signedInGroup.transform, "로그아웃", 0, SignOut, false);
        closeInButton = DialogButton(signedInGroup.transform, "닫기", 250, Close, false);
        LinkRow(new[] { syncButton, signOutButton, closeInButton });

        conflictGroup = Group(dialog.transform, "Conflict");
        useCloudButton = DialogButton(conflictGroup.transform, "계정 기록 사용", -250, () => Resolve(true), true);
        useLocalButton = DialogButton(conflictGroup.transform, "이 PC 기록 사용", 0, () => Resolve(false), true);
        laterButton = DialogButton(conflictGroup.transform, "나중에", 250, Close, false);

        overlay.SetActive(false);
        view = View.SignedOut;
    }

    private static GameObject Group(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Stretch((RectTransform)go.transform);
        return go;
    }

    private static TMP_Text Label(Transform parent, string value, int size, TextAlignmentOptions alignment)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var label = go.GetComponent<TMP_Text>();
        UIFontTheme.Apply(label, UIFontTheme.Normal);
        label.text = value; label.fontSize = size; label.alignment = alignment; label.raycastTarget = false;
        label.richText = true;
        var rect = label.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return label;
    }

    private static void Place(RectTransform rect, float x, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1); rect.pivot = new Vector2(.5f, 1);
        rect.anchoredPosition = new Vector2(x, top); rect.sizeDelta = new Vector2(width, height);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private TMP_InputField CreateInput(Transform parent, string placeholderText, float top, bool password)
    {
        var go = new GameObject(password ? "Password" : "Username", typeof(RectTransform), typeof(Image));
        go.SetActive(false); // Wire the TMP_InputField before it enables.
        go.transform.SetParent(parent, false);
        Place((RectTransform)go.transform, 0, top, 560, 52);
        go.GetComponent<Image>().color = new Color(.10f, .06f, .05f, .95f);

        var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        area.transform.SetParent(go.transform, false);
        var areaRect = (RectTransform)area.transform; Stretch(areaRect);
        areaRect.offsetMin = new Vector2(18, 6); areaRect.offsetMax = new Vector2(-18, -6);

        var placeholder = Label(area.transform, placeholderText, 18, TextAlignmentOptions.MidlineLeft);
        placeholder.color = new Color(.62f, .55f, .47f, .8f);
        var text = Label(area.transform, "", 22, TextAlignmentOptions.MidlineLeft);
        text.color = new Color(.95f, .90f, .82f);

        var input = go.AddComponent<TMP_InputField>();
        input.textViewport = areaRect;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.fontAsset = UIFontTheme.Normal;
        input.pointSize = 22;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = password ? 30 : 20;
        input.contentType = password ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
        if (password) input.asteriskChar = '*';
        input.caretColor = new Color(.95f, .80f, .55f);
        input.selectionColor = new Color(.86f, .62f, .30f, .35f);
        input.targetGraphic = go.GetComponent<Image>();
        go.SetActive(true);
        return input;
    }

    private static Button DialogButton(Transform parent, string text, float x, UnityEngine.Events.UnityAction action, bool confirm)
    {
        var go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0); rect.pivot = new Vector2(.5f, 0);
        rect.anchoredPosition = new Vector2(x, 34); rect.sizeDelta = new Vector2(230, 58);
        var label = Label(go.transform, text, 22, TextAlignmentOptions.Center);
        var button = go.GetComponent<Button>();
        button.onClick.AddListener(action);
        go.AddComponent<SaveConfirmButtonStyle>().Configure(label, confirm);
        return button;
    }

    private static void LinkColumn(params Selectable[] items)
    {
        for (int i = 0; i < items.Length; i++)
        {
            var nav = items[i].navigation; nav.mode = Navigation.Mode.Explicit;
            nav.selectOnUp = items[(i + items.Length - 1) % items.Length];
            nav.selectOnDown = items[(i + 1) % items.Length];
            items[i].navigation = nav;
        }
    }

    private static void LinkRow(Button[] row, Selectable above = null)
    {
        for (int i = 0; i < row.Length; i++)
        {
            var nav = new Navigation { mode = Navigation.Mode.Explicit,
                selectOnLeft = row[(i + row.Length - 1) % row.Length], selectOnRight = row[(i + 1) % row.Length],
                selectOnUp = above, selectOnDown = above };
            row[i].navigation = nav;
        }
    }
}
