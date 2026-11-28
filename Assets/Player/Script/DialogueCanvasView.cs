using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Serialized bindings survive renaming and rearranging the editable Canvas hierarchy.
public sealed class DialogueCanvasView : MonoBehaviour
{
    public CanvasGroup group;
    public Image portrait;
    public TMP_Text speaker;
    public TMP_Text body;
    public Image talking;
    public GameObject continueIndicator;
    public Shader continueGlowShader;
    [Min(0)] public float continueGlowRadius = 15f;
    [Range(0, 1)] public float continueGlowOpacity = .3f;
    [Min(0)] public float continueBobHeight = 5f;
    [Min(0)] public float continueBobSpeed = 3f;
    public GameObject[] visibleObjects = Array.Empty<GameObject>();
    public DialogueChoiceView choiceTemplate;
}
