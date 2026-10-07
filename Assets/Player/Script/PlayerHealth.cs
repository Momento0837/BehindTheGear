using System;
using UnityEngine;

[RequireComponent(typeof(PlayerControlLock))]
public sealed class PlayerHealth : MonoBehaviour
{
    [SerializeField, Min(1)] private int maxHealth = 10;
    [SerializeField, Min(0)] private int defense = 11;
    [SerializeField, Min(0f)] private float invincibilityDuration = .2f;

    public int MaxHealth => maxHealth;
    public int Defense => defense;
    public int CurrentHealth { get; private set; }
    public bool IsDead => CurrentHealth <= 0;
    public event Action<int, int> HealthChanged;
    public event Action Died;

    private PlayerControlLock playerControlLock;
    private float invincibilityEndTime;

    private void Awake()
    {
        playerControlLock = GetComponent<PlayerControlLock>();
        CurrentHealth = maxHealth;
    }

    public void TakeDamage(int attackPower)
    {
        if (attackPower <= 0 || IsDead || Time.time < invincibilityEndTime) return;
        int damage = Mathf.Max(1, attackPower - defense);

        CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
        invincibilityEndTime = Time.time + invincibilityDuration;
        HealthChanged?.Invoke(CurrentHealth, maxHealth);

        if (!IsDead) return;
        playerControlLock.Acquire();
        Died?.Invoke();
    }
}
