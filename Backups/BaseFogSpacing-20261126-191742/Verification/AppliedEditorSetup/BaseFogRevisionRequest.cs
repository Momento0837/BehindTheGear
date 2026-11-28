using System.IO;
using BehindTheGear.HideOut;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class BaseFogRevisionRequest
{
    static BaseFogRevisionRequest() => EditorApplication.update += Poll;
    private static void Poll()
    {
        const string request = "Temp/BaseFogRevision/check.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating
            || !EditorApplication.isPlaying || Application.unityVersion != "6000.3.23f1") return;
        if (Object.FindFirstObjectByType<HideOutScene>()?.TrainingPlayer?.Initialized != true) return;
        File.Delete(request);
        BaseStoryUnlockChecks.RunChecks();
    }
}
