using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Adds status to the authored skill slots; never moves or resizes those slots.</summary>
[DisallowMultipleComponent]
public sealed class InGameSkillHUD : MonoBehaviour
{
    private PlayerSkills skills;
    private PlayerMana mana;
    private PlayerGrenadeSkill grenades;
    private TMP_Text grenadeNotice;
    private readonly TMP_Text[] titles = new TMP_Text[2];
    private readonly TMP_Text[] timers = new TMP_Text[2];
    private readonly TMP_Text[] costs = new TMP_Text[2];
    private readonly Image[] cooldowns = new Image[2];
    private float nextSearch;

    private void Awake()
    {
        BuildSlot(0, "Skill_ZQ");
        BuildSlot(1, "Skill_EX");
        var notice = new GameObject("Grenade Notice", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(Shadow));
        notice.transform.SetParent(transform, false);
        var noticeRect = notice.GetComponent<RectTransform>();
        noticeRect.anchorMin = noticeRect.anchorMax = new Vector2(.5f, 0);
        noticeRect.anchoredPosition = new Vector2(0, 150); noticeRect.sizeDelta = new Vector2(650, 52);
        grenadeNotice = notice.GetComponent<TextMeshProUGUI>();
        UIFontTheme.Apply(grenadeNotice, UIFontTheme.Bold); grenadeNotice.fontSize = 24;
        grenadeNotice.alignment = TextAlignmentOptions.Center; grenadeNotice.raycastTarget = false;
        notice.GetComponent<Shadow>().effectColor = new Color(0, 0, 0, .9f);
        notice.GetComponent<Shadow>().effectDistance = new Vector2(2, -2);
        notice.SetActive(false);
    }

    private void BuildSlot(int index, string name)
    {
        var slot = transform.Find(name) as RectTransform;
        if (slot == null) return;
        var icon = slot.GetComponent<Image>();
        if (icon != null) icon.raycastTarget = false;
        var overlay = new GameObject("Skill Cooldown", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(slot, false);
        var rect = overlay.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        cooldowns[index] = overlay.GetComponent<Image>();
        cooldowns[index].sprite = icon != null ? icon.sprite : null;
        cooldowns[index].color = new Color(0f, 0f, 0f, .72f);
        cooldowns[index].type = Image.Type.Filled;
        cooldowns[index].fillMethod = Image.FillMethod.Radial360;
        cooldowns[index].raycastTarget = false;
        titles[index] = Label(slot, "Skill Name", 1f, 12f, 21);
        timers[index] = Label(slot, "Skill Timer", .5f, 0f, 25);
        costs[index] = Label(slot, "Skill MP", 0f, -12f, 17);
    }

    private static TMP_Text Label(RectTransform slot, string name, float anchorY, float y, int fontSize)
    {
        var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(slot, false);
        var rect = label.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, anchorY);
        rect.sizeDelta = new Vector2(166, 30);
        rect.anchoredPosition = new Vector2(0f, y);
        var text = label.GetComponent<TextMeshProUGUI>();
        UIFontTheme.Apply(text, UIFontTheme.Bold);
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    private void LateUpdate()
    {
        if (skills == null && Time.unscaledTime >= nextSearch)
        {
            nextSearch = Time.unscaledTime + .25f;
            var target = PlayerTargetRegistry.Current;
            skills = target != null ? target.GetComponent<PlayerSkills>() : FindFirstObjectByType<PlayerSkills>();
            mana = skills != null ? skills.GetComponent<PlayerMana>() : null;
            grenades = skills != null ? skills.GetComponent<PlayerGrenadeSkill>() : null;
        }
        bool wasd = PlayerControlSchemeSettings.Current == PlayerControlScheme.WasdMouseQe;
        RefreshSlot(0, wasd ? "Q 은신" : "C 은신", skills != null ? skills.StealthCooldownRemaining : 0f,
            skills != null ? skills.StealthCooldown : 12f, skills != null ? skills.StealthManaCost : 15);
        RefreshGrenadeSlot(wasd);
    }

    private void RefreshGrenadeSlot(bool wasd)
    {
        if (titles[1] != null)
        {
            titles[1].text = wasd ? "E 수류탄" : "V 수류탄";
            timers[1].text = grenades == null ? "" : grenades.IsAiming ? $"{grenades.AimAngle:0}° · 놓아 투척"
                : grenades.Remaining == 0 ? "소진" : grenades.CooldownRemaining > 0 ? $"{grenades.CooldownRemaining:0.0}s" : "누르고 조준";
            timers[1].fontSize = 19;
            costs[1].text = grenades != null ? $"수류탄 {grenades.Remaining}/{grenades.Capacity}" : "수류탄 —/5";
            costs[1].color = grenades != null && grenades.Remaining == 0 ? new Color(1, .45f, .4f) : new Color(1, .8f, .4f);
            cooldowns[1].fillAmount = grenades == null ? 0 : grenades.CooldownRemaining / Mathf.Max(.01f, grenades.Cooldown);
        }
        bool showNotice = grenades != null && grenades.NoticeRemaining > 0;
        grenadeNotice.gameObject.SetActive(showNotice);
        if (showNotice)
        {
            grenadeNotice.text = grenades.Notice;
            grenadeNotice.color = new Color(1, .85f, .55f, Mathf.Clamp01(grenades.NoticeRemaining / .25f));
        }
    }

    private void RefreshSlot(int index, string title, float remaining, float cooldown, int cost)
    {
        if (titles[index] == null) return;
        titles[index].text = title;
        bool active = index == 0 && skills != null && skills.IsStealthed;
        bool insufficient = mana != null && mana.CurrentMana < cost;
        timers[index].text = active ? $"은신 {skills.StealthRemaining:0.0}" : remaining > 0f ? $"{remaining:0.0}s"
            : skills == null || !skills.CanUseSkills ? "" : insufficient ? "MP 부족" : "준비";
        timers[index].color = active ? new Color(.8f, .67f, 1f) : Color.white;
        costs[index].text = $"MP {cost}";
        costs[index].color = insufficient ? new Color(1f, .45f, .4f) : new Color(.65f, .85f, 1f);
        cooldowns[index].fillAmount = remaining / Mathf.Max(.01f, cooldown);
    }
}
