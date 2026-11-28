#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SelenaQuestContinuationCheck
{
    private const string Folder = "Logs/Validation/SelenaQuest";
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    [Serializable] private sealed class Lines { public Line[] lines; }
    [Serializable] private sealed class Line { public string speaker, 표정, 연출, text; }
    static SelenaQuestContinuationCheck() => EditorApplication.update += Poll;
    private static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode
            || !File.Exists(Folder + "/check-v2.request")) return;
        File.Delete(Folder + "/check-v2.request");
        Run();
    }
    [MenuItem("Tools/Checks/Selena Quest Continuation")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Directory.CreateDirectory(Folder);
        var result = new StringBuilder();
        var ownerField = typeof(QuestTracker).GetField("owner", Static);
        object previousOwner = ownerField.GetValue(null);
        string previousTitle = QuestTracker.CurrentQuestName, previousObjective = QuestTracker.CurrentObjective;
        bool previousShow = QuestTracker.ShowObjective, previousCompletion = QuestTracker.IsCompletionChange;
        var preview = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        IEnumerator conversation = null;
        try
        {
            root = new GameObject("Selena Quest Check");
            SceneManager.MoveGameObjectToScene(root, preview);
            root.SetActive(false);
            var flow = root.AddComponent<StorySceneFlow>();
            var dialogue = root.AddComponent<ConversationDialogue>();
            Field(flow, "dialogue").SetValue(flow, dialogue);
            string[] ids = { "forest_selena_talk", "selena_take_cover", "neighborhood_hideout" };
            string[] titles = { "셀레나와 대화하기", "몸을 피하기", "개구멍 안으로 몸을 밀어넣기" };
            for (int i = 0; i < ids.Length; i++)
            {
                var asset = Resources.Load<TextAsset>("Quests/" + ids[i]);
                Require(asset != null, "Quest JSON exists: " + ids[i]);
                var definition = JsonUtility.FromJson<QuestDefinition>(asset.text);
                Require(definition.questId == ids[i] && definition.questName == titles[i] && !definition.showObjective,
                    "Quest title/schema: " + ids[i]);
            }
            var source = Resources.Load<TextAsset>("Dialogue/Forest_4/selena");
            var cueLine = JsonUtility.FromJson<Lines>(source.text).lines.Single(line => line.연출 == "quest:take_cover");
            Require(cueLine.speaker == "Ethan" && cueLine.표정 == "곤란", "Cue belongs to Ethan's troubled expression");

            Call(flow, "ShowGuide", ids[0]);
            Require(QuestTracker.CurrentQuestName == titles[0], "Cutscene successor quest takes ownership");
            int forwarded = 0;
            IEnumerator PreviousEvent(string cue) { forwarded++; yield break; }
            Func<string, IEnumerator> original = PreviousEvent;
            dialogue.BeforeLineEvent = original;
            conversation = (IEnumerator)Call(flow, "SelenaConversation");
            Require(conversation.MoveNext(), "Selena conversation installs handlers");
            var before = dialogue.BeforeLineEvent("quest:take_cover");
            Drain(before);
            Require(QuestTracker.CurrentQuestName == titles[0], "Cue waits until the portrait/line is revealed");
            ((Action)Field(dialogue, "LineRevealStarted").GetValue(dialogue))?.Invoke();
            Require(QuestTracker.CurrentQuestName == titles[1] && QuestTracker.IsCompletionChange,
                "Line reveal updates the quest after setting the troubled portrait");
            var other = dialogue.BeforeLineEvent("unrelated");
            Drain(other);
            Require(forwarded == 1, "Other dialogue cues are preserved");
            ((IDisposable)conversation).Dispose(); conversation = null;
            Require(ReferenceEquals(dialogue.BeforeLineEvent, original) && Field(dialogue, "LineRevealStarted").GetValue(dialogue) == null,
                "Dialogue handlers are restored after the conversation");

            Call(flow, "ShowGuide", ids[1]);
            Call(flow, "AdvanceGuide", ids[2]);
            Require(QuestTracker.CurrentQuestName == titles[2], "Automatic walk ends with the crawlspace objective");
            var saved = new FieldReturnState { questName = titles[2], objective = "saved objective", showObjective = true,
                story = new StoryFieldState { chapter = 2, guide = ids[2] } };
            var chapter = Field(flow, "currentChapter");
            chapter.SetValue(flow, Enum.ToObject(chapter.FieldType, 1));
            flow.CompleteHideoutEntranceQuest(saved);
            Require(saved.questName == titles[2], "Other chapter cannot complete the entrance step");
            chapter.SetValue(flow, Enum.ToObject(chapter.FieldType, 2));
            flow.CompleteHideoutEntranceQuest(saved);
            Require(string.IsNullOrEmpty(QuestTracker.CurrentQuestName) && (string)Field(flow, "currentGuide").GetValue(flow) == "",
                "Successful base entry clears the live quest");
            Require(saved.questName == "" && saved.objective == "" && !saved.showObjective && saved.story.guide == "",
                "Captured field-return objective is also cleared");
            var restored = JsonUtility.FromJson<FieldReturnState>(JsonUtility.ToJson(saved));
            restored.RestoreQuest(flow);
            Require(QuestTracker.CurrentQuestName == "", "Save/continue/return cannot restore the completed objective");
            result.AppendLine("PASS: quest JSONs, troubled-portrait event timing, cue cleanup, automatic-walk successor, base completion, and saved return state.");
        }
        catch (Exception error)
        {
            result.AppendLine("FAIL: " + error);
            Debug.LogException(error);
        }
        finally
        {
            (conversation as IDisposable)?.Dispose();
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(preview);
            QuestTracker.Restore(previousOwner, previousTitle, previousObjective, previousShow);
            typeof(QuestTracker).GetField("<IsCompletionChange>k__BackingField", Static)?.SetValue(null, previousCompletion);
            File.WriteAllText(Folder + "/result.txt", result.ToString());
        }
    }
    private static void Drain(IEnumerator routine)
    {
        try
        {
            while (routine.MoveNext())
                if (routine.Current is IEnumerator nested) Drain(nested);
        }
        finally { (routine as IDisposable)?.Dispose(); }
    }
    private static FieldInfo Field(object target, string name) => target.GetType().GetField(name, Instance);
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Instance).Invoke(target, args);
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
#endif
