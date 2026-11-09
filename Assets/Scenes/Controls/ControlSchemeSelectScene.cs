using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Handles the two buttons saved in the control selection scene.</summary>
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
        if (mainMenuPrefab == null || controlSelectionCanvas == null)
        {
            Debug.LogError("[Startup] Assign the main menu prefab and control selection canvas.", this);
            return;
        }

        showingMainMenu = true;
        controlSelectionCanvas.SetActive(false);
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
            controlSelectionCanvas.SetActive(true);
        }, fadeDuration);
        showingMainMenu = false;
        Destroy(mainMenu.gameObject);
    }

    public void SelectWasdMouseQe() => Select(PlayerControlScheme.WasdMouseQe);

    public void SelectArrowKeys() => Select(PlayerControlScheme.ArrowKeys);

    private void Select(PlayerControlScheme scheme)
    {
        if (showingMainMenu || selecting || SceneFadeTransition.IsRunning) return;
        selecting = true;
        PlayerControlSchemeSettings.Select(scheme);
        SceneFadeTransition.LoadScene(gameSceneName, fadeDuration, success =>
        {
            if (!success) selecting = false;
        });
    }
}
