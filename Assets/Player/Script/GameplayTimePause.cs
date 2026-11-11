using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Shares a pause across overlapping scene fades and cutscenes.</summary>
public static class GameplayTimePause
{
    private static readonly HashSet<Lease> leases = new();
    private static float resumeScale = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        leases.Clear();
        resumeScale = 1f;
    }

    public static IDisposable Acquire()
    {
        if (leases.Count == 0) resumeScale = Time.timeScale;
        var lease = new Lease();
        leases.Add(lease);
        Time.timeScale = 0f;
        return lease;
    }

    private sealed class Lease : IDisposable
    {
        public void Dispose()
        {
            if (!leases.Remove(this)) return;
            Time.timeScale = leases.Count == 0 ? resumeScale : 0f;
        }
    }
}
