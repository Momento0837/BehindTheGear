using System;
using BehindTheGear.HideOut;
using UnityEngine;

/// <summary>Player MP outside the hideout; uses the existing hideout MP when available.</summary>
[DisallowMultipleComponent]
public sealed class PlayerMana : MonoBehaviour
{
    [SerializeField, Min(1)] private int maxMana = 100;
    [SerializeField, Min(0)] private int currentMana = 100;
    private HideOutPlayerBridge bridge;
    private int lastCurrent = -1, lastMaximum = -1;
    public event Action<int, int> ManaChanged;

    private bool HasBridge => bridge != null && bridge.Initialized;
    public int MaxMana => HasBridge ? bridge.MaxMana : Mathf.Max(1, maxMana);
    public int CurrentMana => Mathf.Clamp(HasBridge ? bridge.State.mana : currentMana, 0, MaxMana);

    private void Awake() => bridge = GetComponent<HideOutPlayerBridge>();

    private void Update()
    {
        if (bridge == null) bridge = GetComponent<HideOutPlayerBridge>();
        PublishChanges();
    }

    public bool TrySpend(int amount)
    {
        if (amount <= 0 || CurrentMana < amount) return false;
        SetCurrent(CurrentMana - amount);
        return true;
    }

    public int RestoreMana(int amount)
    {
        int restored = Mathf.Min(Mathf.Max(0, amount), MaxMana - CurrentMana);
        if (restored > 0) SetCurrent(CurrentMana + restored);
        return restored;
    }

    private void SetCurrent(int value)
    {
        if (HasBridge) bridge.State.mana = Mathf.Clamp(value, 0, MaxMana);
        else currentMana = Mathf.Clamp(value, 0, MaxMana);
        PublishChanges();
    }

    private void PublishChanges()
    {
        int current = CurrentMana, maximum = MaxMana;
        if (current == lastCurrent && maximum == lastMaximum) return;
        lastCurrent = current;
        lastMaximum = maximum;
        ManaChanged?.Invoke(current, maximum);
    }
}
