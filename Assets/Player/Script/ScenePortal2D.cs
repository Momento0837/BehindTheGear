using UnityEngine;

/// <summary>Loads a destination scene when the player presses F on this portal.</summary>
[RequireComponent(typeof(Interactable2D))]
public sealed class ScenePortal2D : MonoBehaviour
{
    [SerializeField] private string destinationSceneName = "Main";
    [SerializeField] private string destinationSpawnPointId = "FromDemoRightPortal";

    public void Travel(GameObject interactor)
    {
        if (interactor == null) return;
        PersistentPlayer2D player = interactor.GetComponent<PersistentPlayer2D>();
        if (player == null)
        {
            Debug.LogError("[Portal] The interacting player needs PersistentPlayer2D.", this);
            return;
        }

        player.TravelTo(destinationSceneName, destinationSpawnPointId);
    }
}
