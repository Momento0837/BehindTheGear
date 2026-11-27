using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace BehindTheGear.HideOut
{
    // Shows one Selena line in the base with the existing story Dialogue UI, the same way BaseArrivalTutorial
    // does (a temporary copy of the scene's ConversationDialogue canvas). Speaker "Selena" makes ConversationDialogue
    // use the existing Selena portrait as a temporary image until the Live2D portrait exists.
    // The line is a real-time one (SelenaRealtime, local SLM server) when one was prefetched for this exact state;
    // otherwise it is picked from the reviewed line pool. The base never waits for the server.
    public static class SelenaBaseDialogue
    {
        [Serializable] private sealed class OneLineFile { public Line[] lines; }
        [Serializable] private sealed class Line { public string speaker, text; }

        public const string Speaker = "Selena";
        // A coroutine stopped by its host being disabled never reaches `finally`; the destroyed copy clears the flag.
        public static bool IsShowing => showing && active != null;
        private static bool showing;
        private static ConversationDialogue active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState() { showing = false; active = null; }

        // Picks a line for the state and shows it. Returns false (and calls nothing) when Selena stays silent:
        // not recruited, no matching line, another line already showing, or no dialogue template in the scene.
        // `finished` runs after the player closed the line. `inputRoot`'s raycasters are disabled meanwhile so the
        // click that closes the line does not also press base buttons underneath.
        public static bool TrySay(MonoBehaviour host, HideOutPlayerBridge bridge, SelenaLineState state, Transform inputRoot, Action finished = null)
        {
            if (host == null || !host.isActiveAndEnabled || IsShowing || BaseArrivalTutorial.IsShowing || !SelenaGameState.SelenaPresent(bridge)) return false;
            var template = UnityEngine.Object.FindObjectsByType<ConversationDialogue>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(d => d.BodyText != null && !d.IsPlaying);
            if (template == null) { Debug.LogWarning("[Selena] No ConversationDialogue template in the loaded scenes."); return false; }
            string slotId = "realtime";
            if (!SelenaRealtime.TryTake(bridge, state, out string text)) text = SelenaLinePool.Shared.Pick(state, out slotId);
            if (string.IsNullOrEmpty(text)) return false;
            SelenaRealtime.NoteSpoken(text);
            Debug.Log($"[Selena] {state.eventId} -> {slotId}: {text}");
            showing = true;
            host.StartCoroutine(Play(host.transform, template, text, inputRoot, () => { SelenaGameState.OnSpoken(bridge, state.eventId); finished?.Invoke(); }));
            return true;
        }

        private static IEnumerator Play(Transform parent, ConversationDialogue template, string text, Transform inputRoot, Action finished)
        {
            var raycasters = inputRoot != null ? inputRoot.GetComponentsInChildren<GraphicRaycaster>(true).Where(r => r.enabled).ToArray() : Array.Empty<GraphicRaycaster>();
            foreach (var r in raycasters) r.enabled = false;
            ConversationDialogue presentation = null;
            TextAsset asset = null;
            try
            {
                active = presentation = template.CreateTutorialPresentation(parent);
                asset = new TextAsset(JsonUtility.ToJson(new OneLineFile { lines = new[] { new Line { speaker = Speaker, text = text } } }));
                presentation.Play(asset);
                while (presentation != null && presentation.IsPlaying) yield return null;
            }
            finally
            {
                foreach (var r in raycasters) if (r != null) r.enabled = true;
                if (presentation != null) UnityEngine.Object.Destroy(presentation.gameObject);
                if (asset != null) UnityEngine.Object.Destroy(asset);
                showing = false; active = null;
            }
            finished?.Invoke();
        }
    }
}
