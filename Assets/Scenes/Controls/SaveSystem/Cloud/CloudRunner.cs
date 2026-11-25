using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Hidden coroutine host for the static cloud services. Scene travel may destroy it
/// (SaveGameSession disposes DontDestroyOnLoad roots); every in-flight operation then
/// reports "interrupted" and the next menu sync repairs the state from the hashes.
/// </summary>
internal sealed class CloudRunner : MonoBehaviour
{
    private static CloudRunner instance;
    public static event Action Lost;

    public static Coroutine Run(IEnumerator routine)
    {
        if (instance == null)
        {
            var go = new GameObject("Cloud Save Runner");
            go.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
            instance = go.AddComponent<CloudRunner>();
        }
        return instance.StartCoroutine(routine);
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        instance = null;
        Lost?.Invoke();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null; // Subscribers re-register idempotently (-= then +=).
}
