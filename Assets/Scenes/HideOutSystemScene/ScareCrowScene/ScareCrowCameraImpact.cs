using UnityEngine;

namespace BehindTheGear.HideOut
{
    // Only attached to the training camera; offset is removed before the existing follow update.
    [DefaultExecutionOrder(1100)]
    public sealed class ScareCrowCameraImpact : MonoBehaviour
    {
        public bool enableShake;
        [Range(0f, .1f)] public float strength = .025f;
        [Min(.01f)] public float duration = .1f;
        private Vector3 appliedOffset;
        private float remaining;
        public void Play() { if (enableShake) remaining = duration; }
        private void Update() { transform.position -= appliedOffset; appliedOffset = Vector3.zero; }
        private void LateUpdate()
        {
            if (!enableShake || remaining <= 0) return;
            remaining = Mathf.Max(0, remaining - Time.unscaledDeltaTime);
            float fade = remaining / Mathf.Max(.01f, duration);
            appliedOffset = new Vector3(Mathf.Sin(remaining * 180), Mathf.Cos(remaining * 230), 0) * (strength * fade);
            transform.position += appliedOffset;
        }
        private void OnDisable() { transform.position -= appliedOffset; appliedOffset = Vector3.zero; remaining = 0; }
    }
}
