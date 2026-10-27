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
    private bool laughPlayed;
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
        if (conversation == null) conversation = GetComponent<ConversationDialogue>();
        conversation.ChoiceSelected += HandleChoice;
    }

    private void OnDisable()
    {
        if (conversation != null) conversation.ChoiceSelected -= HandleChoice;
    }

    public void SetMapVisible(bool value)
    {
        visible = value;
        foreach (var renderer in GetComponentsInChildren<SpriteRenderer>(true)) renderer.enabled = value;
        foreach (var collider in GetComponents<Collider2D>()) collider.enabled = value;
        if (textEffects == null) textEffects = GetComponent<DialogueTextEffects>();
        if (textEffects != null) textEffects.enabled = value;
    }

    private void HandleChoice(bool accepted)
    {
        if (!accepted || !visible || laughPlayed || animator == null || animator.runtimeAnimatorController == null) return;
        // The controller returns to looping Idle at the end of this non-looping Laugh state.
        laughPlayed = true;
        animator.Play(Laugh, 0, 0f);
    }
}
