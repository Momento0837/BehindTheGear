#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Opt-in asset/PCM checks. Writes reports and an audio preview, but never modifies scenes/saves.</summary>
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
        Require(gains != null && gains.SequenceEqual(new[] { 1f, .25f, .1f, .03f })
            && Mathf.Approximately(library.uiEchoInterval, .15f), "UI echoes: four taps at 100/25/10/3 percent, 0.15-second spacing");
        VerifyEchoPCM(library.mainButton, library.uiEchoInterval, true, Folder + "/Main_Button_Echo.wav");
        VerifyEchoPCM(library.inventoryOpen, library.uiEchoInterval, false);
        VerifyEchoPCM(library.inventorySlide, library.uiEchoInterval, true);
        var menu = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/!UI/Main/MainCanvas.prefab");
        Require(menu != null && new[] { "Start", "Quit", "Setting" }.All(name =>
            menu.GetComponentsInChildren<Button>(true).Any(b => b.name == name)), "all three main menu buttons exist in the authored prefab");
        Report.AppendLine("LIMIT: EditMode asset/selection and actual generated PCM checks only; runtime input timing, scene transitions, audio-device output and speaker listening were not tested here.");
    }

    private static void VerifyEchoPCM(AudioClip original, float interval, bool isolatedTaps, string previewPath = null)
    {
        // Read Unity's imported samples first: checking constants alone cannot prove the audible signal.
        Require(original != null, "PCM source exists");
        if (original.loadState == AudioDataLoadState.Unloaded)
            Require(original.LoadAudioData(), original.name + ": PCM loading requested");
        Require(original.loadState == AudioDataLoadState.Loaded,
            original.name + ": imported PCM is loaded before echo generation");
        var source = new float[checked(original.samples * original.channels)];
        Require(original.GetData(source, 0), original.name + ": imported PCM GetData succeeds");
        Require(source.All(sample => !float.IsNaN(sample) && !float.IsInfinity(sample)),
            original.name + ": imported PCM is finite");

        // Keep the oracle independent of the implementation's EchoGains field.
        double[] expectedGains = { 1.0, 0.25, 0.1, 0.03 };
        int spacingFrames = Mathf.RoundToInt(interval * original.frequency);
        Require(spacingFrames > 0, original.name + ": positive sample-aligned echo spacing");
        AudioClip generated = null;
        try
        {
            generated = GameSoundService.CreateUIEchoClip(original, interval);
            Require(generated != null && generated != original,
                original.name + ": generator returns a new caller-owned clip");
            Require(generated.channels == original.channels && generated.frequency == original.frequency,
                original.name + ": generated channel count and sample rate preserve the imported format");
            int expectedFrames = checked(original.samples + spacingFrames * 3);
            Require(generated.samples == expectedFrames,
                original.name + ": generated frame count includes exactly three echo intervals (" + expectedFrames + ")");
            Report.AppendLine("INFO " + original.name + ": Unity length=" + Number(generated.length)
                + ", PCM duration=" + Number(expectedFrames / (double)original.frequency));
            var actual = new float[checked(generated.samples * generated.channels)];
            Require(generated.GetData(actual, 0), original.name + ": generated PCM GetData succeeds");
            Require(actual.All(sample => !float.IsNaN(sample) && !float.IsInfinity(sample)),
                original.name + ": generated PCM is finite");

            // Compare the entire returned signal with delayed copies of the actual imported waveform.
            // This also covers Inventory01, whose echoes overlap and cannot be judged by window peaks.
            double peak = 0, maximumError = 0;
            for (int sample = 0; sample < actual.Length; sample++)
            {
                double expected = 0;
                for (int tap = 0; tap < expectedGains.Length; tap++)
                {
                    int sourceIndex = sample - tap * spacingFrames * original.channels;
                    if (sourceIndex >= 0 && sourceIndex < source.Length)
                        expected += source[sourceIndex] * expectedGains[tap];
                }
                maximumError = Math.Max(maximumError, Math.Abs(actual[sample] - expected));
                peak = Math.Max(peak, Math.Abs(actual[sample]));
            }
            Require(maximumError <= 0.000002,
                original.name + ": generated PCM equals the expected four-tap waveform; max error=" + Number(maximumError));
            Require(peak < 1.0,
                original.name + ": generated PCM has no full-scale/clipping samples; peak=" + Number(peak));

            if (isolatedTaps)
            {
                Require(original.samples < spacingFrames,
                    original.name + ": source is shorter than echo spacing, so tap gains are measurable independently");
                double sourceRms = Rms(source, 0, source.Length);
                Require(sourceRms > 0.000001, original.name + ": source contains measurable audio");
                for (int tap = 0; tap < expectedGains.Length; tap++)
                {
                    int offset = tap * spacingFrames * original.channels;
                    double ratio = Rms(actual, offset, source.Length) / sourceRms;
                    Require(Math.Abs(ratio - expectedGains[tap]) <= 0.00001,
                        original.name + ": tap " + (tap + 1) + " starts at frame " + tap * spacingFrames
                        + ", actual RMS gain=" + Number(ratio) + ", expected=" + Number(expectedGains[tap]));
                    if (tap == expectedGains.Length - 1) continue;
                    double gapPeak = 0;
                    int gapEnd = (tap + 1) * spacingFrames * original.channels;
                    for (int sample = offset + source.Length; sample < gapEnd; sample++)
                        gapPeak = Math.Max(gapPeak, Math.Abs(actual[sample]));
                    Require(gapPeak <= 0.000002,
                        original.name + ": silent gap after tap " + (tap + 1) + " preserves sample-timed spacing");
                }
            }

            if (previewPath != null)
            {
                Require(generated.channels == 2, original.name + ": review WAV is stereo");
                WritePcm16Wave(previewPath, actual, generated.channels, generated.frequency);
                Require(File.Exists(previewPath) && new FileInfo(previewPath).Length == 44L + actual.Length * 2L,
                    "PCM16 review WAV exported from generated GetData: " + previewPath);
            }
        }
        finally
        {
            if (generated != null && generated != original) UnityEngine.Object.DestroyImmediate(generated);
        }
    }

    private static double Rms(float[] samples, int start, int count)
    {
        double sum = 0;
        for (int sample = start; sample < start + count; sample++)
            sum += (double)samples[sample] * samples[sample];
        return Math.Sqrt(sum / count);
    }

    private static string Number(double value) => value.ToString("G9", CultureInfo.InvariantCulture);

    private static void WritePcm16Wave(string path, float[] samples, int channels, int sampleRate)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using var writer = new BinaryWriter(File.Create(path));
        int dataBytes = checked(samples.Length * sizeof(short));
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(checked(36 + dataBytes));
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((ushort)1);
        writer.Write((ushort)channels);
        writer.Write(sampleRate);
        writer.Write(checked(sampleRate * channels * sizeof(short)));
        writer.Write((ushort)(channels * sizeof(short)));
        writer.Write((ushort)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
        foreach (float sample in samples)
            writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));
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
