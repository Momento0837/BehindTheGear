using TMPro;
using UnityEngine;

public sealed class RecoveryPotionPenalty : MonoBehaviour
{
    private double expires;
    private TMP_Text label;
    private GameObject root;
    public bool Active => Time.realtimeSinceStartupAsDouble < expires;
    public float Multiplier => Active ? .8f : 1;
    public void Apply() { expires = Time.realtimeSinceStartupAsDouble + 90; enabled = true; }
    void Update()
    {
        if (!Active) { if(root!=null)root.SetActive(false); return; }
        if (root == null)
        {
            root = new GameObject("Recovery Potion Timer",typeof(Canvas)); root.transform.SetParent(transform,false);
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=100;
            label=new GameObject("Remaining mm:ss",typeof(RectTransform),typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();label.transform.SetParent(root.transform,false);
            var rect=label.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(1,.5f);rect.pivot=new Vector2(1,.5f);rect.anchoredPosition=new Vector2(-28,0);rect.sizeDelta=new Vector2(220,60);
            UIFontTheme.Apply(label,UIFontTheme.Normal);label.fontSize=24;label.alignment=TextAlignmentOptions.Right;label.raycastTarget=false;
        }
        root.SetActive(true);int remaining=Mathf.CeilToInt((float)(expires-Time.realtimeSinceStartupAsDouble));label.text=$"이동속도 80%\n{remaining/60:00}:{remaining%60:00}";
    }
    void OnDestroy(){if(root!=null)Destroy(root);}
}
