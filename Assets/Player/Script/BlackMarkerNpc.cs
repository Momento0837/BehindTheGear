using UnityEngine;

/// <summary>Keeps the NPC presentation separate from the dialogue used for map instructions.</summary>
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(Animator), typeof(ConversationDialogue), typeof(DialogueTextEffects))]
public sealed class BlackMarkerNpc : MonoBehaviour
{
    private Animator animator;
    private ConversationDialogue conversation;
    private DialogueTextEffects textEffects;
    private bool visible;
    private static readonly int Idle = Animator.StringToHash("Base Layer.Idle");
    private static readonly int Laugh = Animator.StringToHash("Base Layer.Laugh");

    private void Awake()
    {
        animator = GetComponent<Animator>();
        conversation = GetComponent<ConversationDialogue>();
        textEffects = GetComponent<DialogueTextEffects>();
        string sceneName = gameObject.scene.name;
        SetMapVisible(sceneName != "Forest" && sceneName != "Market" && sceneName != "Garbage");
        if (animator.runtimeAnimatorController != null) animator.Play(Idle, 0, 0f);
    }

    private void OnEnable()
    {
        var player = FindFirstObjectByType<PlayerSpriteFacing>();
        var playerSprite = player != null ? player.GetComponent<SpriteRenderer>() : null;
        var npcSprite = GetComponent<SpriteRenderer>();
        if (playerSprite != null && npcSprite != null)
        {
            npcSprite.sortingLayerID = playerSprite.sortingLayerID;
            npcSprite.sortingOrder = playerSprite.sortingOrder - 1;
        }
        if (conversation == null) conversation = GetComponent<ConversationDialogue>();
        conversation.LineRevealStarted += HandleLineStarted;
    }

    private void OnDisable()
    {
        if (conversation != null)
        {
            conversation.LineRevealStarted -= HandleLineStarted;
        }
    }

    public void SetMapVisible(bool value)
    {
        visible = value;
        foreach (var renderer in GetComponentsInChildren<SpriteRenderer>(true)) renderer.enabled = value;
        foreach (var collider in GetComponents<Collider2D>()) collider.enabled = value;
        if (textEffects == null) textEffects = GetComponent<DialogueTextEffects>();
        if (textEffects != null) textEffects.enabled = value;
    }

    private void HandleLineStarted()
    {
        if (!conversation.CurrentLineNpcLaugh || !visible || animator == null || animator.runtimeAnimatorController == null) return;
        // One reveal-start event per line; skip/completion input cannot restart the laugh.
        animator.Play(Laugh, 0, 0f);
    }
}
