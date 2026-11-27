using System;

namespace BehindTheGear.HideOut
{
    // Treatment screen line source for the Selena situations.
    //   Enter          -> clinic visit priority (returned > badly hurt/critical > tired > new gear > clinic ready/cooldown)
    //   HealDone       -> after_heal
    //   HealOnCooldown -> clinic_cooldown
    //   CooldownReady  -> clinic_ready
    // The line is a real-time one from the local SLM server when it is running (SelenaRealtime: a prefetched line at
    // once, otherwise a short wait) and a reviewed Selena line pool line when the server is off or too slow.
    // Events with no situation (heal not needed/unavailable, upgrade results) keep the existing rule-based
    // TreatmentRuleLines unchanged.
    public sealed class SelenaPoolTreatmentLines : ITreatmentDialogueSource
    {
        private readonly HideOutPlayerBridge bridge;
        private readonly ITreatmentDialogueSource fallback;
        private int serial; // A newer event drops a real-time answer that is still on its way.

        public SelenaPoolTreatmentLines(HideOutPlayerBridge bridge, ITreatmentDialogueSource fallback)
        {
            this.bridge = bridge; this.fallback = fallback;
        }

        public void RequestLine(TreatmentTalkContext context, Action<string> deliver)
        {
            int ticket = ++serial;
            SelenaLineState? state = context.talkEvent switch
            {
                TreatmentTalkEvent.Enter => SelenaGameState.ChooseClinicVisit(bridge),
                TreatmentTalkEvent.HealDone => SelenaGameState.Collect(bridge, SelenaEvent.AfterHeal),
                TreatmentTalkEvent.HealOnCooldown => SelenaGameState.Collect(bridge, SelenaEvent.ClinicCooldown),
                TreatmentTalkEvent.CooldownReady => SelenaGameState.Collect(bridge, SelenaEvent.ClinicReady),
                _ => null,
            };
            if (!state.HasValue) { fallback?.RequestLine(context, deliver); return; }
            SelenaLineState situation = state.Value;
            // Answers at once when a line is ready or the server is off, otherwise within SelenaRealtime.MaxWaitSeconds.
            SelenaRealtime.Request(bridge, situation, generated =>
            {
                if (ticket != serial) return;
                string line = generated, slotId = "realtime";
                if (string.IsNullOrEmpty(line)) line = SelenaLinePool.Shared.Pick(situation, out slotId);
                if (string.IsNullOrEmpty(line)) { fallback?.RequestLine(context, deliver); return; }
                SelenaGameState.OnSpoken(bridge, situation.eventId);
                SelenaRealtime.NoteSpoken(line);
                UnityEngine.Debug.Log($"[Selena] {situation.eventId} -> {slotId}: {line}");
                deliver?.Invoke(line);
            });
        }
    }
}
