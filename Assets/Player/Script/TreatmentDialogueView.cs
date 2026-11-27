using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Editable UI references used by the treatment room's existing dialogue playback.</summary>
public sealed class TreatmentDialogueView : MonoBehaviour
{
    public CanvasGroup group;
    public TMP_Text speaker;
    public TMP_Text body;
    public Image continueMark;
    public Button advance;

    public bool IsValid => group != null && speaker != null && body != null
        && continueMark != null && advance != null;
}