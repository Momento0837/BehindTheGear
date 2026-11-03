using UnityEngine;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Light2D))]
public sealed class InteractionLightPulse : MonoBehaviour
{
    private Light2D targetLight;
    private float elapsed;
    private void OnEnable()
    {
        targetLight = GetComponent<Light2D>();
        elapsed = 0f;
        targetLight.intensity = 1f;
    }
    private void Update()
    {
        elapsed += Time.deltaTime;
        // Two seconds from minimum to maximum, then two seconds back.
        targetLight.intensity = Mathf.Lerp(1f, 4f, .5f - .5f * Mathf.Cos(elapsed * Mathf.PI / 2f));
    }
}
