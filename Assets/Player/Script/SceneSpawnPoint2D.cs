using UnityEngine;

/// <summary>Editable arrival marker used by scene portals.</summary>
public sealed class SceneSpawnPoint2D : MonoBehaviour
{
    [SerializeField] private string spawnPointId = "Entry";
    public string SpawnPointId => spawnPointId;

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(.25f, .85f, 1f, .9f);
        Gizmos.DrawWireCube(transform.position, new Vector3(.7f, 1.8f, .1f));
        Gizmos.DrawLine(transform.position + Vector3.left * .5f, transform.position + Vector3.right * .5f);
        Gizmos.DrawLine(transform.position + Vector3.down * .5f, transform.position + Vector3.up * .5f);
    }
}
