#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Keeps build references in sync without moving or reimporting the source sounds.</summary>
[InitializeOnLoad]
public sealed class GameSoundLibraryBuilder : AssetPostprocessor
{
    private const string Root = "Assets/!Sounds/";
    private const string AssetPath = "Assets/Resources/Audio/GameSoundLibrary.asset";
    private static bool pending = true;
    public static bool IsPending => pending;
    static GameSoundLibraryBuilder() => EditorApplication.update += RebuildWhenReady;

    private static void RebuildWhenReady()
    {
        if (!pending || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        pending = false;
        Rebuild();
    }

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(p =>
            p == "Assets/!Music/Base_BGM.wav" || p == "Assets/!Music/Market_BGM.wav" || (p.StartsWith(Root, StringComparison.Ordinal)
            && p.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))))
        {
            pending = true;
        }
    }

    [MenuItem("Tools/Behind The Gear/Refresh Sound Library")]
    public static void Rebuild()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        { pending = true; return; }
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Audio"))
            AssetDatabase.CreateFolder("Assets/Resources", "Audio");
        var library = AssetDatabase.LoadAssetAtPath<GameSoundLibrary>(AssetPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<GameSoundLibrary>();
            AssetDatabase.CreateAsset(library, AssetPath);
        }
        var asset = new SerializedObject(library);
        Set(asset, "forestAmbience", Clips("Ambient_Map/Forest_Loop"));
        Set(asset, "garbageAmbience", Clips("Ambient_Map/Garbage_Loop"));
        Set(asset, "marketHouseAmbience", Clips("Ambient_Map/Market_House_Loop"));
        var dirt = Clips("Walking/Footsteps/Walking_Dirt");
        var concrete = Clips("Walking/Footsteps/Walking_Concrete");
        Set(asset, "dirtSteps", dirt.Where(c => !IsLoose(c)).ToArray());
        Set(asset, "dirtLooseSteps", dirt.Where(IsLoose).ToArray());
        Set(asset, "concreteSteps", concrete.Where(c => !IsLoose(c) && !IsSlide(c)).ToArray());
        Set(asset, "concreteLooseSteps", concrete.Where(c => IsLoose(c) || IsSlide(c)).ToArray());
        Set(asset, "concreteLandings", Clips("Walking/Footsteps/Landing_Concrete"));
        Set(asset, "concreteDeathLandings", Clips("Walking/Footsteps/Landing_Concrete")
            .Where(c => c.name.IndexOf("concrete", StringComparison.OrdinalIgnoreCase) >= 0).ToArray());
        Set(asset, "punchHits", Clips("Punches"));
        SetClip(asset, "mainButton", "Main_Button.wav");
        SetClip(asset, "inventoryOpen", "Inventory_01.wav");
        SetClip(asset, "inventorySlide", "Inventory_02.wav");
        SetClip(asset, "reservedMetallicButton", "metallic_button_02.wav");
        asset.FindProperty("baseBgm").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/!Music/Base_BGM.wav");
        asset.FindProperty("marketBgm").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/!Music/Market_BGM.wav");
        if (asset.ApplyModifiedPropertiesWithoutUndo()) AssetDatabase.SaveAssetIfDirty(library);
    }

    private static AudioClip[] Clips(string folder)
    {
        string path = Root + folder;
        if (!AssetDatabase.IsValidFolder(path)) return Array.Empty<AudioClip>();
        return AssetDatabase.FindAssets("t:AudioClip", new[] { path }).Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal)
            .Select(AssetDatabase.LoadAssetAtPath<AudioClip>).Where(c => c != null).ToArray();
    }
    private static bool IsLoose(AudioClip clip) => clip.name.IndexOf("loose", StringComparison.OrdinalIgnoreCase) >= 0;
    private static bool IsSlide(AudioClip clip) => clip.name.IndexOf("slide", StringComparison.OrdinalIgnoreCase) >= 0;
    private static void Set(SerializedObject asset, string name, AudioClip[] clips)
    {
        var property = asset.FindProperty(name);
        property.arraySize = clips.Length;
        for (int i = 0; i < clips.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
    }
    private static void SetClip(SerializedObject asset, string name, string file)
        => asset.FindProperty(name).objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Buttons/" + file);
}
#endif
