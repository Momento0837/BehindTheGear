#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Runs in the existing editor; scene changes exist only in Play mode.</summary>
[InitializeOnLoad]
public static class MarketDialogueCheck
{
    private const string Key = "BTG.MarketDialogueCheck";
    private const string Output = "Temp/MarketDialogueValidation";
    private static double deadline;
    private static double nextTime;
    private static Vector2 firstSize;
    private static float initialScale;
    private static float initialAlpha;
    private static float normalLens;
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    static MarketDialogueCheck() => EditorApplication.update += Tick;

    [MenuItem("Tools/Checks/Market NPC Bubble %#F7")]
    public static void Run()
    {
        if (Application.unityVersion != "6000.3.23f1" || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run in Unity 6000.3.23f1 outside Play mode.");
        Directory.CreateDirectory(Output);
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.isDirty) UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, Output + "/Unsaved-" + scene.name + ".unity", true);
        }
        SessionState.SetInt(Key, 1);
        deadline = EditorApplication.timeSinceStartup + 90;
        EditorApplication.isPlaying = true;
    }

    private static object Read(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    private static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Next(int stage, float delay = .15f)
    {
        SessionState.SetInt(Key, stage);
        nextTime = EditorApplication.timeSinceStartup + delay;
    }

    private static void Tick()
    {
        int stage = SessionState.GetInt(Key, 0);
        if (stage == 0 || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 90;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timeout at stage " + stage);
            if (EditorApplication.timeSinceStartup < nextTime) return;
            if (stage == 1)
            {
                SceneFadeTransition.LoadScene("Market", .4f);
                Next(2, .15f);
                return;
            }
            var flow = UnityEngine.Object.FindFirstObjectByType<StorySceneFlow>();
            if (flow == null || Read(flow, "inventory") == null) return;
            if (SceneFadeTransition.IsRunning)
            {
                if (stage == 2) Require(QuestTracker.CurrentQuestName != JsonUtility.FromJson<QuestDefinition>(Resources.Load<TextAsset>("Quests/equipment_tutorial").text).questName,
                    "Equipment quest stays hidden until fade completes");
                return;
            }
            var dialogue = (ConversationDialogue)Read(flow, "dialogue");
            var player = (PlayerMovement2D)Read(flow, "player");
            var inventory = player.GetComponent<PlayerInventory>();
            var portal = (GameObject)Read(flow, "portal");
            var bubble = (MarketNpcBubble)Read(flow, "marketBubble");
            switch (stage)
            {
                case 2:
                    Require(!dialogue.IsPlaying, "Equipment tutorial must not open dialogue");
                    // The fade coroutine clears its flag before the story coroutine resumes next frame.
                    if (QuestTracker.CurrentQuestName != JsonUtility.FromJson<QuestDefinition>(Resources.Load<TextAsset>("Quests/garbage_search").text).questName) return;
                    var search = UnityEngine.Object.FindObjectsByType<Interactable2D>(FindObjectsSortMode.None).First(i => i.name == "Interaction");
                    search.Interact(player.gameObject);
                    Require(QuestTracker.CurrentQuestName == "망토 두르기", "Searching supplies the cloak and advances the quest");
                    Require(dialogue.TalkingImage != null && !dialogue.TalkingImage.gameObject.activeSelf, "Talking hidden in Garbage");
                    int slot = Enumerable.Range(0, inventory.Capacity).Where(i => !inventory.GetSlot(i).IsEmpty && inventory.GetSlot(i).Item.IsEquipment).DefaultIfEmpty(-1).First();
                    Require(slot >= 0 && inventory.EquipFromSlot(slot), "Tutorial gear can be equipped");
                    Next(3);
                    break;
                case 3:
                case 10:
                    if (stage == 10)
                    {
                        var searchAgain = UnityEngine.Object.FindObjectsByType<Interactable2D>(FindObjectsSortMode.None).FirstOrDefault(i => i.name == "Interaction");
                        if (searchAgain != null) searchAgain.Interact(player.gameObject);
                    }
                    if (!(bool)Read(flow, "ready")) return;
                    portal.GetComponent<Interactable2D>().Interact(player.gameObject);
                    Next(stage == 3 ? 4 : 11);
                    break;
                case 4:
                    Require(QuestTracker.CurrentQuestName == string.Empty, "Conversation quest waits for greeting completion");
                    normalLens = Camera.main.orthographic ? Camera.main.orthographicSize : Camera.main.fieldOfView;
                    Require(bubble != null && !bubble.HasPresented, "Market entry does not greet before passing");
                    player.enabled = false;
                    player.transform.position = dialogue.transform.position + Vector3.left;
                    Next(5, .08f);
                    break;
                case 5:
                    Require(!bubble.HasPresented, "Passing NPC before X 70 does not start greeting");
                    player.transform.position = new Vector3(70f, player.transform.position.y, 0f);
                    Next(6, .05f);
                    break;
                case 6:
                    Require(bubble.HasPresented && dialogue.TalkingImage.gameObject.activeSelf, "Passing reveals Talking");
                    Require(!dialogue.IsPlaying && bubble.IsPresenting && !player.enabled, "Greeting locks controls without opening Dialogue panel");
                    Require(dialogue.TalkingImage.type == Image.Type.Sliced && dialogue.TalkingImage.sprite.border != Vector4.zero, "Authored 9-slice sprite retained");
                    firstSize = dialogue.TalkingImage.rectTransform.sizeDelta;
                    initialScale = dialogue.TalkingImage.transform.localScale.x;
                    initialAlpha = dialogue.TalkingImage.GetComponent<CanvasGroup>().alpha;
                    Require(initialScale >= .8f && initialScale < 1f && initialAlpha > 0f && initialAlpha < 1f, "Scale and opacity entrance in progress");
                    Next(7, 2.5f);
                    break;
                case 7:
                    Require((Camera.main.orthographic ? Camera.main.orthographicSize : Camera.main.fieldOfView) < normalLens, "Greeting camera zooms in");
                    var rect = dialogue.TalkingImage.rectTransform;
                    Require(rect.sizeDelta.x > firstSize.x && rect.sizeDelta.y >= firstSize.y, "Bubble grows with revealed characters");
                    Require(Mathf.Approximately(rect.localScale.x, 1f) && Mathf.Approximately(dialogue.TalkingImage.GetComponent<CanvasGroup>().alpha, 1f), "Entrance reaches full scale and opacity");
                    Require(Quaternion.Angle(rect.localRotation, Quaternion.identity) < .01f, "Follow through settles at zero");
                    var label = dialogue.TalkingImage.GetComponentInChildren<TMP_Text>();
                    Require(label.text == MarketNpcBubble.FormatBubbleText(ConversationDialogue.GetOpeningText(Resources.Load<TextAsset>("Dialogue/quest_offer"))), "First greeting keeps all text with sentence breaks");
                    Require(label.textInfo.lineCount > 1 && label.textWrappingMode == TextWrappingModes.Normal, "Greeting wraps at sentence boundaries");
                    Require(Mathf.Approximately(label.fontSize, 21.6f) && label.rectTransform.offsetMin.x == 26f && label.rectTransform.offsetMax.y == -26f, "90 percent text size and 26 unit body padding");
                    ScreenCapture.CaptureScreenshot(Output + "/passing.png");
                    player.transform.position += Vector3.left * 3;
                    Next(8);
                    break;
                case 8:
                    // Camera movement can alter screen coordinates; the tail must still project from the NPC head.
                    var camera = Camera.main;
                    var npcCanvas = dialogue.TalkingImage.canvas.rootCanvas;
                    Vector3 head = dialogue.transform.position + Vector3.up * (float)Read(bubble, "headOffset");
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(npcCanvas.transform as RectTransform,
                        camera.WorldToScreenPoint(head), null, out Vector2 expected);
                    var corners = new Vector3[4];
                    dialogue.TalkingImage.rectTransform.GetWorldCorners(corners);
                    foreach (var corner in corners)
                        Require(((RectTransform)npcCanvas.transform).rect.Contains(npcCanvas.transform.InverseTransformPoint(corner)), "Entire bubble stays on screen");
                    Call(bubble, "Advance");
                    Require((int)Read(bubble, "lineIndex") == 1, "Second input advances to greeting line two");
                    Call(bubble, "Advance");
                    Require((bool)Read(bubble, "fastCompleting"), "Reveal input starts fast completion");
                    Next(15, .22f);
                    break;
                case 15:
                    Require(dialogue.TalkingImage.rectTransform.sizeDelta.x > (float)Read(bubble, "fullWidth"), "Fast width overshoots final content width");
                    var fastText = dialogue.TalkingImage.GetComponentInChildren<TMP_Text>();
                    fastText.ForceMeshUpdate();
                    Require(fastText.textBounds.size.x <= dialogue.TalkingImage.rectTransform.sizeDelta.x - 52f + 2f, "Fast glyphs stay inside wrapped bubble width");
                    Next(16, .4f);
                    break;
                case 16:
                    Require(!(bool)Read(bubble, "fastCompleting") && Mathf.Abs(dialogue.TalkingImage.rectTransform.sizeDelta.x - (float)Read(bubble, "fullWidth")) < .1f, "Fast completion settles at full width");
                    Require((int)Read(bubble, "revealedCount") == ((string)Read(bubble, "opening")).Length, "Fast completion reveals entire second line");
                    var wrappedText = dialogue.TalkingImage.GetComponentInChildren<TMP_Text>();
                    wrappedText.ForceMeshUpdate();
                    Require(wrappedText.textBounds.size.y <= wrappedText.rectTransform.rect.height + 1f, "All wrapped lines fit bubble height");
                    Call(bubble, "Advance");
                    Require((int)Read(bubble, "lineIndex") == 2, "Third greeting line follows second");
                    Call(bubble, "Advance");
                    Next(17, .7f);
                    break;
                case 17:
                    Require(bubble.IsPresenting && !player.enabled, "Third line waits for input and keeps controls locked");
                    Call(bubble, "Advance");
                    Next(12, 4f);
                    break;
                case 12:
                    Require(!bubble.IsPresenting && !dialogue.TalkingImage.gameObject.activeSelf && player.enabled, "Greeting finishes and restores control");
                    Require(QuestTracker.CurrentQuestName == "???와 대화", "Conversation quest appears after greeting");
                    Require(Mathf.Abs((Camera.main.orthographic ? Camera.main.orthographicSize : Camera.main.fieldOfView) - normalLens) < .1f, "Camera returns to normal lens");
                    player.transform.position = portal.transform.position;
                    Require(portal.activeSelf, "Portal available before accepting request");
                    portal.GetComponent<Interactable2D>().Interact(player.gameObject);
                    Next(13, .4f);
                    break;
                case 13:
                    Require(dialogue.IsPlaying && !player.enabled, "Using portal before accepting opens reminder");
                    Require(dialogue.CurrentSpeaker == "Ethan", "Reminder uses Ethan portrait/speaker");
                    Require(!(bool)Read(flow, "ready"), "Portal remains locked until acceptance");
                    Call(dialogue, "FinishDialogue");
                    Next(14);
                    break;
                case 14:
                    Require(!dialogue.IsPlaying && player.enabled, "Reminder closes and restores controls without looping");
                    player.transform.position = dialogue.transform.position + Vector3.left;
                    dialogue.GetComponent<Interactable2D>().Interact(player.gameObject);
                    Require((int)Read(dialogue, "startingLine") == 3 && dialogue.IsPlaying, "Interaction starts at fourth line");
                    Require(!dialogue.TalkingImage.gameObject.activeSelf, "Normal dialogue hides bubble");
                    Next(18, 1f);
                    break;
                case 18:
                    Require(Read(dialogue, "choiceRoot") != null, "Fourth line immediately offers choices");
                    Call(dialogue, "SelectChoice", false);
                    Next(19, .5f);
                    break;
                case 19:
                    Call(dialogue, "FinishDialogue");
                    Next(20, .2f);
                    break;
                case 20:
                    dialogue.GetComponent<Interactable2D>().Interact(player.gameObject);
                    Require((int)Read(dialogue, "startingLine") == 3 && dialogue.IsPlaying, "After rejection only fourth offer line repeats");
                    Next(21, 1f);
                    break;
                case 21:
                    Require(Read(dialogue, "choiceRoot") != null, "Repeated fourth line offers choices again");
                    Next(9);
                    break;
                case 9:
                    SceneManager.LoadScene("Market");
                    Next(10, .6f);
                    break;
                case 11:
                    Require(bubble != null && !bubble.HasPresented, "Fresh Market encounter has no passing greeting");
                    dialogue.GetComponent<Interactable2D>().Interact(player.gameObject);
                    Require((int)Read(dialogue, "startingLine") == 3 && dialogue.IsPlaying, "Direct interaction starts at fourth line");
                    Require(!bubble.HasPresented && !dialogue.TalkingImage.gameObject.activeSelf, "Direct interaction never shows bubble");
                    File.WriteAllText(Output + "/result.txt", "PASS Unity " + Application.unityVersion + "\nX70 greeting; three input-driven lines; fast reveal stays inside bubble; width overshoots and settles; controls and camera restore after final advance; quest follows greeting; portal reminder; fourth line offers choices; rejection repeats fourth line and choices; direct interaction starts at fourth line.\n");
                    SessionState.SetInt(Key, 0);
                    Debug.Log("MARKET_DIALOGUE_CHECK_PASS");
                    EditorApplication.isPlaying = false;
                    break;
            }
        }
        catch (Exception exception)
        {
            File.WriteAllText(Output + "/result.txt", "FAIL stage " + stage + "\n" + exception);
            SessionState.SetInt(Key, 0);
            Debug.LogException(exception);
            EditorApplication.isPlaying = false;
        }
    }
}
#endif
