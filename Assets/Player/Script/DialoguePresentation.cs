using System.Collections.Generic;
using UnityEngine;

/// <summary>Tracks active speech UI independently of player movement locks.</summary>
public static class DialoguePresentation
{
    private static readonly HashSet<Object> owners = new();
    public static bool IsActive
    {
        get { owners.RemoveWhere(owner => owner == null); return owners.Count > 0; }
    }
    public static bool HasChoices
    {
        get
        {
            foreach (var owner in owners)
                if (owner != null && owner is ConversationDialogue dialogue && dialogue.IsChoosing)
                    return true;
            return false;
        }
    }
    public static void Set(Object owner, bool active)
    {
        if (active) owners.Add(owner); else owners.Remove(owner);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => owners.Clear();
}
