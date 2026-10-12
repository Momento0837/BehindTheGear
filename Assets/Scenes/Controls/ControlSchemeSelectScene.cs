using UnityEngine;

/// <summary>Handles the two buttons saved in the control selection scene.</summary>
public sealed class ControlSchemeSelectScene : MonoBehaviour
{
    [SerializeField] private string gameSceneName = "PlayerMovementDemo";
    [SerializeField, Min(.01f)] private float fadeDuration = .8f;

    public void SelectWasdMouseQe() => Select(PlayerControlScheme.WasdMouseQe);

    public void SelectArrowKeys() => Select(PlayerControlScheme.ArrowKeys);

    private void Select(PlayerControlScheme scheme)
    {
        PlayerControlSchemeSettings.Select(scheme);
        SceneFadeTransition.LoadScene(gameSceneName, fadeDuration);
    }
}
