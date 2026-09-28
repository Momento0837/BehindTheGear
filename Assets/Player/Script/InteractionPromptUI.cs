using TMPro;
using UnityEngine;

public sealed class InteractionPromptUI : MonoBehaviour
{
    [SerializeField] private TMP_Text promptText;
    [SerializeField, Min(0f)] private float heightOffset = 0.35f;

    public void Configure(TMP_Text text)
    {
        promptText = text;
        promptText.text = "[F] Interact";
        SetVisible(false);
    }

    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
    }

    public void SetTarget(Interactable2D target)
    {
        if (target == null) return;

        Collider2D targetCollider = target.GetComponent<Collider2D>();
        Vector3 position = target.transform.position;
        if (targetCollider != null)
        {
            Bounds bounds = targetCollider.bounds;
            position = new Vector3(bounds.center.x, bounds.max.y + heightOffset, position.z);
        }
        else position += Vector3.up * heightOffset;

        transform.position = position;
    }
}
