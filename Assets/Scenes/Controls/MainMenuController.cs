using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Connects the authored main menu buttons to the startup flow.</summary>
public sealed class MainMenuController : MonoBehaviour
{
    [SerializeField] private Button startButton;
    [SerializeField] private Button quitButton;
    private bool starting;

    public event Action StartRequested;

    private void Awake()
    {
        startButton.onClick.AddListener(StartGame);
        quitButton.onClick.AddListener(QuitGame);
    }

    public void StartGame()
    {
        if (starting || StartRequested == null) return;
        starting = true;
        startButton.interactable = false;
        quitButton.interactable = false;
        StartRequested.Invoke();
    }

    public void QuitGame()
    {
        if (starting) return;
#if !UNITY_EDITOR
        Application.Quit();
#endif
    }

    private void OnDestroy()
    {
        startButton.onClick.RemoveListener(StartGame);
        quitButton.onClick.RemoveListener(QuitGame);
    }
}
