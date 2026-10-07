using System;
using UnityEngine;

public sealed class PlayerTargetRegistry : MonoBehaviour
{
    public static Transform Current { get; private set; }
    public static event Action<Transform> TargetChanged;

    private void OnEnable()
    {
        Current = transform;
        TargetChanged?.Invoke(Current);
    }

    private void OnDisable()
    {
        if (Current != transform) return;
        Current = null;
        TargetChanged?.Invoke(null);
    }
}
