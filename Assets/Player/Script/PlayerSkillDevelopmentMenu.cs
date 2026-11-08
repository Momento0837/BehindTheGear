using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#endif

/// <summary>Editor/development-only equipment workbench. Does not change production saves or refill skills.</summary>
[DefaultExecutionOrder(-100)]
public sealed class PlayerSkillDevelopmentMenu : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState() => IsOpen = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private PlayerAttackController actions;
    private PlayerControlLock controlLock;
    private GameObject canvasRoot, throwPage, primaryPage;
    private TMP_Text selectedLabel;
    private bool ownsLock;
    private void Awake() { actions = GetComponent<PlayerAttackController>(); controlLock = GetComponent<PlayerControlLock>(); }
    private void OnEnable() => SceneManager.sceneLoaded += SceneLoaded;
    private void OnDisable() { Close(); SceneManager.sceneLoaded -= SceneLoaded; }
    private void OnDestroy() { if (canvasRoot != null) Destroy(canvasRoot); }
    private void SceneLoaded(Scene scene, LoadSceneMode mode) => Close();
    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.kKey.wasPressedThisFrame)
        {
            if (ownsLock) Close(); else Open();
        }
        if (ownsLock && (keyboard.escapeKey.wasPressedThisFrame || GetComponent<PlayerHealth>()?.IsDead == true)) Close();
        if (ownsLock && selectedLabel != null)
            selectedLabel.text = $"Q / C  ·  {(actions.SelectedPrimarySkill == PlayerAttackController.PrimarySkill.Stealth ? "은신" : "충격파")}     E / V  ·  {(actions.SelectedThrowSkill == PlayerAttackController.ThrowSkill.PoisonDaggers ? "독 단검" : "수류탄")}";
    }
    public bool Open()
    {
        if (IsOpen || actions == null || controlLock == null || controlLock.IsLocked || SceneFadeTransition.IsRunning
            || GetComponent<PlayerHealth>()?.IsDead == true || GetComponent<Rigidbody2D>()?.simulated != true) return false;
        if (canvasRoot == null) Build();
        GetComponent<PlayerGrenadeSkill>()?.CancelAim();
        controlLock.Acquire(); ownsLock = true; IsOpen = true;
        canvasRoot.SetActive(true); ShowPage(true); return true;
    }
    public void Close()
    {
        if (!ownsLock) return;
        ownsLock = false; IsOpen = false;
        if (canvasRoot != null) canvasRoot.SetActive(false);
        PlayerInputReader.SuppressPointerForCurrentFrame();
        if (controlLock != null) controlLock.Release();
    }
    private void ShowPage(bool throwing)
    { throwPage.SetActive(throwing); primaryPage.SetActive(!throwing); }
    private void Build()
    {
        canvasRoot = new GameObject("Development Skill Workbench", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasRoot.transform.SetParent(transform, false);
        var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32000;
        var scaler = canvasRoot.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
        var dim = Panel(canvasRoot.transform, "Dim", 0, 0, 1600, 900, new Color(0, .01f, .02f, .8f));
        var rect = dim.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var window = Panel(canvasRoot.transform, "Skill workbench", 310, 150, 980, 600, new Color(.035f, .055f, .075f, .99f));
        var outline = window.gameObject.AddComponent<Outline>(); outline.effectColor = new Color(.23f, .53f, .52f); outline.effectDistance = new Vector2(1, -1);
        Label(window.transform, "개발용 스킬 실험실", 30, 20, 700, 45, 30, Color.white);
        Label(window.transform, "EDITOR / DEVELOPMENT ONLY · 저장 데이터와 탄약은 유지됩니다", 32, 69, 860, 30, 15, new Color(.5f, .68f, .73f));
        Button(window.transform, "Close", "K · 닫기", 820, 24, 130, 44, Close);
        Panel(window.transform, "Divider", 26, 112, 928, 2, new Color(.15f, .28f, .3f));
        Button(window.transform, "Throw category", "투척 스킬", 30, 140, 190, 64, () => ShowPage(true));
        Button(window.transform, "Primary category", "근접 / 보조", 30, 218, 190, 64, () => ShowPage(false));
        Label(window.transform, "장착 후 창을 닫으면\n스킬 키로 사용합니다.\n\n수류탄 탄약·쿨타임은\n교체해도 유지됩니다.", 32, 316, 185, 178, 15, new Color(.58f, .7f, .73f));
        selectedLabel = Label(window.transform, "", 30, 544, 920, 34, 17, new Color(.5f, .93f, .68f));
        throwPage = Page(window.transform, "Throw skills");
        Label(throwPage.transform, "투척 · E / V", 0, 0, 660, 35, 24, Color.white);
        Card(throwPage.transform, "Equip poison daggers", "독 단검 · 3연 편대", "구현 완료  ·  직선 투척 / 독 / 쿨타임 7초\n중앙이 앞서고 두 단검이 나란히 뒤따릅니다.", 0,
            () => { actions.SelectThrowSkill(PlayerAttackController.ThrowSkill.PoisonDaggers); });
        Card(throwPage.transform, "Equip grenade", "수류탄 · 보존된 투척 스킬", "구현 완료  ·  누르고 조준, 놓아서 투척\n기존 탄약 · 포물선 · 충돌 폭발을 유지합니다.", 170,
            () => { actions.SelectThrowSkill(PlayerAttackController.ThrowSkill.Grenade); });
        primaryPage = Page(window.transform, "Primary skills");
        Label(primaryPage.transform, "근접 / 보조 · Q / C", 0, 0, 660, 35, 24, Color.white);
        Card(primaryPage.transform, "Equip stealth", "은신", "구현 완료  ·  기존 은신과 MP / 쿨타임 사용\n기본 근접 공격과 Shift 대쉬는 그대로 유지합니다.", 0,
            () => actions.SelectPrimarySkill(PlayerAttackController.PrimarySkill.Stealth));
        Card(primaryPage.transform, "Equip shockwave", "충격파 · 보관된 근접 범위 스킬", "구현 완료  ·  기존 범위 피해 / 밀치기\n새 스킬을 만드는 대신 기존 구현을 다시 연결합니다.", 170,
            () => actions.SelectPrimarySkill(PlayerAttackController.PrimarySkill.Shockwave));
        canvasRoot.SetActive(false);
    }
    private static GameObject Page(Transform parent, string name)
    { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); Place(go.GetComponent<RectTransform>(), 252, 139, 690, 390); return go; }
    private static void Card(Transform parent, string name, string title, string description, float y, UnityEngine.Events.UnityAction select)
    {
        var card = Panel(parent, name + " card", 0, y + 48, 690, 150, new Color(.065f, .105f, .125f));
        Panel(card.transform, "Accent", 0, 0, 4, 150, new Color(.3f, .8f, .55f));
        Label(card.transform, title, 20, 15, 500, 32, 21, Color.white);
        Label(card.transform, description, 20, 57, 505, 73, 16, new Color(.63f, .77f, .8f));
        Button(card.transform, name, "장착", 556, 50, 110, 50, select);
    }
    private static void Place(RectTransform rect, float x, float y, float width, float height)
    { rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); }
    private static Image Panel(Transform parent, string name, float x, float y, float width, float height, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = true; Place(image.rectTransform, x, y, width, height); return image;
    }
    private static TMP_Text Label(Transform parent, string value, float x, float y, float width, float height, int size, Color color)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>(); UIFontTheme.Apply(text, size >= 24 ? UIFontTheme.ExtraBold : UIFontTheme.Normal);
        text.fontSize = size; text.color = color; text.text = value; text.raycastTarget = false;
        Place(text.rectTransform, x, y, width, height); return text;
    }
    private static void Button(Transform parent, string name, string title, float x, float y, float width, float height, UnityEngine.Events.UnityAction action)
    {
        var image = Panel(parent, name, x, y, width, height, new Color(.12f, .28f, .29f));
        var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(action);
        Label(image.transform, title, 6, 6, width - 12, height - 12, 18, Color.white).alignment = TextAlignmentOptions.Center;
    }
#endif
}
