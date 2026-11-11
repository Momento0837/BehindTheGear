using UnityEngine;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Light2D))]
public sealed class InteractionLightPulse : MonoBehaviour
{
    private Light2D targetLight;
    private float elapsed;
    private const float MinimumIntensity = 1f;
    private const float MaximumIntensity = 7f;
    private Transform nearbyPlayer;
    private Collider2D interactionArea;

    public void BindProximity(Transform player, Collider2D area)
    {
        nearbyPlayer = player;
        interactionArea = area;
    }
    private void OnEnable()
    {
        targetLight = GetComponent<Light2D>();
        elapsed = 0f;
        targetLight.intensity = MinimumIntensity;
    }
    private void Update()
    {
        if (nearbyPlayer != null && nearbyPlayer.gameObject.activeInHierarchy
            && interactionArea != null && interactionArea.enabled
            && Vector2.Distance(nearbyPlayer.position, interactionArea.ClosestPoint(nearbyPlayer.position)) <= 2.5f)
        {
            // Resume the pulse from its peak when the player walks away.
            elapsed = 2f;
            targetLight.intensity = MaximumIntensity;
            return;
        }
        elapsed += Time.deltaTime;
        // Two seconds from minimum to maximum, then two seconds back.
        targetLight.intensity = Mathf.Lerp(MinimumIntensity, MaximumIntensity, .5f - .5f * Mathf.Cos(elapsed * Mathf.PI / 2f));
    }
}
