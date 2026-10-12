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

    private void Awake()
    {
        PlayerInputReader input = GetComponent<PlayerInputReader>();
        input.BasicAttackPressed += BasicAttack;
        input.SecondaryAttackPressed += SecondaryAttack;
        input.SkillOnePressed += SkillOne;
        input.SkillTwoPressed += SkillTwo;
    }

    public void BasicAttack() => InvokeSharedOrLegacy(onBasicAttack,
        PlayerControlSchemeSettings.Current == PlayerControlScheme.WasdMouseQe ? onPrimaryClick : onAttackZ);

    public void SecondaryAttack() => InvokeSharedOrLegacy(onSecondaryAttack,
        PlayerControlSchemeSettings.Current == PlayerControlScheme.WasdMouseQe ? onSecondaryClick : onAttackX);

    public void SkillOne() => InvokeSharedOrLegacy(onSkillOne,
        PlayerControlSchemeSettings.Current == PlayerControlScheme.WasdMouseQe ? onSkillQ : onAttackC);

    public void SkillTwo() => InvokeSharedOrLegacy(onSkillTwo,
        PlayerControlSchemeSettings.Current == PlayerControlScheme.WasdMouseQe ? onSkillE : onAttackV);

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
