using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using BehindTheGear.HideOut;

public sealed class BaseArrivalTutorial : MonoBehaviour
{
    private static ConversationDialogue active;
    public static bool IsShowing => active != null;
    public const string TreatmentDialoguePath = "Dialogue/Hideout/base_treatment_tutorial";
    public const string PotionDialoguePath = "Dialogue/Hideout/base_potion_tutorial";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState() => active = null;

    public static bool ClinicUnlocked
    {
        get
        {
            var player = PlayerTargetRegistry.Current;
            var bridge = player != null ? player.GetComponent<HideOutPlayerBridge>() : null;
            return bridge != null && bridge.Initialized && bridge.State.baseTutorialComplete;
        }
    }
    private IEnumerator Start()
    {
        yield return null;
        var actor = PlayerTargetRegistry.Current;
        var bridge = actor != null ? actor.GetComponent<HideOutPlayerBridge>() : null;
        if (bridge == null || !bridge.Initialized) yield break;
        // Start when the black transition clears, even while the map entrance is still playing.
        while (SceneFadeTransition.IsRunning) yield return null;
        if (bridge.State.baseTutorialComplete)
        {
            TryShowAfterTreatment(this, bridge, transform);
            yield break;
        }
        var progress = actor.GetComponent<PrototypeTutorialState>();
        if (progress == null || !progress.SelenaArrived) yield break;
        TryShow(this, bridge, transform, "Dialogue/Hideout/base_upgrade_tutorial", false);
    }

    public static bool TryShowAfterTreatment(MonoBehaviour host, HideOutPlayerBridge bridge, Transform inputRoot)
    {
        if (bridge == null || !bridge.Initialized || !bridge.State.baseTutorialComplete
            || !bridge.State.baseFirstTreatmentComplete || bridge.State.baseTreatmentDialogueComplete) return false;
        return TryShow(host, bridge, inputRoot, TreatmentDialoguePath, true);
    }

    private static bool TryShow(MonoBehaviour host, HideOutPlayerBridge bridge, Transform inputRoot, string path, bool afterTreatment)
    {
        if (host == null || !host.isActiveAndEnabled || IsShowing || SelenaBaseDialogue.IsShowing) return false;
        var content = Resources.Load<TextAsset>(path);
        var template = FindObjectsByType<ConversationDialogue>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(d => d.BodyText != null && !d.IsPlaying);
        if (content == null || template == null)
        {
            Debug.LogError($"[Base tutorial] Missing dialogue JSON or presentation: {path}");
            return false;
        }
        var potionContent = afterTreatment ? Resources.Load<TextAsset>(PotionDialoguePath) : null;
        if (afterTreatment && potionContent == null)
        {
            Debug.LogError($"[Base tutorial] Missing dialogue JSON: {PotionDialoguePath}");
            return false;
        }
        host.StartCoroutine(Play(host.transform, bridge, inputRoot, template, content, potionContent, afterTreatment));
        return true;
    }

    private static IEnumerator Play(Transform parent, HideOutPlayerBridge bridge, Transform inputRoot,
        ConversationDialogue template, TextAsset content, TextAsset potionContent, bool afterTreatment)
    {
        var inputLock = BasePresentationInputLock.Acquire(inputRoot);
        ConversationDialogue presentation = null;
        try
        {
            active = presentation = template.CreateTutorialPresentation(parent);
            presentation.Play(content);
            // A missing/empty JSON must never grant an unlock.
            if (!presentation.IsPlaying) yield break;
            while (presentation != null && presentation.IsPlaying) yield return null;
            if (presentation == null || bridge == null) yield break;
            if (afterTreatment)
            {
                // Keep base input locked until the separate potion introduction also finishes.
                presentation.Play(potionContent);
                if (!presentation.IsPlaying) yield break;
                while (presentation != null && presentation.IsPlaying) yield return null;
                if (presentation == null || bridge == null) yield break;
            }
            // Separate completion from any heal/button transaction on this frame.
            yield return null;
            bool completed = bridge.Change(state =>
            {
                if (afterTreatment) state.baseTreatmentDialogueComplete = true;
                else { state.baseTutorialComplete = true; state.selenaRecruited = true; }
                return afterTreatment ? "물약 보관대가 열렸습니다." : "치료구역이 열렸습니다.";
            });
            if (completed && !afterTreatment)
            {
                var reward = Resources.Load<InventoryItemDefinition>("Story/TutorialRecoveryPotion");
                if (reward != null && bridge.Inventory.Add(reward, 1) != 1)
                {
                    bridge.State.storage.Add(new ItemStack(reward.ItemId, 1));
                    bridge.SetMessage("가방이 가득 차 회복 물약을 창고에 보관했습니다.");
                }
            }
        }
        finally
        {
            inputLock.Dispose();
            if (presentation != null) Destroy(presentation.gameObject);
            active = null;
        }
    }
}
