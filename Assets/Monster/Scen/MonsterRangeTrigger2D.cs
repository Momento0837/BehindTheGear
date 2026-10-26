using System.Collections.Generic;
using UnityEngine;

/// <summary>Each sensor owns one collider so recognition and attack trigger events stay distinct.</summary>
public sealed class MonsterRangeTrigger2D : MonoBehaviour
{
    private MonsterFollowController owner;
    private readonly HashSet<Collider2D> contacts = new();
    public bool HasPlayer
    {
        get
        {
            contacts.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);
            return contacts.Count > 0;
        }
    }

    public void Configure(MonsterFollowController value, bool attack)
    {
        owner = value;
        contacts.Clear();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var health = other.GetComponentInParent<PlayerHealth>();
        if (health == null || owner == null) return;
        contacts.Add(other);
        owner.RecognizePlayer(health);
    }

    private void OnTriggerExit2D(Collider2D other) => contacts.Remove(other);
    private void OnDisable() => contacts.Clear();
}
