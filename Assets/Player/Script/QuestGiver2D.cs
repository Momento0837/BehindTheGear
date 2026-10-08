using UnityEngine;
using UnityEngine.Events;

/// <summary>Prototype quest: finish the acceptance dialogue, collect one spawned object, then finish the completion dialogue.</summary>
public sealed class QuestGiver2D : MonoBehaviour
{
    public enum QuestStage
    {
        NotStarted,
        WaitingForItem,
        ReadyToComplete,
        Completed
    }

    private enum DialoguePurpose
    {
        None,
        Acceptance,
        Rejection,
        Completion
    }

    [Header("Dialogue")]
    [SerializeField] private ConversationDialogue conversationDialogue;
    [SerializeField] private TextAsset questAcceptedDialogue;
    [SerializeField] private TextAsset questRejectedDialogue;
    [SerializeField] private TextAsset questCompletedDialogue;

    [Header("Quest object")]
    [SerializeField] private QuestPickup2D questItemPrefab;
    [SerializeField] private Transform questItemSpawnPoint;

    [Header("Optional scene feedback")]
    [SerializeField] private UnityEvent onQuestAccepted;
    [SerializeField] private UnityEvent onQuestCompleted;

    private QuestPickup2D activeQuestItem;
    private DialoguePurpose activeDialoguePurpose;

    public QuestStage CurrentStage { get; private set; }

    private void Awake()
    {
        if (conversationDialogue == null)
            conversationDialogue = GetComponent<ConversationDialogue>();
    }

    private void OnEnable()
    {
        if (conversationDialogue == null)
            conversationDialogue = GetComponent<ConversationDialogue>();
        if (conversationDialogue != null)
            conversationDialogue.Finished += HandleDialogueFinished;
    }

    private void OnDisable()
    {
        if (conversationDialogue != null)
            conversationDialogue.Finished -= HandleDialogueFinished;
    }

    public void Interact()
    {
        if (conversationDialogue == null)
        {
            Debug.LogError("[Quest] Quest giver needs ConversationDialogue on the same object.", this);
            return;
        }
        if (conversationDialogue.IsPlaying) return;

        switch (CurrentStage)
        {
            case QuestStage.NotStarted:
                ShowQuestChoices();
                break;
            case QuestStage.ReadyToComplete:
                PlayQuestDialogue(questCompletedDialogue, DialoguePurpose.Completion);
                break;
            case QuestStage.WaitingForItem:
                // Keep one active pickup in the world until it is collected.
                break;
            case QuestStage.Completed:
                CurrentStage = QuestStage.NotStarted;
                ShowQuestChoices();
                break;
        }
    }

    private void ShowQuestChoices()
    {
        conversationDialogue.ShowChoices("김철수", "반가워 퀘스트 받을레?", "수락", "거절", HandleQuestChoice);
    }

    private void PlayQuestDialogue(TextAsset dialogue, DialoguePurpose purpose)
    {
        if (dialogue == null)
        {
            Debug.LogError($"[Quest] Assign the {purpose} dialogue JSON in the Inspector.", this);
            return;
        }

        activeDialoguePurpose = purpose;
        conversationDialogue.Play(dialogue);
    }

    private void HandleDialogueFinished()
    {
        DialoguePurpose finishedPurpose = activeDialoguePurpose;
        activeDialoguePurpose = DialoguePurpose.None;

        if (finishedPurpose == DialoguePurpose.Acceptance && CurrentStage == QuestStage.NotStarted)
        {
            if (!SpawnQuestItem()) return;
            CurrentStage = QuestStage.WaitingForItem;
            onQuestAccepted?.Invoke();
        }
        else if (finishedPurpose == DialoguePurpose.Rejection)
        {
            // Rejection leaves the quest available for another conversation.
        }
        else if (finishedPurpose == DialoguePurpose.Completion && CurrentStage == QuestStage.ReadyToComplete)
        {
            CurrentStage = QuestStage.Completed;
            onQuestCompleted?.Invoke();
        }
    }

    private void HandleQuestChoice(bool accepted)
    {
        if (accepted)
            PlayQuestDialogue(questAcceptedDialogue, DialoguePurpose.Acceptance);
        else
            PlayQuestDialogue(questRejectedDialogue, DialoguePurpose.Rejection);
    }

    private bool SpawnQuestItem()
    {
        if (questItemPrefab == null || questItemSpawnPoint == null)
        {
            Debug.LogError("[Quest] Assign both a quest item prefab and its spawn point in the Inspector.", this);
            return false;
        }

        activeQuestItem = Instantiate(questItemPrefab, questItemSpawnPoint.position, Quaternion.identity);
        activeQuestItem.Bind(this);
        return true;
    }

    public void NotifyQuestItemCollected(QuestPickup2D item)
    {
        if (CurrentStage != QuestStage.WaitingForItem || item == null || item != activeQuestItem) return;
        activeQuestItem = null;
        CurrentStage = QuestStage.ReadyToComplete;
    }
}
