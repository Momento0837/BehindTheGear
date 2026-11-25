using UnityEngine;

public static class WindowsWindowedStartup
{
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void StartWindowed()
    {
        Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
        var host = new GameObject("Window Aspect Ratio");
        Object.DontDestroyOnLoad(host);
        host.AddComponent<WindowsWindowAspect>();
    }
#endif
}
