using UnityEngine;

namespace BehindTheGear.HideOut
{
    // Authored placement aid only, with no inventory, physics or damage receiver.
    public sealed class ScareCrowPlacementPreview : MonoBehaviour
    {
        private void Awake() => gameObject.SetActive(false);
    }
}
