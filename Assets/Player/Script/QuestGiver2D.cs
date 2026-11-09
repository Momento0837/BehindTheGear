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
        Offer,
        Acceptance,
        Rejection,
        Completion
    }

    [Header("Dialogue")]
    [SerializeField] private ConversationDialogue conversationDialogue;
    [SerializeField] private TextAsset questOfferDialogue;
    [SerializeField] private TextAsset questAcceptedDialogue;
    [SerializeField] private TextAsset questRejectedDialogue;
    [SerializeField] private TextAsset questCompletedDialogue;

    [Header("Quest object")]
    [Tooltip("JSON containing the questName and objective fields.")]
    [SerializeField] private TextAsset questDefinition;
    [SerializeField] private QuestPickup2D questItemPrefab;
    [SerializeField] private Transform questItemSpawnPoint;

    [Header("Optional scene feedback")]
    [SerializeField] private UnityEvent onQuestAccepted;
    [SerializeField] private UnityEvent onQuestCompleted;

    private QuestPickup2D activeQuestItem;
    private DialoguePurpose activeDialoguePurpose;

    private string QuestId => QuestJournal.Id(questDefinition != null ? questDefinition : Resources.Load<TextAsset>("Quests/collection_quest"));
    public QuestStage CurrentStage => (QuestStage)(QuestJournal.Find(QuestId)?.stage ?? 0);
    public void ConfigureDefinition(TextAsset definition) => questDefinition = definition;
    private void Start()
    {
        if (CurrentStage == QuestStage.WaitingForItem && QuestJournal.Definition(QuestId)?.objectives[0].kind == "collect")
            SpawnQuestItem();
    }

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
                PlayQuestDialogue(questOfferDialogue, DialoguePurpose.Offer);
                break;
            case QuestStage.ReadyToComplete:
                PlayQuestDialogue(questCompletedDialogue, DialoguePurpose.Completion);
                break;
            case QuestStage.WaitingForItem:
                var record = QuestJournal.Find(QuestId);
                var definition = QuestJournal.Definition(QuestId);
                conversationDialogue.ShowChoices("김철수", definition.objective + " " +
                    string.Join(" · ", System.Array.ConvertAll(record.amounts, n => n.ToString())), "확인", "닫기", _ => { });
                break;
            case QuestStage.Completed:
                conversationDialogue.ShowChoices("김철수", "이미 완료한 부탁이야. 고마워!", "확인", "닫기", _ => { });
                break;
        }
    }

    private void ShowQuestChoices()
    {
        conversationDialogue.ShowChoices("김철수", "이 부탁을 받아줄래?", "수락", "거절", HandleQuestChoice);
    }

    private void PlayQuestDialogue(TextAsset dialogue, DialoguePurpose purpose)
    {
        if (dialogue == null)
        {
            Debug.LogError($"[Quest] Assign the {purpose} dialogue JSON in the Inspector.", this);
            return;
        }

        activeDialoguePurpose = purpose;
        if (purpose == DialoguePurpose.Offer)
            conversationDialogue.PlayWithChoices(dialogue, "수락", "거절", HandleQuestChoice);
        else
            conversationDialogue.Play(dialogue);
    }

    private void HandleDialogueFinished()
    {
        DialoguePurpose finishedPurpose = activeDialoguePurpose;
        activeDialoguePurpose = DialoguePurpose.None;

        if (finishedPurpose == DialoguePurpose.Acceptance && CurrentStage == QuestStage.NotStarted)
        {
            var definition = QuestJournal.Definition(QuestId);
            if (definition?.objectives == null) return;
            if (definition.objectives[0].kind == "collect" && !SpawnQuestItem()) return;
            QuestJournal.Accept(QuestId);
            onQuestAccepted?.Invoke();
        }
        else if (finishedPurpose == DialoguePurpose.Rejection)
        {
            // Rejection leaves the quest available for another conversation.
        }
        else if (finishedPurpose == DialoguePurpose.Completion && CurrentStage == QuestStage.ReadyToComplete)
        {
            var inventory = PlayerTargetRegistry.Current != null ? PlayerTargetRegistry.Current.GetComponent<PlayerInventory>() : FindFirstObjectByType<PlayerInventory>();
            var bridge = inventory != null ? inventory.GetComponent<BehindTheGear.HideOut.HideOutPlayerBridge>() : null;
            if (bridge == null && inventory != null && SaveGameSession.Settings != null)
            {
                bridge = inventory.gameObject.AddComponent<BehindTheGear.HideOut.HideOutPlayerBridge>();
                bridge.Initialize(SaveGameSession.Settings, false);
            }
            if (QuestJournal.Report(QuestId, bridge)) onQuestCompleted?.Invoke();
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
        // PlayerInventory.Add already recorded the accepted item amount.
    }
}
