using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Screen-space cursor that follows the actual pointer without intercepting UI clicks.</summary>
[DefaultExecutionOrder(2000)]
public sealed class GameMouseCursor : MonoBehaviour
{
    private Image icon;
    private bool previousVisibility;
    private Texture2D hiddenCursor;
    private bool hidingSystemCursor;
    private PlayerMovement2D player;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (FindFirstObjectByType<GameMouseCursor>() != null) return;
        var style = Resources.Load<GameCursorStyle>("UI/GameCursorStyle");
        if (style == null || style.sprite == null) { Debug.LogWarning("[Cursor] Missing Curser sprite reference."); return; }
        var root = new GameObject("Game Mouse Cursor", typeof(Canvas), typeof(GameMouseCursor));
        DontDestroyOnLoad(root);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        var cursor = root.GetComponent<GameMouseCursor>();
        cursor.previousVisibility = Cursor.visible;
        var visual = new GameObject("Curser", typeof(RectTransform), typeof(Image));
        visual.transform.SetParent(root.transform, false);
        cursor.icon = visual.GetComponent<Image>();
        cursor.icon.sprite = style.sprite;
        cursor.icon.preserveAspect = true;
        cursor.icon.raycastTarget = false;
        cursor.icon.rectTransform.sizeDelta = style.size;
        cursor.icon.enabled = false;
    }
    private void LateUpdate()
    {
        if (icon == null) return;
        // Speech and movies hide both pointers; choice prompts use the native Windows cursor.
        if (player == null || !player.gameObject.activeInHierarchy)
            player = FindFirstObjectByType<PlayerMovement2D>();
        bool hasPlayer = player != null && player.gameObject.activeInHierarchy;
        bool inventoryOpen = hasPlayer && player.GetComponent<InventoryUI>() != null && player.GetComponent<InventoryUI>().IsOpen;
        Vector2 pointer = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        bool pointerInGame = Application.isFocused && Mouse.current != null && pointer.x >= 0f && pointer.y >= 0f
            && pointer.x < Screen.width && pointer.y < Screen.height;
#if UNITY_EDITOR
        // Input System can retain the last in-game position after leaving the Game view.
        var hoveredWindow = UnityEditor.EditorWindow.mouseOverWindow;
        pointerInGame &= hoveredWindow != null && hoveredWindow.GetType().Name == "GameView";
#endif
        bool cutsceneVisible = CutsceneVideoSequence.IsAnyVisible;
        bool dialogueVisible = DialoguePresentation.IsActive || cutsceneVisible;
        bool choicesVisible = !cutsceneVisible && DialoguePresentation.HasChoices;
        bool visible = hasPlayer && !inventoryOpen && !dialogueVisible && pointerInGame;
        icon.enabled = visible;
        SetSystemCursorHidden(pointerInGame && !choicesVisible && (visible || dialogueVisible));
        if (choicesVisible || (inventoryOpen && !dialogueVisible)) Cursor.visible = true;
        if (visible) icon.rectTransform.position = pointer;
    }
    private void SetSystemCursorHidden(bool hidden)
    {
        if (hidden != hidingSystemCursor)
        {
            if (hidden && hiddenCursor == null)
            {
                hiddenCursor = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                hiddenCursor.SetPixel(0, 0, Color.clear);
                hiddenCursor.Apply();
            }
            // A transparent native cursor also prevents a second pointer when the
            // editor restores cursor visibility while hovering over the Game view.
            Cursor.SetCursor(hidden ? hiddenCursor : null, Vector2.zero, CursorMode.Auto);
            hidingSystemCursor = hidden;
        }
        Cursor.visible = hidden ? false : previousVisibility;
    }
    private void OnApplicationFocus(bool focused)
    {
        if (!focused) { if (icon != null) icon.enabled = false; SetSystemCursorHidden(false); }
    }
    private void OnDisable() { if (icon != null) icon.enabled = false; SetSystemCursorHidden(false); }
    private void OnDestroy()
    {
        SetSystemCursorHidden(false);
        if (hiddenCursor != null) Destroy(hiddenCursor);
    }
}
