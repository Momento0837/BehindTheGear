using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using BehindTheGear.HideOut;

public sealed class BaseArrivalTutorial : MonoBehaviour
{
    public static bool ClinicUnlocked
    {
        get
        {
            var player = PlayerTargetRegistry.Current;
            var bridge = player != null ? player.GetComponent<HideOutPlayerBridge>() : null;
            return bridge != null && bridge.Initialized && bridge.State.baseTutorialComplete;
        }
    }
    IEnumerator Start()
    {
        yield return null;
        var actor = PlayerTargetRegistry.Current;
        if (actor == null) yield break;
        var progress = actor.GetComponent<PrototypeTutorialState>();
        var bridge = actor.GetComponent<HideOutPlayerBridge>();
        if (progress == null || !progress.SelenaArrived || bridge == null || bridge.State.baseTutorialComplete) yield break;
        while (SceneFadeTransition.IsRunning) yield return null;
        var template = FindObjectsByType<ConversationDialogue>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(d => d.BodyText != null);
        if (template == null) { Debug.LogError("[Base tutorial] No dialogue template available."); yield break; }
        var raycasters = GetComponentsInChildren<GraphicRaycaster>(true).Where(r => r.enabled).ToArray();
        foreach (var r in raycasters) r.enabled = false;
        var presentation = template.CreateTutorialPresentation(transform);
        try
        {
            presentation.Play(Resources.Load<TextAsset>("Dialogue/base_upgrade_tutorial"));
            while (presentation.IsPlaying) yield return null;
            bridge.State.baseTutorialComplete = true;
            bridge.SetSelenaRecruited();
            var reward = Resources.Load<InventoryItemDefinition>("Story/TutorialRecoveryPotion");
            if (reward != null && bridge.Inventory.Add(reward, 1) != 1)
            {
                // Preserve the one-time reward even when the bag is full.
                bridge.State.storage.Add(new ItemStack(reward.ItemId, 1));
                bridge.SetMessage("가방이 가득 차 회복 물약을 창고에 보관했습니다.");
            }
        }
        finally
        {
            foreach (var r in raycasters) if (r != null) r.enabled = true;
            if (presentation != null) Destroy(presentation.gameObject);
        }
    }
}
