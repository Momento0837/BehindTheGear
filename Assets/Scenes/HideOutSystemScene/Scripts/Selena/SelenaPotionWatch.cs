using UnityEngine;

namespace BehindTheGear.HideOut
{
    // potion_refilled: Selena speaks only when the potion cabinet becomes ready WHILE its screen is being viewed.
    // If the potions were already ready when the screen opened, she stays silent.
    public sealed class SelenaPotionWatch
    {
        private bool watching, wasReady;
        private float nextPrefetch;

        public void Tick(MonoBehaviour host, HideOutPlayerBridge bridge, bool viewing, Transform inputRoot)
        {
            if (!viewing || !SelenaGameState.SelenaPresent(bridge)) { watching = false; return; }
            bool ready = SelenaGameState.PotionReady(bridge);
            if (!watching) { watching = true; wasReady = ready; return; } // Screen just opened: remember, do not speak.
            // Real-time line: the refill moment cannot wait for the server, so its line is asked for while waiting.
            if (!ready && Time.unscaledTime >= nextPrefetch)
            {
                nextPrefetch = Time.unscaledTime + .5f;
                var refilled = SelenaGameState.Collect(bridge, SelenaEvent.PotionRefilled);
                refilled.potionReady = true;
                SelenaRealtime.Prefetch(bridge, refilled);
            }
            if (ready && !wasReady)
                SelenaBaseDialogue.TrySay(host, bridge, SelenaGameState.Collect(bridge, SelenaEvent.PotionRefilled), inputRoot);
            wasReady = ready;
        }
    }
}
