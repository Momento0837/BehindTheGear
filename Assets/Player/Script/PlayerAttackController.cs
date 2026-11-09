using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(PlayerInputReader))]
public sealed class PlayerAttackController : MonoBehaviour
{
    public enum ThrowSkill { PoisonDaggers, Grenade }
    public enum PrimarySkill { Stealth, Shockwave }
    [Header("Equipped skills - development selection does not change saved grenade ammunition")]
    [SerializeField] private ThrowSkill selectedThrowSkill = ThrowSkill.PoisonDaggers;
    [SerializeField] private PrimarySkill selectedPrimarySkill = PrimarySkill.Stealth;
    public ThrowSkill SelectedThrowSkill => selectedThrowSkill;
    public PrimarySkill SelectedPrimarySkill => selectedPrimarySkill;
    public PlayerPoisonDaggerSkill Daggers { get; private set; }
    [Header("Shared actions")]
    [SerializeField] private UnityEvent onBasicAttack = new();
    [SerializeField] private UnityEvent onSecondaryAttack = new();
    [SerializeField] private UnityEvent onSkillOne = new();
    [SerializeField] private UnityEvent onSkillTwo = new();

    [Header("Legacy action events")]
    [SerializeField] private UnityEvent onAttackZ;
    [SerializeField] private UnityEvent onAttackX;
    [SerializeField] private UnityEvent onAttackC;
    [SerializeField] private UnityEvent onAttackV;
    [SerializeField] private UnityEvent onPrimaryClick;
    [SerializeField] private UnityEvent onSecondaryClick;
    [SerializeField] private UnityEvent onSkillQ;
    [SerializeField] private UnityEvent onSkillE;
    private PlayerSkills skills;
    private PlayerGrenadeSkill grenades;

    private void Awake()
    {
        EnsureSkillComponents();
    }

    private void EnsureSkillComponents()
    {
        // Wire authored scene players and the Continue prefab through the same implementation.
        skills = GetComponent<PlayerSkills>() ?? gameObject.AddComponent<PlayerSkills>();
        grenades = GetComponent<PlayerGrenadeSkill>() ?? gameObject.AddComponent<PlayerGrenadeSkill>();
        Daggers = GetComponent<PlayerPoisonDaggerSkill>() ?? gameObject.AddComponent<PlayerPoisonDaggerSkill>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (GetComponent<PlayerSkillDevelopmentMenu>() == null) gameObject.AddComponent<PlayerSkillDevelopmentMenu>();
#endif
    }

    private void OnEnable()
    {
        EnsureSkillComponents();
        PlayerInputReader input = GetComponent<PlayerInputReader>();
        input.BasicAttackPressed += BasicAttack;
        input.SecondaryAttackPressed += SecondaryAttack;
        input.SkillOnePressed += SkillOne;
        input.SkillTwoPressed += SkillTwo;
        input.SkillTwoReleased += ReleaseSkillTwo;
    }

    public void BasicAttack() => InvokeSharedOrLegacy(onBasicAttack,
        PlayerControlSchemeSettings.Current == PlayerControlScheme.WasdMouseQe ? onPrimaryClick : onAttackZ);

    public void SecondaryAttack() => InvokeSharedOrLegacy(onSecondaryAttack,
        PlayerControlSchemeSettings.Current == PlayerControlScheme.WasdMouseQe ? onSecondaryClick : onAttackX);

    public void SkillOne()
    {
        if (!isActiveAndEnabled || PlayerSkillDevelopmentMenu.IsOpen) return;
        bool used = selectedPrimarySkill == PrimarySkill.Stealth ? skills.TryUseStealth() : skills.TryUseShockwave();
        if (!used) return;
        InvokeSharedOrLegacy(onSkillOne,
            PlayerControlSchemeSettings.Current == PlayerControlScheme.WasdMouseQe ? onSkillQ : onAttackC);
    }

    public void SkillTwo()
    {
        if (!isActiveAndEnabled || PlayerSkillDevelopmentMenu.IsOpen) return;
        if (selectedThrowSkill == ThrowSkill.Grenade) grenades.BeginAim();
        else if (Daggers.TryThrow()) NotifyThrow();
    }

    public void ReleaseSkillTwo()
    {
        if (!isActiveAndEnabled || selectedThrowSkill != ThrowSkill.Grenade || PlayerSkillDevelopmentMenu.IsOpen || !grenades.TryThrow()) return;
        NotifyThrow();
    }

    private void NotifyThrow()
    {
        InvokeSharedOrLegacy(onSkillTwo,
            PlayerControlSchemeSettings.Current == PlayerControlScheme.WasdMouseQe ? onSkillE : onAttackV);
    }

    public void SelectThrowSkill(ThrowSkill choice) { grenades.CancelAim(); selectedThrowSkill = choice; }
    public void SelectPrimarySkill(PrimarySkill choice) => selectedPrimarySkill = choice;

    private void OnDisable()
    {
        grenades?.CancelAim();
        var input = GetComponent<PlayerInputReader>();
        if (input == null) return;
        input.BasicAttackPressed -= BasicAttack; input.SecondaryAttackPressed -= SecondaryAttack;
        input.SkillOnePressed -= SkillOne; input.SkillTwoPressed -= SkillTwo; input.SkillTwoReleased -= ReleaseSkillTwo;
    }

    public void AttackZ() => onAttackZ?.Invoke();
    public void AttackX() => onAttackX?.Invoke();
    public void PrimaryClick() => onPrimaryClick?.Invoke();
    public void SecondaryClick() => onSecondaryClick?.Invoke();

    private static void InvokeSharedOrLegacy(UnityEvent shared, UnityEvent legacy)
    {
        if (shared != null && shared.GetPersistentEventCount() > 0) shared.Invoke();
        else legacy?.Invoke();
    }
}
