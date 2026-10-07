using System;
using System.Collections.Generic;
using UnityEngine;

public interface IItemStatModifierProvider
{
    bool HasStatModifiers { get; }
    IReadOnlyList<ItemStatModifier> StatModifiers { get; }
    int GetStatIncrease(PersonalStatType statType);
}

[Serializable]
public struct ItemStatModifier
{
    [SerializeField] private PersonalStatType statType;
    [SerializeField, Min(0)] private int increase;

    public PersonalStatType StatType => statType;
    public int Increase => Mathf.Max(0, increase);

    public ItemStatModifier(PersonalStatType statType, int increase)
    {
        this.statType = statType;
        this.increase = Mathf.Max(0, increase);
    }
}
