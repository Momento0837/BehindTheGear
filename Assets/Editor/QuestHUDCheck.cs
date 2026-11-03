using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;

public static class QuestHUDCheck
{
    [MenuItem("Tools/Checks/In Game Quest")]
    public static void Run()
    {
        const string result = "Temp/QuestHUDValidation/result.txt";
        Directory.CreateDirectory(Path.GetDirectoryName(result));
        GameObject root = null;
        InGameQuestHUD hud = null;
        var owner = new object();
        try
        {
            Require(Application.unityVersion == "6000.3.23f1", "Unity version");
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play mode before checking");
            root = PrefabUtility.LoadPrefabContents("Assets/!UI/Ingame/InGame_Canvas.prefab");
            hud = root.GetComponent<InGameQuestHUD>();
            Require(hud != null, "Quest HUD attached");
            var quest = root.transform.Find("Quest").gameObject;
            var text = quest.GetComponentInChildren<TMP_Text>(true);
            var font = text.font;
            var material = text.fontSharedMaterial;
            var size = text.rectTransform.sizeDelta;
            Require(!quest.activeSelf && string.IsNullOrEmpty(text.text), "Hidden and empty in prefab");
            Call(hud, "OnEnable");
            Require(!quest.activeSelf, "Hidden before accepting / when rejecting");
            foreach (string guide in new[] { "forest_combat", "forest_exit", "garbage_search", "equipment_tutorial", "garbage_exit", "market_talk", "market_retry" })
            {
                var guideData = Resources.Load<TextAsset>("Quests/" + guide);
                Require(guideData != null, "Story guide data exists: " + guide);
                var guideDefinition = JsonUtility.FromJson<QuestDefinition>(guideData.text);
                QuestTracker.Accept(owner, guideData);
                Require(quest.activeSelf && text.text == InGameQuestHUD.FormatObjective(guideDefinition.questName), "Automatic story guide updates UI: " + guide);
                Require(QuestTracker.CurrentObjective == guideDefinition.objective && !string.IsNullOrWhiteSpace(guideDefinition.objective), "Guide objective stored: " + guide);
            }
            var data = Resources.Load<TextAsset>("Quests/market_quest");
            Require(data != null, "Stored quest data exists");
            var definition = JsonUtility.FromJson<QuestDefinition>(data.text);
            QuestTracker.Accept(owner, data);
            Require(quest.activeSelf && text.text == definition.questName, "Acceptance displays separately stored name");
            Require(QuestTracker.CurrentObjective == definition.objective && QuestTracker.CurrentQuestName != QuestTracker.CurrentObjective, "Name and objective remain separate");
            string sixteen = "가나다라마바사아자차카타파하거너";
            QuestTracker.Accept(owner, sixteen, "목표 내용");
            Require(text.text == sixteen, "Exactly 16 characters unchanged");
            QuestTracker.Accept(owner, sixteen + "더 긴 내용", "목표 내용");
            Require(text.text == sixteen + "더 긴 내용", "Long titles remain complete");
            Require(QuestTracker.CurrentQuestName == sixteen + "더 긴 내용", "Full stored name is not truncated");
            string emoji = string.Concat(System.Linq.Enumerable.Repeat("😀", 16));
            Require(InGameQuestHUD.FormatObjective(emoji + "끝") == emoji + "끝", "Unicode characters remain intact");
            var position = text.rectTransform.anchoredPosition;
            float originalTop = position.y + text.rectTransform.rect.height * (1f - text.rectTransform.pivot.y);
            string longTitle = string.Concat(System.Linq.Enumerable.Repeat("긴 퀘스트 제목을 줄바꿈하여 모두 표시합니다. ", 5));
            QuestTracker.Accept(owner, longTitle, "목표 내용");
            text.ForceMeshUpdate(true);
            Require(text.text == longTitle.Trim() && text.textInfo.lineCount > 1, "Long Korean title wraps without truncation");
            Require(Mathf.Abs(text.rectTransform.anchoredPosition.x - position.x) < .01f, "Right edge retained");
            Require(Mathf.Abs(text.rectTransform.anchoredPosition.y + text.rectTransform.rect.height * (1f - text.rectTransform.pivot.y) - originalTop) < .01f, "Top edge retained while height grows");
            QuestTracker.Accept(owner, new TextAsset("{\"questName\":\"제목\",\"objective\":\"첫째 줄\\n둘째 줄\",\"showObjective\":true}"));
            Require(text.text == "제목\n첫째 줄\n둘째 줄", "JSON can opt into multiline description");
            Require(InGameQuestHUD.FormatObjective("첫째\r\n둘째") == "첫째\n둘째", "Explicit line breaks preserved");
            QuestTracker.Complete(new object());
            Require(quest.activeSelf, "Other quests cannot clear current objective");
            Call(hud, "OnDisable");
            Call(hud, "OnEnable");
            Require(text.text == "제목\n첫째 줄\n둘째 줄", "UI reopening restores current quest and description");
            QuestTracker.Complete(owner);
            Require(!quest.activeSelf && text.text == string.Empty, "Completion hides and clears");
            Require(QuestTracker.CurrentQuestName == string.Empty && QuestTracker.CurrentObjective == string.Empty, "Completion clears both name and objective");
            Require(text.font == font && text.fontSharedMaterial == material && text.rectTransform.sizeDelta == size, "Font, material and layout preserved");
            File.WriteAllText(result, "PASS Unity " + Application.unityVersion + "\nAll story JSON definitions, full titles, Korean wrapping, fixed top/right edges, optional multiline descriptions, Unicode, reopening, completion, original fonts/material and empty layout restoration. No scene or prefab saved.\n");
            Debug.Log("QUEST_HUD_CHECK_PASS");
        }
        catch (Exception exception)
        {
            File.WriteAllText(result, "FAIL\n" + exception);
            Debug.LogException(exception);
        }
        finally
        {
            QuestTracker.Complete(owner);
            if (hud != null) Call(hud, "OnDisable");
            if (root != null) PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void Call(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
