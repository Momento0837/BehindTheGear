using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(PlayerControlLock))]
public sealed class PlayerHealth : MonoBehaviour
{
    [SerializeField, Min(1)] private int maxHealth = 10;
    [SerializeField, Min(0)] private int defense = 11;
    [SerializeField, Min(0f)] private float invincibilityDuration = .2f;

    private readonly Dictionary<string, int> maxHealthBonuses = new();
    public int MaxHealth { get { int total = maxHealth; foreach (int bonus in maxHealthBonuses.Values) total += bonus; return Mathf.Max(1, total); } }
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
        CurrentHealth = MaxHealth;
    }

    public void TakeDamage(int attackPower)
    {
        if (attackPower <= 0 || IsDead || Time.time < invincibilityEndTime) return;
        int damage = Mathf.Max(1, attackPower - defense);

        CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
        invincibilityEndTime = Time.time + invincibilityDuration;
        HealthChanged?.Invoke(CurrentHealth, MaxHealth);

        if (!IsDead) return;
        playerControlLock.Acquire();
        Died?.Invoke();
    }

    public void SetMaxHealthBonus(string source, int amount)
    {
        if (maxHealthBonuses.TryGetValue(source, out int previous) && previous == amount) return;
        maxHealthBonuses[source] = Mathf.Max(0, amount);
        CurrentHealth = Mathf.Min(CurrentHealth, MaxHealth);
        HealthChanged?.Invoke(CurrentHealth, MaxHealth);
    }

    public int RestoreHealth(int amount)
    {
        if (IsDead || amount <= 0) return 0;
        int restored = Mathf.Min(amount, MaxHealth - CurrentHealth);
        if (restored <= 0) return 0;
        CurrentHealth += restored;
        HealthChanged?.Invoke(CurrentHealth, MaxHealth);
        return restored;
    }

    // Save adapters restore current HP separately from the base maximum and growth sources.
    public void RestoreSavedHealth(int amount)
    {
        CurrentHealth = Mathf.Clamp(amount, 0, MaxHealth);
        HealthChanged?.Invoke(CurrentHealth, MaxHealth);
    }
}
