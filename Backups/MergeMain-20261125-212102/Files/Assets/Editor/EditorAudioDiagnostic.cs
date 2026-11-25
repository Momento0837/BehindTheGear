#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
[InitializeOnLoad]
public static class EditorAudioDiagnostic
{
    static EditorAudioDiagnostic() { EditorApplication.update += Check; }
    static void Check()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists("Temp/CheckEditorAudio")) return;
        File.Delete("Temp/CheckEditorAudio");
        var report = new StringBuilder();
        report.AppendLine("Unity=" + Application.unityVersion + "; playing=" + EditorApplication.isPlaying);
        foreach (var type in typeof(Editor).Assembly.GetTypes())
        {
            foreach(var property in type.GetProperties(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static))
                if(property.Name.Equals("audioMasterMute",StringComparison.OrdinalIgnoreCase))
                    try { report.AppendLine(type.FullName+"."+property.Name+"="+property.GetValue(null)); } catch(Exception e) {report.AppendLine(e.Message);}
        }
        report.AppendLine("Listener volume="+AudioListener.volume+"; pause="+AudioListener.pause);
        foreach(var listener in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            report.AppendLine("Listener: "+listener.name+" active="+listener.isActiveAndEnabled);
        foreach(var source in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            report.AppendLine("Source: "+source.name+" active="+source.isActiveAndEnabled+" playing="+source.isPlaying+" mute="+source.mute+" volume="+source.volume+" clip="+(source.clip!=null?source.clip.name:"NULL"));
        File.WriteAllText("Temp/EditorAudioDiagnostic.txt",report.ToString());
    }
}
#endif
