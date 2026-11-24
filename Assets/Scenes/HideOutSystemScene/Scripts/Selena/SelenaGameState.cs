using System;
using UnityEngine;

namespace BehindTheGear.HideOut
{
    // Collects only what Selena can observe in the base and turns it into the pool's bands
    // (docs/GAME_STATE_DESIGN_20261121.md section 6). Session flags live in memory only and are never saved.
    public static class SelenaGameState
    {
        // Maps the field HUD can show (LocationHUD.SetLocation). Anything else is treated as unknown ("").
        private static readonly string[] KnownMaps = { "포레스트", "가베지", "마켓", "거주구역" };

        // --- Session memory (not part of HideOutSave) ---
        public static bool ReturnPending { get; private set; }
        public static string LastMap { get; private set; } = string.Empty;
        private static bool gearBaseline;
        private static string seenWeaponId, seenCloakId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            ReturnPending = false; LastMap = string.Empty;
            gearBaseline = false; seenWeaponId = seenCloakId = null;
        }

        // Called by HideOutTravel when the player walks from a field map into the base.
        public static void MarkReturnedFromField(HideOutPlayerBridge bridge, string locationName)
        {
            if (!gearBaseline) RememberGear(bridge); // Gear worn when arriving is not "new".
            ReturnPending = true;
            LastMap = Array.IndexOf(KnownMaps, locationName) >= 0 ? locationName : string.Empty;
        }

        public static void ConsumeReturn() => ReturnPending = false;

        // --- Bands ---

        // Same thresholds as the red screen overlay (PrototypeScreenEffects): 75 / 50 / 30 / 15 %.
        public static string HealthBand(int current, int max)
        {
            if (max <= 0) return "unknown";
            float r = Mathf.Clamp01(current / (float)max);
            return r >= .75f ? "healthy" : r >= .5f ? "slightly_hurt" : r >= .3f ? "hurt" : r >= .15f ? "badly_hurt" : "critical";
        }

        // MP is only shown as tiredness: 50 % and up fresh, 20-50 % tired, below 20 % exhausted.
        public static string StaminaBand(int current, int max)
        {
            if (max <= 0) return "unknown";
            float r = Mathf.Clamp01(current / (float)max);
            return r >= .5f ? "fresh" : r >= .2f ? "tired" : "exhausted";
        }

        public static bool SelenaPresent(HideOutPlayerBridge bridge)
            => bridge != null && bridge.Initialized && bridge.State.selenaRecruited;

        public static bool ClinicReady(HideOutPlayerBridge bridge)
            => bridge.State.baseTutorialComplete && bridge.Now >= bridge.State.clinicReadyAt;

        public static bool PotionReady(HideOutPlayerBridge bridge)
            => bridge.State.selenaRecruited && bridge.Now >= bridge.State.potionsReadyAt;

        public static SelenaLineState Collect(HideOutPlayerBridge bridge, string eventId)
        {
            var state = new SelenaLineState { eventId = eventId, health = "unknown", stamina = "unknown", lastMap = LastMap, gear = string.Empty };
            if (bridge == null || !bridge.Initialized) return state;
            if (bridge.Health != null) state.health = HealthBand(bridge.Health.CurrentHealth, bridge.Health.MaxHealth);
            var mana = bridge.GetComponent<PlayerMana>();
            state.stamina = mana != null ? StaminaBand(mana.CurrentMana, mana.MaxMana) : StaminaBand(bridge.State.mana, bridge.MaxMana);
            state.clinicReady = ClinicReady(bridge);
            state.potionReady = PotionReady(bridge);
            return state;
        }

        // --- Equipment Selena has not commented on yet ---

        private static InventoryItemDefinition EquippedWeapon(HideOutPlayerBridge bridge)
            => bridge.Inventory != null ? bridge.Inventory.GetEquipped(InventoryItemDefinition.EquipmentSlot.Weapon) : null;

        // The cloak (Tattered_Cloak, "망토") is an accessory; armor is also checked in case a cloak is defined there.
        private static InventoryItemDefinition EquippedCloak(HideOutPlayerBridge bridge)
        {
            if (bridge.Inventory == null) return null;
            foreach (var slot in new[] { InventoryItemDefinition.EquipmentSlot.Accessory, InventoryItemDefinition.EquipmentSlot.Armor })
            {
                var item = bridge.Inventory.GetEquipped(slot);
                if (item != null && (item.ItemId == "Tattered_Cloak" || (item.DisplayName ?? string.Empty).Contains("망토"))) return item;
            }
            return null;
        }

        // "weapon" / "cloak" when something was newly equipped since Selena last commented on gear, otherwise "".
        // The first look of a play session only records a baseline (gear already worn is not "new").
        public static string NewGear(HideOutPlayerBridge bridge)
        {
            if (bridge == null || !bridge.Initialized) return string.Empty;
            if (!gearBaseline) { RememberGear(bridge); return string.Empty; }
            string weapon = EquippedWeapon(bridge)?.ItemId, cloak = EquippedCloak(bridge)?.ItemId;
            if (!string.IsNullOrEmpty(weapon) && weapon != seenWeaponId) return "weapon";
            if (!string.IsNullOrEmpty(cloak) && cloak != seenCloakId) return "cloak";
            return string.Empty;
        }

        public static void RememberGear(HideOutPlayerBridge bridge)
        {
            if (bridge == null || !bridge.Initialized) return;
            seenWeaponId = EquippedWeapon(bridge)?.ItemId;
            seenCloakId = EquippedCloak(bridge)?.ItemId;
            gearBaseline = true;
        }

        // --- Priority for the clinic visit (booth clicked / treatment screen opened) ---
        // 1. returned        first clinic visit after coming back from a field map (its slots already rank
        //                    critical/badly hurt > tired > facility > map, so severity is still respected)
        // 2. clinic_*        badly_hurt / critical: treatment comes first
        // 3. tired           MP below 50 %
        // 4. new_equipment   newly equipped weapon/cloak not commented on yet
        // 5. clinic_ready / clinic_cooldown
        // after_heal is not chosen here: it answers a successful heal directly.
        public static SelenaLineState ChooseClinicVisit(HideOutPlayerBridge bridge)
        {
            if (!gearBaseline) RememberGear(bridge);
            var probe = Collect(bridge, SelenaEvent.ClinicReady);
            string clinicEvent = probe.clinicReady ? SelenaEvent.ClinicReady : SelenaEvent.ClinicCooldown;
            if (ReturnPending) return Collect(bridge, SelenaEvent.Returned);
            if (probe.health == "badly_hurt" || probe.health == "critical") return Collect(bridge, clinicEvent);
            if (probe.stamina == "tired" || probe.stamina == "exhausted") return Collect(bridge, SelenaEvent.Tired);
            string gear = NewGear(bridge);
            if (gear.Length > 0)
            {
                var state = Collect(bridge, SelenaEvent.NewEquipment);
                state.gear = gear;
                return state;
            }
            return Collect(bridge, clinicEvent);
        }

        // Bookkeeping after a line was actually shown.
        public static void OnSpoken(HideOutPlayerBridge bridge, string eventId)
        {
            if (eventId == SelenaEvent.Returned) ConsumeReturn();
            if (eventId == SelenaEvent.NewEquipment) RememberGear(bridge);
        }
    }
}
