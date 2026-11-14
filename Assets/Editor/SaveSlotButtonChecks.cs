using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BehindTheGear.HideOut;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using InputKey = UnityEngine.InputSystem.Key;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Menu integration checks in the existing editor, with slots restricted to project Temp.</summary>
[InitializeOnLoad]
public static class SaveSlotButtonChecks
{
    private const string Folder = "Temp/SaveSlotButtonValidation/";
    private const string Key = "BTG.SaveSlotButtonChecks";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static IEnumerator routine;
    private static readonly StringBuilder report = new();
    private static double deadline;
    private static Keyboard keyboard, originalKeyboard;
    private static Mouse mouse, originalMouse;
    private static Gamepad gamepad, originalGamepad;
    private static string originalValidationRoot;
    private static int errors;
    private static int lastFrame = -1;
    private static bool originalBackground;

    static SaveSlotButtonChecks()
    {
        if ((File.Exists(Folder + "run.flag") || SessionState.GetBool(Key, false))
            && File.Exists("Temp/HideOutValidation/request-ui-checks.flag"))
            File.Delete("Temp/HideOutValidation/request-ui-checks.flag"); // Initial refresh only, never run unrelated checks.
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            {
                // A previously queued generic refresh validator must not interrupt this menu run.
                SessionState.SetBool("HideOut.Validation.PlayArmed", false);
                EditorApplication.delayCall -= HideOutPlayChecks.Run;
                return;
            }
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key + ".restore", false)) return;
            RestoreEditorPreferences();
        };
    }

    [MenuItem("Tools/Checks/Save Slot Button Design")]
    public static void Run()
    {
        if (Application.unityVersion != "6000.3.23f1") throw new InvalidOperationException("Requires Unity 6000.3.23f1.");
        Directory.CreateDirectory(Folder);
        var importedSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/!UI/Main/Main_Button.png");
        if (importedSprite != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(importedSprite, out string spriteGuid, out long spriteId))
            File.WriteAllText(Folder + "sprite-import.txt", spriteGuid + " " + spriteId);
        SessionState.SetBool("HideOut.Validation.PlayArmed", false);
        EditorApplication.delayCall -= HideOutVerificationRunner.Run;
        SessionState.SetInt(Key + ".window", EditorWindow.focusedWindow != null ? EditorWindow.focusedWindow.GetInstanceID() : 0);
        SessionState.SetBool(Key + ".wasPaused", EditorApplication.isPaused);
        SessionState.SetBool(Key + ".ownsPlay", !EditorApplication.isPlayingOrWillChangePlaymode);
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            // Preserve unsaved teammate scenes as copies; no original scene is saved or replaced.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty) EditorSceneManager.SaveScene(scene, Folder + "Unsaved-" + i + ".unity", true);
                if (scene.path == "Assets/Scenes/Controls/ControlSchemeSelect.unity")
                {
                    // The already-open editor scene can still cache its pre-change serialized fields.
                    // Update this one reference with Undo, retaining every other in-memory scene edit.
                    foreach (var menu in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ManualSaveMenu>(true)))
                    {
                        var serialized = new SerializedObject(menu);
                        var sprite = serialized.FindProperty("buttonPlateSprite");
                        if (sprite.objectReferenceValue != importedSprite)
                        { sprite.objectReferenceValue = importedSprite; serialized.ApplyModifiedProperties(); }
                        var gear = serialized.FindProperty("buttonGear");
                        gear.objectReferenceValue = AssetDatabase.LoadAssetAtPath<InventoryItemDefinition>("Assets/Scenes/Inventory/Items/CopperGear.asset");
                        serialized.ApplyModifiedProperties();
                    }
                }
            }
            SessionState.SetString(Key + ".startScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Controls/ControlSchemeSelect.unity");
        }
        SessionState.SetBool(Key, true);
        routine = null;
        if (!EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = true;
    }

    private static object Read(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        report.AppendLine("PASS " + message);
        File.WriteAllText(Folder + "progress.txt", report.ToString());
    }
    private static void Observe(string text, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception) { errors++; report.AppendLine("UNITY " + text); }
    }

    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (SessionState.GetBool(Key, false) && !EditorApplication.isPlayingOrWillChangePlaymode)
        { Finish(new InvalidOperationException("Previous validation was interrupted before completion.")); return; }
        if (File.Exists(Folder + "run.flag") && !SessionState.GetBool(Key, false))
        { File.Delete(Folder + "run.flag"); Run(); }
        if (!SessionState.GetBool(Key, false) || !Application.isPlaying) return;
        if (EditorApplication.isPaused)
        {
            SessionState.SetBool(Key + ".wasPaused", true);
            EditorApplication.isPaused = false;
        }
        if (routine == null)
        {
            report.Clear(); errors = 0;
            originalBackground = Application.runInBackground; Application.runInBackground = true;
            EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();
            originalKeyboard = Keyboard.current; originalMouse = Mouse.current; originalGamepad = Gamepad.current;
            originalValidationRoot = (string)typeof(ManualSaveStore).GetField("validationRoot", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>(); gamepad = InputSystem.AddDevice<Gamepad>();
            InputSystem.onBeforeUpdate += FeedMouse;
            ManualSaveStore.UseValidationDirectory(Path.GetFullPath(Folder + "slot-" + Guid.NewGuid().ToString("N")));
            Application.logMessageReceived += Observe;
            deadline = EditorApplication.timeSinceStartup + 150;
            routine = Flatten(Checks());
        }
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Save slot check timed out.");
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            if (routine.MoveNext()) return;
            Check(errors == 0, "No Unity errors/exceptions during these checks.");
            Finish(null);
        }
        catch (Exception exception) { Finish(exception); }
    }

    private static void FeedMouse()
    {
        if (InputState.currentUpdateType != InputUpdateType.Dynamic || mouse == null) return;
        mouse.MakeCurrent(); InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(-1000, -1000) });
    }
    private static void Finish(Exception error)
    {
        if (error != null) report.AppendLine("FAIL " + error);
        report.AppendLine("Unity " + Application.unityVersion + ": " + (error == null ? "PASSED" : "FAILED"));
        File.WriteAllText(Folder + "results.txt", report.ToString());
        Application.logMessageReceived -= Observe;
        InputSystem.onBeforeUpdate -= FeedMouse;
        foreach (var device in new InputDevice[] { keyboard, mouse, gamepad }) if (device != null && device.added) InputSystem.RemoveDevice(device);
        if (originalKeyboard != null && originalKeyboard.added) originalKeyboard.MakeCurrent();
        if (originalMouse != null && originalMouse.added) originalMouse.MakeCurrent();
        if (originalGamepad != null && originalGamepad.added) originalGamepad.MakeCurrent();
        ManualSaveStore.UseValidationDirectory(originalValidationRoot);
        Application.runInBackground = originalBackground;
        SessionState.SetBool(Key, false);
        routine = null;
        if (SessionState.GetBool(Key + ".ownsPlay", false))
        {
            SessionState.SetBool(Key + ".restore", true);
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
            else RestoreEditorPreferences();
        }
        else EditorApplication.isPaused = SessionState.GetBool(Key + ".wasPaused", false);
    }

    private static void RestoreEditorPreferences()
    {
        SessionState.SetBool(Key + ".restore", false);
        string path = SessionState.GetString(Key + ".startScene", "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
        EditorApplication.isPaused = SessionState.GetBool(Key + ".wasPaused", false);
        if (EditorUtility.InstanceIDToObject(SessionState.GetInt(Key + ".window", 0)) is EditorWindow window) window.Focus();
    }

    private static void Keys(params InputKey[] keys) { keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); }
    private static void Pad(params GamepadButton[] buttons)
    { gamepad.MakeCurrent(); InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(buttons.Length > 0 ? buttons[0] : GamepadButton.South, buttons.Length > 0)); }
    private static IEnumerator Frames(int count = 3) { for (int i = 0; i < count; i++) yield return null; }
    private static IEnumerator Flatten(IEnumerator root)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(root);
        while (stack.Count > 0)
        {
            var top = stack.Peek();
            if (!top.MoveNext()) { (top as IDisposable)?.Dispose(); stack.Pop(); continue; }
            if (top.Current is IEnumerator child) { stack.Push(child); continue; }
            yield return top.Current;
        }
    }
    private static IEnumerator Capture(string name)
    {
        bool done = false;
        Object.FindFirstObjectByType<ManualSaveMenu>().StartCoroutine(Grab());
        IEnumerator Grab()
        {
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Folder + name + ".png", texture.EncodeToPNG()); Object.Destroy(texture);
            done = true;
        }
        while (!done) yield return null;
    }

    private static IEnumerator ShowSlotMenu()
    {
        while (Object.FindFirstObjectByType<ManualSaveMenu>() == null || SceneFadeTransition.IsRunning) yield return null;
        var main = Object.FindFirstObjectByType<MainMenuController>();
        if (main != null) main.StartGame();
        while (SceneFadeTransition.IsRunning) yield return null;
        yield return null; yield return null;
    }
    private static IEnumerator ReturnToSlotMenu()
    {
        SaveGameSession.ReturnToMenu();
        while (SceneManager.GetActiveScene().name != "ControlSchemeSelect" || SceneFadeTransition.IsRunning) yield return null;
        yield return ShowSlotMenu();
    }
    private static void Pointer(Button target, bool down, bool click = false)
    {
        var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        ExecuteEvents.Execute(target.gameObject, data, ExecuteEvents.pointerEnterHandler);
        if (down) ExecuteEvents.Execute(target.gameObject, data, ExecuteEvents.pointerDownHandler);
        else ExecuteEvents.Execute(target.gameObject, data, ExecuteEvents.pointerUpHandler);
        if (click) ExecuteEvents.Execute(target.gameObject, data, ExecuteEvents.pointerClickHandler);
    }

    private static string Caption(Button button) => button.GetComponentInChildren<TMP_Text>().text;
    private static void CheckMenuCopy(ManualSaveMenu menu, bool saved)
    {
        Check(((TMP_Text)Read(menu, "status")).text == (saved ? "저장된 게임" : "저장된 게임이 없습니다"),
            "Status copy matches " + (saved ? "saved" : "empty") + " state.");
        Check(((TMP_Text)Read(menu, "guidance")).text == (saved
            ? "침대에서 마지막으로 저장한 지점부터 이어집니다."
            : "게임 진행 상황은 침대에서 저장할 수 있습니다."), "Footer copy matches the current save state.");
        Check(Caption((Button)Read(menu, "newGameButton")) == "새 게임"
            && Caption((Button)Read(menu, "continueButton")) == "이어하기"
            && Caption((Button)Read(menu, "deleteButton")) == "저장 삭제", "Main button captions match the requested copy.");
        CheckVisibleText(menu);
    }
    private static void CheckVisibleText(ManualSaveMenu menu)
    {
        Canvas.ForceUpdateCanvases();
        foreach (var text in menu.GetComponentsInChildren<TMP_Text>())
        {
            text.ForceMeshUpdate();
            Check(!text.isTextOverflowing, "Visible label fits its existing rectangle: " + text.text.Replace('\n', ' '));
        }
    }

    private static IEnumerator Checks()
    {
        Check(SceneManager.GetActiveScene().name == "ControlSchemeSelect", "Testing the authored ControlSchemeSelect scene.");
        Check(ManualSaveStore.SlotPath.StartsWith(Path.GetFullPath(Folder), StringComparison.OrdinalIgnoreCase),
            "All save/continue/delete checks use a project-Temp slot, not the user's real save.");
        // Load the authored disk scene inside Play; preserve the editor's potentially unsaved scene in place.
        SceneManager.LoadScene("ControlSchemeSelect");
        yield return Frames();
        yield return ShowSlotMenu();
        var menu = Object.FindFirstObjectByType<ManualSaveMenu>();
        string hideoutPath = menu.settings.scenePath;
        menu.Refresh(); // The real menu may have built before the isolated slot directory was selected.
        yield return Frames();
        var fresh = (Button)Read(menu, "newGameButton");
        var resume = (Button)Read(menu, "continueButton");
        var delete = (Button)Read(menu, "deleteButton");
        var confirmation = (GameObject)Read(menu, "confirmation");
        CheckMenuCopy(menu, false);
        Check(!ManualSaveStore.Exists && !resume.interactable && !delete.interactable, "Empty slot disables Continue/Delete without dimming the entire label group.");
        Check(fresh.GetComponent<SaveSlotButtonStyle>().IsPrimary && !resume.GetComponent<SaveSlotButtonStyle>().IsPrimary, "Empty slot makes New Game the primary action.");
        Check(EventSystem.current.currentSelectedGameObject == fresh.gameObject, "Keyboard/gamepad focus starts on New Game for an empty slot.");
        Check(new[] { fresh, resume, delete }.All(button => ((RectTransform)button.transform).sizeDelta == new Vector2(420, 72)), "All three plates share dimensions and aligned spacing.");
        var font = fresh.GetComponentInChildren<TMP_Text>().font;
        Check(font == UIFontTheme.Normal && new[] { fresh, resume, delete }.All(b => b.GetComponentInChildren<TMP_Text>().alignment == TextAlignmentOptions.Center), "Original pixel font/material retained, labels centered.");
        Check(fresh.GetComponentInChildren<Image>(true) != null && fresh.GetComponentsInChildren<Image>(true).Any(i => i.sprite != null && i.type == Image.Type.Sliced), "Existing Main_Button sprite is reused as 9-sliced surface grain.");
        Check(fresh.transform.Find("Copper Gear Inlay") != null, "Existing CopperGear inventory icon is reused as a fixed pixel inlay.");
        var graphic = fresh.GetComponentInChildren<SaveSlotPlateGraphic>();
        var graphicMesh = graphic.canvasRenderer.GetMesh();
        Check(graphicMesh != null && graphicMesh.vertexCount > 0, "Metal face, bevels and pins have a rendered Canvas mesh.");
        File.WriteAllText(Folder + "render-diagnostics.txt", "rect=" + graphic.rectTransform.rect + "\ncolor=" + graphic.color
            + "\nrenderer=" + graphic.canvasRenderer.GetColor() + "\nalpha=" + graphic.canvasRenderer.GetInheritedAlpha()
            + "\ntop=" + Read(graphic, "top") + "\nbottom=" + Read(graphic, "bottom")
            + "\nvertices=" + graphicMesh.vertexCount + "\nmesh color=" + (graphicMesh.colors.Length > 0 ? graphicMesh.colors[0].ToString() : "empty")
            + "\nmaterial=" + graphic.material.name + "\ntexture=" + graphic.mainTexture.name);
        yield return Capture("empty-slot");
        var style = fresh.GetComponent<SaveSlotButtonStyle>();
        Pointer(fresh, true);
        Check(style.VisualState == "Pressed", "Mouse pointer-down produces inset pressed metalwork.");
        yield return Capture("mouse-pressed");
        Pointer(fresh, false);
        ExecuteEvents.Execute(fresh.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler);
        var rect = (RectTransform)fresh.transform;
        rect.sizeDelta = new Vector2(560, 84); Canvas.ForceUpdateCanvases();
        yield return Capture("resized-plate");
        rect.sizeDelta = new Vector2(420, 72);
        yield return Frames();

        // Build a valid isolated save through the existing Continue rig, without touching the real slot.
        var rig = Object.Instantiate(menu.continuePlayerPrefab, new Vector3(10000, 10000), Quaternion.identity);
        var inventory = rig.GetComponentInChildren<PlayerInventory>();
        var bridge = inventory.GetComponent<HideOutPlayerBridge>() ?? inventory.gameObject.AddComponent<HideOutPlayerBridge>();
        bridge.InitializeForValidation(menu.settings, ManualSaveStore.SlotPath);
        SaveSnapshotValidation.StampFacilities(bridge.State, menu.settings);
        HideOutPlayerBridge.ValidateSnapshot(bridge.State, menu.settings);
        ManualSaveStore.Write(ManualSaveStore.SlotPath, bridge.State);
        rig.SetActive(false); Object.Destroy(rig);
        yield return Frames();
        menu.Refresh(); EventSystem.current.SetSelectedGameObject(null); menu.SetVisible(true);
        yield return Frames();
        Check(resume.interactable && delete.interactable && resume.GetComponent<SaveSlotButtonStyle>().IsPrimary
            && !fresh.GetComponent<SaveSlotButtonStyle>().IsPrimary, "Saved slot enables Continue/Delete and emphasizes Continue.");
        Check(EventSystem.current.currentSelectedGameObject == resume.gameObject, "Saved slot focuses Continue without a mouse click.");
        CheckMenuCopy(menu, true);
        yield return Capture("saved-slot");

        Keys(InputKey.DownArrow); yield return Frames(); Keys(); yield return Frames();
        Check(EventSystem.current.currentSelectedGameObject == delete.gameObject, "Native keyboard UI navigation selects Delete.");
        Check(delete.GetComponent<SaveSlotButtonStyle>().VisualState == "Focused", "Keyboard selection has a visible border/brightness state.");
        Keys(InputKey.Enter); yield return Frames(); Keys(); yield return Frames();
        Check(confirmation.activeSelf && ManualSaveStore.Exists, "Native keyboard submit opens Delete confirmation without deleting the slot.");
        Check(((TMP_Text)Read(menu, "question")).text == "저장 데이터를 삭제할까요?\n삭제한 진행 상황은 복구할 수 없습니다."
            && Caption((Button)Read(menu, "confirmButton")) == "삭제"
            && Caption((Button)Read(menu, "cancelButton")) == "취소", "Delete dialog title, warning and explicit Delete/Cancel captions match the requested copy.");
        CheckVisibleText(menu);
        yield return Capture("delete-confirmation");
        var cancel = (Button)Read(menu, "cancelButton");
        Check(EventSystem.current.currentSelectedGameObject == cancel.gameObject, "Confirmation defaults to Cancel and isolates navigation.");
        Pad(GamepadButton.South); yield return Frames(); Pad(); yield return Frames();
        Check(!confirmation.activeSelf && ManualSaveStore.Exists, "Native virtual gamepad submit cancels deletion and preserves the saved slot.");
        CheckMenuCopy(menu, true);
        yield return Capture("after-delete-cancel");
        Pad(GamepadButton.DpadUp); yield return Frames(); Pad(); yield return Frames();
        Check(EventSystem.current.currentSelectedGameObject == resume.gameObject, "Native virtual gamepad navigation returns to Continue.");

        Pointer(fresh, false, true); yield return Frames();
        Check(confirmation.activeSelf && ManualSaveStore.Exists, "Mouse New Game preserves the original overwrite confirmation.");
        Check(((TMP_Text)Read(menu, "question")).text == "새 게임을 시작할까요?\n침대에서 처음 저장하면 기존 진행 기록을 덮어씁니다."
            && Caption((Button)Read(menu, "confirmButton")) == "시작하기"
            && Caption((Button)Read(menu, "cancelButton")) == "취소", "New Game title, warning and Start/Cancel captions match the requested copy.");
        CheckVisibleText(menu);
        yield return Capture("new-game-confirmation");
        Pointer(cancel, false, true); yield return Frames();
        Check(!confirmation.activeSelf && ManualSaveStore.Exists, "Mouse Cancel preserves existing saved data.");
        EventSystem.current.SetSelectedGameObject(resume.gameObject);
        Keys(InputKey.Enter); yield return Frames(); Keys();
        while (SceneManager.GetActiveScene().path != hideoutPath || SceneFadeTransition.IsRunning) yield return null;
        Check(Object.FindFirstObjectByType<HideOutScene>() != null, "Native keyboard Continue executes the original load and enters HideOut.");
        yield return ReturnToSlotMenu();
        menu = Object.FindFirstObjectByType<ManualSaveMenu>();
        delete = (Button)Read(menu, "deleteButton");
        Pointer(delete, false, true); yield return Frames();
        Pointer((Button)Read(menu, "confirmButton"), false, true); yield return Frames();
        Check(!ManualSaveStore.Exists && !((Button)Read(menu, "continueButton")).interactable,
            "Mouse Delete/Confirm deletes only the isolated test slot and refreshes disabled states.");
        CheckMenuCopy(menu, false);
        fresh = (Button)Read(menu, "newGameButton");
        EventSystem.current.SetSelectedGameObject(fresh.gameObject);
        Pad(GamepadButton.South); yield return Frames(); Pad();
        while (SceneManager.GetActiveScene().name != "Forest" || SceneFadeTransition.IsRunning) yield return null;
        Check(Object.FindFirstObjectByType<PlayerMovement2D>() != null, "Native virtual gamepad New Game executes the original Forest startup.");
        yield return ReturnToSlotMenu();
        yield return Capture("final-empty-slot");
    }
}
