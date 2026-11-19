using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Adds status to the authored skill slots; never moves or resizes those slots.</summary>
[DisallowMultipleComponent]
public sealed class InGameSkillHUD : MonoBehaviour
{
    private const float ReadyFlashSeconds = .45f;
    private static readonly Color TitleColor = new(.95f, .89f, .78f);
    private static readonly Color HintColor = new(.86f, .80f, .70f);
    private static readonly Color BlockedColor = new(1f, .52f, .45f);

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
    private readonly SkillSlotIconGraphic[] emblems = new SkillSlotIconGraphic[2];
    private readonly float[] previousRemaining = new float[2];
    private readonly float[] readyFlashAt = { -10f, -10f };
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
        if (icon != null)
        {
            icon.raycastTarget = false;
            // The authored sprite is a flat placeholder; the emblem below draws the face. Keep the Image for its sprite/rect.
            icon.color = Color.clear;
        }
        var emblem = new GameObject("Skill Emblem", typeof(RectTransform), typeof(SkillSlotIconGraphic));
        emblem.transform.SetParent(slot, false);
        var emblemRect = emblem.GetComponent<RectTransform>();
        emblemRect.anchorMin = Vector2.zero; emblemRect.anchorMax = Vector2.one;
        emblemRect.offsetMin = emblemRect.offsetMax = Vector2.zero;
        emblems[index] = emblem.GetComponent<SkillSlotIconGraphic>();

        var overlay = new GameObject("Skill Cooldown", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(slot, false);
        var rect = overlay.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        // Sweep only the recessed well so the bezel and rim stay readable during cooldown.
        rect.offsetMin = Vector2.one * SkillSlotIconGraphic.WellInset;
        rect.offsetMax = Vector2.one * -SkillSlotIconGraphic.WellInset;
        cooldowns[index] = overlay.GetComponent<Image>();
        cooldowns[index].sprite = icon != null ? icon.sprite : null;
        cooldowns[index].color = new Color(.025f, .018f, .015f, .74f);
        cooldowns[index].type = Image.Type.Filled;
        cooldowns[index].fillMethod = Image.FillMethod.Radial360;
        cooldowns[index].fillOrigin = (int)Image.Origin360.Top;
        cooldowns[index].fillClockwise = false;
        cooldowns[index].raycastTarget = false;
        titles[index] = Label(slot, "Skill Name", 1f, 12f, 21);
        titles[index].color = TitleColor;
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

    /// <summary>Large centred countdown during cooldown; small caption near the well's bottom edge otherwise.</summary>
    private void SetTimer(int index, string text, bool countdown, Color color)
    {
        var label = timers[index];
        label.text = text;
        label.color = color;
        label.fontSize = countdown ? 25 : 15;
        label.rectTransform.anchoredPosition = new Vector2(0f, countdown ? 0f : -42f);
    }

    private void UpdateEmblem(int index, SkillSlotIconGraphic.Glyph glyph, SkillSlotIconGraphic.State state, float remaining)
    {
        if (emblems[index] == null) return;
        float now = Time.unscaledTime;
        if (previousRemaining[index] > 0f && remaining <= 0f) readyFlashAt[index] = now;
        previousRemaining[index] = remaining;
        float flash = Mathf.Clamp01(1f - (now - readyFlashAt[index]) / ReadyFlashSeconds);
        float pulse = (Mathf.Sin(now * 2.4f) + 1f) * .5f;
        emblems[index].SetVisual(glyph, state, pulse, flash);
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
            skills == null ? 15 : shockwave ? skills.ShockwaveManaCost : skills.StealthManaCost,
            shockwave ? SkillSlotIconGraphic.Glyph.Shockwave : SkillSlotIconGraphic.Glyph.Stealth);
        if (actions != null && actions.SelectedThrowSkill == PlayerAttackController.ThrowSkill.PoisonDaggers) RefreshDaggerSlot();
        else RefreshGrenadeSlot();
    }

    private void RefreshDaggerSlot()
    {
        if (titles[1] != null)
        {
            float remaining = daggers != null ? daggers.CooldownRemaining : 0;
            bool usable = skills != null && skills.CanUseSkills;
            bool insufficient = daggers != null && mana != null && mana.CurrentMana < daggers.ManaCost;
            titles[1].text = "E 단검 투척";
            var state = !usable ? SkillSlotIconGraphic.State.Idle
                : remaining > 0 ? SkillSlotIconGraphic.State.Cooldown
                : insufficient ? SkillSlotIconGraphic.State.Blocked : SkillSlotIconGraphic.State.Ready;
            if (remaining > 0) SetTimer(1, $"{remaining:0.0}s", true, Color.white);
            else SetTimer(1, usable && insufficient ? "MP 부족" : "", false, BlockedColor);
            costs[1].text = "MP 10%";
            costs[1].color = insufficient ? new Color(1f, .45f, .4f) : new Color(.55f, .9f, .6f);
            cooldowns[1].fillAmount = daggers == null ? 0 : remaining / Mathf.Max(.01f, daggers.Cooldown);
            UpdateEmblem(1, SkillSlotIconGraphic.Glyph.Dagger, state, remaining);
        }
        if (grenadeNotice != null) grenadeNotice.gameObject.SetActive(false);
    }

    private void RefreshGrenadeSlot()
    {
        if (titles[1] != null)
        {
            titles[1].text = "E 수류탄";
            float remaining = grenades != null ? grenades.CooldownRemaining : 0;
            var state = grenades == null ? SkillSlotIconGraphic.State.Idle
                : grenades.IsAiming ? SkillSlotIconGraphic.State.Active
                : grenades.Remaining == 0 ? SkillSlotIconGraphic.State.Blocked
                : remaining > 0 ? SkillSlotIconGraphic.State.Cooldown : SkillSlotIconGraphic.State.Ready;
            if (grenades == null) SetTimer(1, "", false, HintColor);
            else if (grenades.IsAiming) SetTimer(1, $"{grenades.AimAngle:0}° · 놓아 투척", false, new Color(1, .85f, .55f));
            else if (grenades.Remaining == 0) SetTimer(1, "소진", false, BlockedColor);
            else if (remaining > 0) SetTimer(1, $"{remaining:0.0}s", true, Color.white);
            else SetTimer(1, "누르고 조준", false, HintColor);
            costs[1].text = grenades != null ? $"수류탄 {grenades.Remaining}/{grenades.Capacity}" : "수류탄 —/5";
            costs[1].color = grenades != null && grenades.Remaining == 0 ? new Color(1, .45f, .4f) : new Color(1, .8f, .4f);
            cooldowns[1].fillAmount = grenades == null ? 0 : remaining / Mathf.Max(.01f, grenades.Cooldown);
            UpdateEmblem(1, SkillSlotIconGraphic.Glyph.Grenade, state, remaining);
        }
        bool showNotice = grenades != null && grenades.NoticeRemaining > 0;
        grenadeNotice.gameObject.SetActive(showNotice);
        if (showNotice)
        {
            grenadeNotice.text = grenades.Notice;
            grenadeNotice.color = new Color(1, .85f, .55f, Mathf.Clamp01(grenades.NoticeRemaining / .25f));
        }
    }

    private void RefreshSlot(int index, string title, float remaining, float cooldown, int cost, SkillSlotIconGraphic.Glyph glyph)
    {
        if (titles[index] == null) return;
        titles[index].text = title;
        bool active = index == 0 && skills != null && skills.IsStealthed
            && (actions == null || actions.SelectedPrimarySkill == PlayerAttackController.PrimarySkill.Stealth);
        bool insufficient = mana != null && mana.CurrentMana < cost;
        bool usable = skills != null && skills.CanUseSkills;
        var state = active ? SkillSlotIconGraphic.State.Active
            : remaining > 0f ? SkillSlotIconGraphic.State.Cooldown
            : !usable ? SkillSlotIconGraphic.State.Idle
            : insufficient ? SkillSlotIconGraphic.State.Blocked : SkillSlotIconGraphic.State.Ready;
        if (active) SetTimer(index, "은신 중", false, new Color(.85f, .74f, 1f));
        else if (remaining > 0f) SetTimer(index, $"{remaining:0.0}s", true, Color.white);
        else SetTimer(index, usable && insufficient ? "MP 부족" : "", false, BlockedColor);
        costs[index].text = $"MP {cost}";
        if (index == 0 && skills != null && (actions == null || actions.SelectedPrimarySkill == PlayerAttackController.PrimarySkill.Stealth))
            costs[index].text = $"MP {skills.StealthDrainPercent:0.#}% / 0.5초";
        costs[index].color = insufficient ? new Color(1f, .45f, .4f) : new Color(.65f, .85f, 1f);
        cooldowns[index].fillAmount = remaining / Mathf.Max(.01f, cooldown);
        UpdateEmblem(index, glyph, state, remaining);
    }
}
