using System;
using UnityEngine;

public enum PersonalStatType
{
    Evasion,
    Stealth,
    Attack,
    Defense,
    Persuasion
}

[DisallowMultipleComponent]
public sealed class PlayerStats : MonoBehaviour
{
    [SerializeField, Min(0)] private int availablePoints = 5;
    [SerializeField, Min(0)] private int evasion;
    [SerializeField, Min(0)] private int stealth;
    [SerializeField, Min(0)] private int attack;
    [SerializeField, Min(0)] private int defense;
    [SerializeField, Min(0)] private int persuasion;
    [SerializeField, Min(0)] private int statSpreadLimit = 5;

    public event Action Changed;

    public int AvailablePoints => availablePoints;
    public int StatSpreadLimit => statSpreadLimit;
    public int Evasion => GetValue(PersonalStatType.Evasion);
    public int Stealth => GetValue(PersonalStatType.Stealth);
    public int Attack => GetValue(PersonalStatType.Attack);
    public int Defense => GetValue(PersonalStatType.Defense);
    public int Persuasion => GetValue(PersonalStatType.Persuasion);
    public float MonsterAttackIgnoreChance => Mathf.Clamp01(Evasion * .005f);
    public float DetectionGainMultiplier => Mathf.Max(0f, 1f - Stealth * .015f);
    public int AttackPowerBonus => Attack;
    public float DamageReduction => Defense < 2 ? 0f : Mathf.Clamp01(Defense * .005f);
    public int ShopPriceDiscount => Persuasion;

    public int GetValue(PersonalStatType type)
    {
        int total = GetBaseValue(type);
        var inventory = GetComponent<PlayerInventory>();
        if (inventory != null)
            for (int i = 1; i <= 6; i++)
                total += inventory.GetEquipped((InventoryItemDefinition.EquipmentSlot)i)?.GetStatIncrease(type) ?? 0;
        return Mathf.Max(0, total);
    }

    public int GetBaseValue(PersonalStatType type)
    {
        return type switch
        {
            PersonalStatType.Evasion => evasion,
            PersonalStatType.Stealth => stealth,
            PersonalStatType.Attack => attack,
            PersonalStatType.Defense => defense,
            PersonalStatType.Persuasion => persuasion,
            _ => 0
        };
    }

    public bool TryUpgrade(PersonalStatType type, out string message)
    {
        if (availablePoints <= 0)
        {
            message = "포인트가 부족합니다.";
            return false;
        }

        int current = GetBaseValue(type);
        if (current + 1 > GetLowestStatValue() + statSpreadLimit)
        {
            message = "스탯 편차 제한으로 강화할 수 없습니다.";
            return false;
        }

        SetValue(type, current + 1);
        availablePoints--;

        string displayName = GetDisplayName(type);
        message = $"{displayName}{GetSubjectParticle(displayName)} 1 증가했습니다.";
        Changed?.Invoke();
        return true;
    }

    public bool TryDowngrade(PersonalStatType type, out string message)
    {
        int current = GetBaseValue(type);
        if (current <= 0) { message = "줄일 수 있는 스탯이 없습니다."; return false; }
        SetValue(type, current - 1);
        availablePoints++;
        message = $"{GetDisplayName(type)} 1 감소, 포인트 1 반환";
        Changed?.Invoke();
        return true;
    }

    public void AddPoints(int amount)
    {
        if (amount <= 0) return;
        availablePoints += amount;
        Changed?.Invoke();
    }

    public string GetEffectSummary(PersonalStatType type)
    {
        return type switch
        {
            PersonalStatType.Evasion => $"몬스터 공격 무시 +{GetValue(type) * .5f:0.#}%",
            PersonalStatType.Stealth => $"발각 증가 속도 -{GetValue(type) * 1.5f:0.#}%",
            PersonalStatType.Attack => $"공격력 +{AttackPowerBonus}, 인벤토리 +{Attack * 2}칸",
            PersonalStatType.Defense => Defense < 2 ? "2부터 피해 경감 적용" : $"받는 피해 -{Defense * .5f:0.#}%",
            PersonalStatType.Persuasion => $"상점 가격 -{ShopPriceDiscount}원",
            _ => string.Empty
        };
    }

    public static string GetDisplayName(PersonalStatType type)
    {
        return type switch
        {
            PersonalStatType.Evasion => "회피",
            PersonalStatType.Stealth => "은신",
            PersonalStatType.Attack => "공격",
            PersonalStatType.Defense => "방어",
            PersonalStatType.Persuasion => "설득",
            _ => type.ToString()
        };
    }

    private static string GetSubjectParticle(string text)
    {
        if (string.IsNullOrEmpty(text)) return "이";

        char last = text[^1];
        if (last < 0xAC00 || last > 0xD7A3) return "이";

        return (last - 0xAC00) % 28 == 0 ? "가" : "이";
    }

    private int GetLowestStatValue()
    {
        return Mathf.Min(evasion, stealth, attack, defense, persuasion);
    }

    private void SetValue(PersonalStatType type, int value)
    {
        switch (type)
        {
            case PersonalStatType.Evasion:
                evasion = value;
                break;
            case PersonalStatType.Stealth:
                stealth = value;
                break;
            case PersonalStatType.Attack:
                attack = value;
                break;
            case PersonalStatType.Defense:
                defense = value;
                break;
            case PersonalStatType.Persuasion:
                persuasion = value;
                break;
        }
    }
}
