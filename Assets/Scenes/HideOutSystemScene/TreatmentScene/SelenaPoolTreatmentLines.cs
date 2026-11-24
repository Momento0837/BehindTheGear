using System;

namespace BehindTheGear.HideOut
{
    // Treatment screen line source backed by the reviewed Selena line pool (no runtime model).
    //   Enter          -> clinic visit priority (returned > badly hurt/critical > tired > new gear > clinic ready/cooldown)
    //   HealDone       -> after_heal
    //   HealOnCooldown -> clinic_cooldown
    //   CooldownReady  -> clinic_ready
    // Events the pool has no situation for (heal not needed/unavailable, upgrade results) keep the existing
    // rule-based TreatmentRuleLines unchanged.
    public sealed class SelenaPoolTreatmentLines : ITreatmentDialogueSource
    {
        private readonly HideOutPlayerBridge bridge;
        private readonly ITreatmentDialogueSource fallback;

        public SelenaPoolTreatmentLines(HideOutPlayerBridge bridge, ITreatmentDialogueSource fallback)
        {
            this.bridge = bridge; this.fallback = fallback;
        }

        public void RequestLine(TreatmentTalkContext context, Action<string> deliver)
        {
            SelenaLineState? state = context.talkEvent switch
            {
                TreatmentTalkEvent.Enter => SelenaGameState.ChooseClinicVisit(bridge),
                TreatmentTalkEvent.HealDone => SelenaGameState.Collect(bridge, SelenaEvent.AfterHeal),
                TreatmentTalkEvent.HealOnCooldown => SelenaGameState.Collect(bridge, SelenaEvent.ClinicCooldown),
                TreatmentTalkEvent.CooldownReady => SelenaGameState.Collect(bridge, SelenaEvent.ClinicReady),
                _ => null,
            };
            string line = null, slotId = null;
            if (state.HasValue) line = SelenaLinePool.Shared.Pick(state.Value, out slotId);
            if (string.IsNullOrEmpty(line)) { fallback?.RequestLine(context, deliver); return; }
            SelenaGameState.OnSpoken(bridge, state.Value.eventId);
            UnityEngine.Debug.Log($"[Selena] {state.Value.eventId} -> {slotId}: {line}");
            deliver?.Invoke(line);
        }
    }
}
