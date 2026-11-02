using System;
using System.Globalization;
using TMPro;
using UnityEngine;

[Serializable]
public sealed class QuestDefinition
{
    public string questName;
    public string objective;
}

/// <summary>Session quest state shared by the story flow and standalone quest givers.</summary>
public static class QuestTracker
{
    public static string CurrentQuestName { get; private set; } = string.Empty;
    public static string CurrentObjective { get; private set; } = string.Empty;
    public static event Action Changed;
    private static object owner;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        owner = null;
        CurrentQuestName = string.Empty;
        CurrentObjective = string.Empty;
        Changed?.Invoke();
    }

    public static void Accept(object source, TextAsset questData)
    {
        if (questData == null)
        {
            Debug.LogError("[Quest] Missing quest definition JSON.");
            return;
        }
        var definition = JsonUtility.FromJson<QuestDefinition>(questData.text);
        if (definition == null || string.IsNullOrWhiteSpace(definition.questName))
        {
            Debug.LogError("[Quest] Quest definition needs a questName: " + questData.name);
            return;
        }
        Accept(source, definition.questName, definition.objective);
    }

    public static void Accept(object source, string questName, string objective)
    {
        owner = source;
        CurrentQuestName = questName ?? string.Empty;
        CurrentObjective = objective ?? string.Empty;
        Changed?.Invoke();
    }

    public static void Complete(object source)
    {
        if (!ReferenceEquals(owner, source)) return;
        owner = null;
        CurrentQuestName = string.Empty;
        CurrentObjective = string.Empty;
        Changed?.Invoke();
    }
}

/// <summary>Only updates the authored quest icon/text; preserves their font and geometry.</summary>
[DisallowMultipleComponent]
public sealed class InGameQuestHUD : MonoBehaviour
{
    [SerializeField] private GameObject questRoot;
    [SerializeField] private TMP_Text questText;

    private void OnEnable()
    {
        QuestTracker.Changed += Refresh;
        Refresh();
    }

    private void OnDisable() => QuestTracker.Changed -= Refresh;

    public void Refresh()
    {
        string content = FormatObjective(QuestTracker.CurrentQuestName);
        if (questText != null)
        {
            questText.richText = false;
            questText.textWrappingMode = TextWrappingModes.NoWrap;
            questText.text = content;
        }
        if (questRoot != null) questRoot.SetActive(content.Length > 0);
    }

    public static string FormatObjective(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;
        content = content.Replace('\r', ' ').Replace('\n', ' ').Trim();
        int[] characters = StringInfo.ParseCombiningCharacters(content);
        return characters.Length > 16 ? content.Substring(0, characters[16]) + "..." : content;
    }
}
