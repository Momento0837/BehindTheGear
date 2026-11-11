using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Entry menu and direct new-game transition; controls are fixed to WASD/mouse.</summary>
public sealed class ControlSchemeSelectScene : MonoBehaviour
{
    [SerializeField] private string gameSceneName = "Forest";
    [SerializeField, Min(.01f)] private float fadeDuration = .8f;
    [SerializeField] private MainMenuController mainMenuPrefab;
    [SerializeField] private GameObject controlSelectionCanvas;
    private MainMenuController mainMenu;
    private bool showingMainMenu;
    private bool selecting;

    private void Awake()
    {
        if (controlSelectionCanvas != null)
        {
            controlSelectionCanvas.SetActive(false);
            Destroy(controlSelectionCanvas);
        }
        if (mainMenuPrefab == null)
        {
            Debug.LogError("[Startup] Assign the main menu prefab.", this);
            return;
        }

        GetComponent<ManualSaveMenu>()?.SetVisible(false);
        showingMainMenu = true;
        mainMenu = Instantiate(mainMenuPrefab);
        mainMenu.StartRequested += ShowControls;
    }

    private void ShowControls() => StartCoroutine(ShowControlsRoutine());

    private IEnumerator ShowControlsRoutine()
    {
        yield return SceneFadeTransition.FadeThrough(() =>
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            mainMenu.gameObject.SetActive(false);
            var saveMenu = GetComponent<ManualSaveMenu>();
            if (saveMenu != null) saveMenu.SetVisible(true);
        }, fadeDuration);
        showingMainMenu = false;
        Destroy(mainMenu.gameObject);
        if (GetComponent<ManualSaveMenu>() == null) StartNewGame();
    }

    public void ShowControlsForNewGame() => StartNewGame();

    public void SelectWasdMouseQe() => StartNewGame();

    // Old editor automation entry point; it cannot select an alternate layout.
    public void SelectArrowKeys() => StartNewGame();

    public void StartNewGame()
    {
        if (showingMainMenu || selecting || SceneFadeTransition.IsRunning) return;
        var menu = GetComponent<ManualSaveMenu>();
        if (menu != null && !menu.ChoosingControls) return;
        selecting = true;
        SceneFadeTransition.LoadScene(gameSceneName, fadeDuration, success =>
        {
            if (!success) selecting = false;
        });
    }
}
