using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(PlayerInputReader))]
public sealed class PlayerAttackController : MonoBehaviour
{
    [SerializeField] private UnityEvent onAttackZ;
    [SerializeField] private UnityEvent onAttackX;
    [SerializeField] private UnityEvent onAttackC;
    [SerializeField] private UnityEvent onAttackV;
    [SerializeField] private UnityEvent onPrimaryClick;
    [SerializeField] private UnityEvent onSecondaryClick;

    private void Awake()
    {
        PlayerInputReader input = GetComponent<PlayerInputReader>();
        input.AttackZPressed += AttackZ;
        input.AttackXPressed += AttackX;
        input.AttackCPressed += AttackC;
        input.AttackVPressed += AttackV;
        input.PrimaryClickPressed += PrimaryClick;
        input.SecondaryClickPressed += SecondaryClick;
    }

    public void AttackZ() => onAttackZ?.Invoke();
    public void AttackX() => onAttackX?.Invoke();
    public void AttackC() => onAttackC?.Invoke();
    public void AttackV() => onAttackV?.Invoke();
    public void PrimaryClick() => onPrimaryClick?.Invoke();
    public void SecondaryClick() => onSecondaryClick?.Invoke();
}
