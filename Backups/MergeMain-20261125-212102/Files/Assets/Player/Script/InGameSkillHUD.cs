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
    private PlayerAttackController actions;
    private PlayerPoisonDaggerSkill daggers;
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
            actions = skills != null ? skills.GetComponent<PlayerAttackController>() : null;
            daggers = skills != null ? skills.GetComponent<PlayerPoisonDaggerSkill>() : null;
        }

        bool shockwave = actions != null && actions.SelectedPrimarySkill == PlayerAttackController.PrimarySkill.Shockwave;
        RefreshSlot(0, "Q " + (shockwave ? "충격파" : "은신"),
            skills == null ? 0 : shockwave ? skills.ShockwaveCooldownRemaining : skills.StealthCooldownRemaining,
            skills == null ? 12 : shockwave ? skills.ShockwaveCooldown : skills.StealthCooldown,
            skills == null ? 15 : shockwave ? skills.ShockwaveManaCost : skills.StealthManaCost);
        if (actions != null && actions.SelectedThrowSkill == PlayerAttackController.ThrowSkill.PoisonDaggers) RefreshDaggerSlot();
        else RefreshGrenadeSlot();
    }

    private void RefreshDaggerSlot()
    {
        if (titles[1] != null)
        {
            float remaining = daggers != null ? daggers.CooldownRemaining : 0;
            titles[1].text = "E 단검 투척";
            timers[1].fontSize = 19; timers[1].text = remaining > 0 ? $"{remaining:0.0}s" : "준비";
            float duration = daggers != null && daggers.Settings != null ? daggers.Settings.poisonDuration : 4;
            costs[1].text = "MP 10%"; costs[1].color = new Color(.55f, .9f, .6f);
            cooldowns[1].fillAmount = daggers == null ? 0 : remaining / Mathf.Max(.01f, daggers.Cooldown);
        }
        if (grenadeNotice != null) grenadeNotice.gameObject.SetActive(false);
    }

    private void RefreshGrenadeSlot()
    {
        if (titles[1] != null)
        {
            titles[1].text = "E 수류탄";
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
        bool active = index == 0 && skills != null && skills.IsStealthed
            && (actions == null || actions.SelectedPrimarySkill == PlayerAttackController.PrimarySkill.Stealth);
        bool insufficient = mana != null && mana.CurrentMana < cost;
        var slotIcon = cooldowns[index].transform.parent.GetComponent<Image>();
        if (slotIcon != null) slotIcon.color = insufficient && !active ? new Color(.7f, .7f, .7f, 1) : Color.white;
        timers[index].text = active ? "은신 중" : remaining > 0f ? $"{remaining:0.0}s"
            : skills == null || !skills.CanUseSkills ? "" : insufficient ? "MP 부족" : "준비";
        timers[index].color = active ? new Color(.8f, .67f, 1f) : Color.white;
        costs[index].text = $"MP {cost}";
        if (index == 0 && skills != null && (actions == null || actions.SelectedPrimarySkill == PlayerAttackController.PrimarySkill.Stealth))
            costs[index].text = $"MP {skills.StealthDrainPercent:0.#}% / 0.5초";
        costs[index].color = insufficient ? new Color(1f, .45f, .4f) : new Color(.65f, .85f, 1f);
        cooldowns[index].fillAmount = remaining / Mathf.Max(.01f, cooldown);
    }
}
