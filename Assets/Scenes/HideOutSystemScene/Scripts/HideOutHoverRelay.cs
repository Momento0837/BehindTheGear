using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace BehindTheGear.HideOut
{
    /// <summary>
    /// Hover-only pointer relay for hideout tooltips.
    /// Unlike EventTrigger (which implements every pointer interface and therefore swallows
    /// drag/scroll events), this only handles enter/exit, so drags and wheel input on a
    /// shortcut card still bubble up to the parent ScrollRect.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HideOutHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private UnityAction enter;
        private UnityAction exit;

        public void Configure(UnityAction onEnter, UnityAction onExit)
        {
            enter = onEnter;
            exit = onExit;
        }

        public void OnPointerEnter(PointerEventData eventData) => enter?.Invoke();
        public void OnPointerExit(PointerEventData eventData) => exit?.Invoke();
    }
}
