#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Opt-in, read-only asset/selection checks. Does not enter Play mode or modify scenes/saves.</summary>
[InitializeOnLoad]
public static class GameSoundChecks
{
    private const string Folder = "Logs/GameSoundChecks";
    private const string Root = "Assets/!Sounds/";
    private static readonly StringBuilder Report = new();
    static GameSoundChecks() => EditorApplication.update += Poll;

    private static void Poll()
    {
        string request = Folder + "/request.txt";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating
            || GameSoundLibraryBuilder.IsPending
            || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        string command = File.ReadAllText(request).Trim();
        File.Delete(request);
        Report.Clear();
        try
        {
            Require(command == "run", "explicit run request");
            Verify();
            File.WriteAllText(Folder + "/result.txt", "PASS Unity " + Application.unityVersion + "\n" + Report);
        }
        catch (Exception error)
        {
            File.WriteAllText(Folder + "/result.txt", "FAIL Unity " + Application.unityVersion + "\n" + Report + error);
            Debug.LogError("[GameSoundChecks] " + error.Message);
        }
    }

    private static void Verify()
    {
        var library = Resources.Load<GameSoundLibrary>("Audio/GameSoundLibrary");
        Require(library != null, "build-loadable sound library exists");
        var baseMusic = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/!Music/Base_BGM.wav");
        Require(baseMusic != null && library.baseBgm == baseMusic, "authored Base_BGM is included in the build sound library");
        Require(library.baseMusicFadeDuration > 0 && library.ambienceCrossfadeDuration > 0,
            "base fade and ambient crossfade durations are positive");
        Require(new[] { "Base_Test", "HideOutSystemScene", "TreatmentScene", "ScareCrowScene" }.All(GameSoundService.IsBaseScene)
            && !new[] { "Forest", "Market", "ControlSchemeSelect" }.Any(GameSoundService.IsBaseScene),
            "base and its treatment/training rooms share one BGM; field/menu scenes do not");
        Group(library.forestAmbience, Clips("Ambient_Map/Forest_Loop"), "Forest simultaneous layers");
        Group(library.garbageAmbience, Clips("Ambient_Map/Garbage_Loop"), "Garbage layers");
        Group(library.marketHouseAmbience, Clips("Ambient_Map/Market_House_Loop"), "Market/neighborhood layers");
        var dirt = Clips("Walking/Footsteps/Walking_Dirt");
        var concrete = Clips("Walking/Footsteps/Walking_Concrete");
        Group(library.dirtSteps, dirt.Where(c => Step(c) && !Loose(c)).ToArray(), "dirt regular steps");
        Group(library.dirtLooseSteps, dirt.Where(Loose).ToArray(), "dirt loose steps");
        Group(library.concreteSteps, concrete.Where(c => Step(c) && !Loose(c)).ToArray(), "concrete regular steps");
        Group(library.concreteLooseSteps, concrete.Where(Loose).ToArray(), "concrete loose steps", true);
        Group(library.concreteLandings, Clips("Walking/Footsteps/Landing_Concrete"), "all landing-folder clips");
        Require(!library.dirtSteps.Intersect(library.dirtLooseSteps).Any(), "loose walk_run clips belong to one category only");
        Require(library.mainButton == Clip("Buttons/Main_Button.wav")
            && library.inventoryOpen == Clip("Buttons/Inventory_01.wav")
            && library.inventorySlide == Clip("Buttons/Inventory_02.wav")
            && library.reservedMetallicButton == Clip("Buttons/metallic_button_02.wav"), "four authored UI clip references");
        var savedRandom = UnityEngine.Random.state;
        try
        {
            UnityEngine.Random.InitState(7219);
            int loose = 0;
            var allowed = new HashSet<AudioClip>(library.dirtSteps.Concat(library.dirtLooseSteps));
            for (int i = 0; i < 10000; i++)
            {
                var clip = library.Footstep(GameSoundMap.Forest);
                if (!allowed.Contains(clip)) throw new Exception("Forest selected an unrelated or null clip.");
                if (Loose(clip)) loose++;
            }
            Require(loose >= 2800 && loose <= 3200, "10000 draws preserve 70/30 group weights: loose=" + loose);
            for (int i = 0; i < 1000; i++)
                if (!library.concreteSteps.Contains(library.Footstep(GameSoundMap.Garbage)))
                    throw new Exception("Concrete fallback selected null/unrelated audio despite available walk/run clips.");
            Require(library.concreteLooseSteps.Length == 0, "concrete empty-loose fallback exercised for 1000 draws");
            Require(library.Footstep(GameSoundMap.None) == null, "no footsteps outside gameplay maps");
        }
        finally { UnityEngine.Random.state = savedRandom; }
        const string forest = "Assets/Scenes/Main/Forest.unity", market = "Assets/Scenes/Main/Market.unity";
        Require(GameSoundService.ResolveMap("Forest", forest, forest) == GameSoundMap.Forest
            && GameSoundService.ResolveMap("Forest", forest, forest + "##4_Forest") == GameSoundMap.Forest, "both Forest chapters share dirt/ambience");
        Require(GameSoundService.ResolveMap("Market", market, market + "#garbage") == GameSoundMap.Garbage
            && GameSoundService.ResolveMap("Market", market, market) == GameSoundMap.Garbage, "Garbage IDs resolve inside Market scene");
        Require(new[] { "#market", "##5_Neighborhood", "##4_Neighborhood" }.All(suffix =>
            GameSoundService.ResolveMap("Market", market, market + suffix) == GameSoundMap.MarketHouse), "Market and current/legacy neighborhood IDs share ambience");
        Require(GameSoundService.ResolveMap("Market", market, null) == GameSoundMap.None
            && GameSoundService.ResolveMap("Market", market, forest) == GameSoundMap.None
            && GameSoundService.ResolveMap("ControlSchemeSelect", "menu", market) == GameSoundMap.None
            && GameSoundService.ResolveMap("Base_Test", "base", forest) == GameSoundMap.None, "missing/stale room cache never leaks gameplay audio into menu/base");
        var gains = typeof(GameSoundService).GetField("EchoGains", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as float[];
        Require(gains != null && gains.SequenceEqual(new[] { 1f, .4f, .2f, .07f })
            && Mathf.Approximately(library.uiEchoInterval, .15f), "UI echoes: four taps at 100/40/20/7 percent, 0.15-second spacing");
        var menu = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/!UI/Main/MainCanvas.prefab");
        Require(menu != null && new[] { "Start", "Quit", "Setting" }.All(name =>
            menu.GetComponentsInChildren<Button>(true).Any(b => b.name == name)), "all three main menu buttons exist in the authored prefab");
        Report.AppendLine("LIMIT: EditMode asset/selection checks only; runtime input timing, scene transitions and speaker listening were not tested here.");
    }

    private static AudioClip Clip(string path)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + path);
        if (clip == null) throw new Exception("WAV failed to import as AudioClip: " + Root + path);
        return clip;
    }
    private static AudioClip[] Clips(string folder) => Directory.GetFiles(Root + folder, "*.wav", SearchOption.AllDirectories)
        .Select(p => p.Replace('\\', '/').Substring(Root.Length)).Select(Clip).ToArray();
    private static bool Loose(AudioClip clip) => clip.name.IndexOf("loose", StringComparison.OrdinalIgnoreCase) >= 0;
    private static bool Step(AudioClip clip) => clip.name.IndexOf("walk", StringComparison.OrdinalIgnoreCase) >= 0
        || clip.name.IndexOf("run", StringComparison.OrdinalIgnoreCase) >= 0;
    private static void Group(AudioClip[] actual, AudioClip[] expected, string name, bool allowEmpty = false)
        => Require(actual != null && (allowEmpty || expected.Length > 0) && actual.All(c => c != null)
            && actual.Length == expected.Length && actual.Distinct().Count() == actual.Length
            && new HashSet<AudioClip>(actual).SetEquals(expected), name + ": " + expected.Length + " clips");
    private static void Require(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        Report.AppendLine("OK " + description);
    }
}
#endif
