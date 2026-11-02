using System;
using UnityEngine;

namespace BehindTheGear.HideOut
{
    public sealed class HideOutDummy : MonoBehaviour, IMeleeDamageReceiver
    {
        public int LastDamage { get; private set; }
        public int Hits { get; private set; }
        public event Action Hit;
        public void ReceiveMeleeDamage(int damage)
        {
            LastDamage = Mathf.Max(0, damage); Hits++; Hit?.Invoke();
        }
        public void Test(HideOutPlayerBridge player)
        {
            if (player.Inventory.GetEquipped(InventoryItemDefinition.EquipmentSlot.Weapon) == null)
            { player.SetMessage("장착된 무기가 없습니다."); return; }
            if (player.Melee == null) { player.SetMessage("기존 플레이어에 PlayerMeleeAttack이 연결되어 있지 않습니다."); return; }
            if (!player.Melee.TryAttackTarget(this)) { player.SetMessage("공격 쿨타임 중입니다."); return; }
            player.SetMessage($"허수아비 명중 · {LastDamage} 데미지");
        }
    }
}
