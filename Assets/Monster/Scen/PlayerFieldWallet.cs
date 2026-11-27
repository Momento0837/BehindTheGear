using BehindTheGear.HideOut;
using UnityEngine;

/// <summary>
/// Coins and AP picked up in the field. They are added to the base state (HideOutPlayerBridge.State) through the
/// bridge's normal memory transaction as soon as an initialized, non-sandbox bridge is on the player; until then
/// (e.g. chapter 1 before the first base visit) they wait here. Nothing is written to disk: the bed save keeps them.
/// The wallet lives on the player, so starting a new game / returning to the menu (which disposes the player) clears it.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerFieldWallet : MonoBehaviour
{
    public int PendingCoins { get; private set; }
    public int PendingActionPoints { get; private set; }

    private HideOutPlayerBridge bridge;

    public void Add(RewardPickup.Kind kind, int amount)
    {
        if (amount <= 0) return;
        if (kind == RewardPickup.Kind.Coin) PendingCoins += amount;
        else PendingActionPoints += amount;
    }

    private void LateUpdate()
    {
        if (PendingCoins == 0 && PendingActionPoints == 0) return;
        if (bridge == null) bridge = GetComponent<HideOutPlayerBridge>();
        if (bridge == null || !bridge.Initialized || bridge.Sandbox || bridge.IsSaving) return;

        int coins = PendingCoins, actionPoints = PendingActionPoints;
        // Change refuses a second command in the same frame; anything not applied simply waits for the next frame.
        bool applied = bridge.Change(s =>
        {
            s.coins = checked(s.coins + coins);
            s.actionPoints = checked(s.actionPoints + actionPoints);
            return actionPoints > 0 ? $"필드 보상 · 코인 +{coins} · AP +{actionPoints}" : $"필드 보상 · 코인 +{coins}";
        });
        if (!applied) return;
        PendingCoins -= coins;
        PendingActionPoints -= actionPoints;
    }
}
