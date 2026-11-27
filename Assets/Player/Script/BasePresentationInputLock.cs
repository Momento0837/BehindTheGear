using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Keeps existing base controls locked until every overlapping presentation finishes.</summary>
public sealed class BasePresentationInputLock : IDisposable
{
    private sealed class State { public int count; public bool wasEnabled; }
    private static readonly Dictionary<GraphicRaycaster, State> states = new();
    private readonly List<GraphicRaycaster> held = new();
    private bool disposed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => states.Clear();

    public static BasePresentationInputLock Acquire(Transform root)
    {
        var lease = new BasePresentationInputLock();
        if (root == null) return lease;
        foreach (var raycaster in root.GetComponentsInChildren<GraphicRaycaster>(true))
        {
            if (!states.TryGetValue(raycaster, out var state))
            {
                // Preserve controls that were intentionally disabled before any presentation.
                if (!raycaster.enabled) continue;
                state = new State { wasEnabled = raycaster.enabled };
                states.Add(raycaster, state);
            }
            state.count++;
            raycaster.enabled = false;
            lease.held.Add(raycaster);
        }
        return lease;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var raycaster in held)
        {
            if (!states.TryGetValue(raycaster, out var state) || --state.count > 0) continue;
            if (raycaster != null) raycaster.enabled = state.wasEnabled;
            states.Remove(raycaster);
        }
        held.Clear();
    }
}
