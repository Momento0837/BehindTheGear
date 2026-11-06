using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(PlayerInputReader))]
public sealed class PlayerAttackController : MonoBehaviour
{
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
        // Wire authored scene players and the Continue prefab through the same implementation.
        skills = GetComponent<PlayerSkills>() ?? gameObject.AddComponent<PlayerSkills>();
        grenades = GetComponent<PlayerGrenadeSkill>() ?? gameObject.AddComponent<PlayerGrenadeSkill>();
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
        if (!isActiveAndEnabled || !skills.TryUseStealth()) return;
        InvokeSharedOrLegacy(onSkillOne,
            PlayerControlSchemeSettings.Current == PlayerControlScheme.WasdMouseQe ? onSkillQ : onAttackC);
    }

    public void SkillTwo()
    {
        if (isActiveAndEnabled) grenades.BeginAim();
    }

    public void ReleaseSkillTwo()
    {
        if (!isActiveAndEnabled || !grenades.TryThrow()) return;
        InvokeSharedOrLegacy(onSkillTwo,
            PlayerControlSchemeSettings.Current == PlayerControlScheme.WasdMouseQe ? onSkillE : onAttackV);
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
