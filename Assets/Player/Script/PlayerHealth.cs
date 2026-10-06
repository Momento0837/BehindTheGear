using System;
using UnityEngine;

[RequireComponent(typeof(PlayerControlLock))]
public sealed class PlayerHealth : MonoBehaviour
{
    [SerializeField, Min(1)] private int maxHealth = 10;

    public int MaxHealth => maxHealth;
    public int CurrentHealth { get; private set; }
    public bool IsDead => CurrentHealth <= 0;
    public event Action<int, int> HealthChanged;
    public event Action Died;

    private PlayerControlLock playerControlLock;

    private void Awake()
    {
        playerControlLock = GetComponent<PlayerControlLock>();
        CurrentHealth = maxHealth;
    }

    public void TakeDamage(int damage)
    {
        if (damage <= 0 || IsDead) return;
        CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
        HealthChanged?.Invoke(CurrentHealth, maxHealth);

        if (!IsDead) return;
        playerControlLock.Acquire();
        Died?.Invoke();
    }
}
