#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
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
        string command;
        try
        {
            command = File.ReadAllText(request).Trim();
            if (command.Length == 0) return;
            File.Delete(request);
        }
        // The external requester may still be writing; retry on the next editor update.
        catch (IOException) { return; }
        Report.Clear();
        try
        {
            Require(command == "run" || command == "quest", "explicit run or quest request");
            if (command == "quest") VerifyOpeningQuest();
            else Verify();
            File.WriteAllText(Folder + "/result.txt", "PASS Unity " + Application.unityVersion + "\n" + Report);
        }
        catch (Exception error)
        {
            File.WriteAllText(Folder + "/result.txt", "FAIL Unity " + Application.unityVersion + "\n" + Report + error);
            Debug.LogError("[GameSoundChecks] " + error.Message);
        }
    }

    private static void VerifyOpeningQuest()
    {
        var library = Resources.Load<GameSoundLibrary>("Audio/GameSoundLibrary");
        var clip = Clip("Quest/Quest_1.wav");
        Require(library != null && library.openingQuest == clip, "opening quest references the original Quest_1 WAV");
        Require(Mathf.Approximately(library.openingQuestVolume, .1f)
            && Mathf.Approximately(library.openingQuestPitchSemitones, 25f)
            && Mathf.Abs(library.OpeningQuestPitch - Mathf.Pow(2f, 25f / 12f)) < .000001f,
            "quest gain is 10 percent and pitch is +25 semitones");
        Require(Mathf.Abs(library.openingQuestAudibleOnsetSeconds - 686f / 48000f) < .000001f
            && Mathf.Abs(library.OpeningQuestOnsetDelay - (686f / 48000f) / Mathf.Pow(2f, 25f / 12f)) < .000001f,
            "glyph deadline includes the pitch-adjusted original quiet beginning");
        var parameters = typeof(InGameQuestHUD).GetMethod("PresentOpeningQuest").GetParameters();
        Require(parameters.Length == 2 && parameters[1].HasDefaultValue && Equals(parameters[1].DefaultValue, false),
            "quest cue requires opt-in; ordinary presentation/death restart defaults to silent");
        if (clip.loadState == AudioDataLoadState.Unloaded) Require(clip.LoadAudioData(), "quest PCM loading requested");
        Require(clip.loadState == AudioDataLoadState.Loaded, "quest imported PCM is ready");
        var imported = new float[checked(clip.samples * clip.channels)];
        Require(clip.GetData(imported, 0) && imported.All(x => !float.IsNaN(x) && !float.IsInfinity(x)),
            "quest imported PCM is readable from sample zero and finite");

        // Compare the actual imported beginning against the WAV at offset zero, not a trimmed copy.
        byte[] wav = File.ReadAllBytes(Root + "Quest/Quest_1.wav");
        int dataOffset = -1, dataBytes = 0, channels = 0, frequency = 0, bits = 0, format = 0;
        for (int offset = 12; offset + 8 <= wav.Length;)
        {
            string id = Encoding.ASCII.GetString(wav, offset, 4);
            int bytes = BitConverter.ToInt32(wav, offset + 4);
            Require(bytes >= 0 && (long)offset + 8 + bytes <= wav.Length, "valid quest WAV chunk " + id);
            if (id == "fmt ")
            {
                format = BitConverter.ToUInt16(wav, offset + 8);
                channels = BitConverter.ToUInt16(wav, offset + 10);
                frequency = BitConverter.ToInt32(wav, offset + 12);
                bits = BitConverter.ToUInt16(wav, offset + 22);
            }
            if (id == "data") { dataOffset = offset + 8; dataBytes = bytes; }
            offset += 8 + bytes + (bytes & 1);
        }
        Require(dataOffset >= 0 && format == 1 && bits == 24 && channels == clip.channels && frequency == clip.frequency,
            "original quest WAV is PCM24 with the imported channel count and sample rate");
        int sourceFrames = dataBytes / (channels * 3);
        Require(Math.Abs(clip.samples - sourceFrames) <= 1, "import preserves the entire WAV duration including its quiet beginning");
        int count = Math.Min(imported.Length, frequency * channels / 10);
        double sourceEnergy = 0, importedEnergy = 0, dot = 0, differenceEnergy = 0;
        for (int i = 0; i < count; i++)
        {
            int offset = dataOffset + i * 3;
            int pcm = wav[offset] | wav[offset + 1] << 8 | wav[offset + 2] << 16;
            if ((pcm & 0x800000) != 0) pcm |= unchecked((int)0xff000000);
            double source = pcm / 8388608d;
            sourceEnergy += source * source;
            importedEnergy += imported[i] * imported[i];
            dot += source * imported[i];
            differenceEnergy += (source - imported[i]) * (source - imported[i]);
        }
        double correlation = dot / Math.Sqrt(sourceEnergy * importedEnergy);
        Require(sourceEnergy > 0 && importedEnergy > 0 && correlation > .98
            && Math.Sqrt(differenceEnergy / count) < .02,
            "first 100 ms remain aligned to the original sample zero; correlation=" + Number(correlation)
            + ", RMS import error=" + Number(Math.Sqrt(differenceEnergy / count)));
        VerifyOpeningQuestHighPitch(library, clip, imported);
        VerifyOpeningQuestGlyph();
        Report.AppendLine("LIMIT: isolated EditMode asset/PCM/TMP checks only. No live scene, saves, playback, audio-device latency or listening test was performed.");
    }

    private static void VerifyOpeningQuestHighPitch(GameSoundLibrary library, AudioClip original, float[] originalPcm)
    {
        var preview = EditorSceneManager.NewPreviewScene();
        AudioClip octave = null;
        GameObject root = null;
        try
        {
            octave = GameSoundService.CreateOpeningQuestOctave(original);
            Require(octave != null && octave.samples == original.samples && octave.channels == original.channels
                && octave.frequency == original.frequency * 2, "high-pitch cue preserves every frame/channel at double playback rate");
            var pcm = new float[originalPcm.Length];
            Require(octave.GetData(pcm, 0), "high-pitch PCM is readable");
            float maxError = 0f;
            for (int i = 0; i < pcm.Length; i++) maxError = Mathf.Max(maxError, Mathf.Abs(pcm[i] - originalPcm[i]));
            Require(maxError < .000001f, "all samples including the quiet beginning and tail are unchanged; max error=" + Number(maxError));
            root = new GameObject("Quest high pitch check", typeof(AudioSource));
            SceneManager.MoveGameObjectToScene(root, preview);
            var source = root.GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.clip = original;
            source.pitch = library.OpeningQuestPitch;
            Report.AppendLine("INFO native AudioSource direct pitch=" + Number(source.pitch) + "; requested total=" + Number(library.OpeningQuestPitch));
            source.clip = octave;
            source.pitch = library.OpeningQuestPitch * original.frequency / octave.frequency;
            source.volume = library.openingQuestVolume;
            source.timeSamples = 0;
            double actualRatio = (double)octave.frequency / original.frequency * source.pitch;
            Require(Math.Abs(actualRatio - Math.Pow(2d, 25d / 12d)) < .00001d && Mathf.Approximately(source.volume, .1f)
                && source.timeSamples == 0, "native playback configuration achieves full +25 semitones at 10 percent from sample zero");
            Report.AppendLine("INFO reported clip lengths: original=" + Number(original.length) + ", octave=" + Number(octave.length)
                + "; sample durations=" + Number((double)original.samples / original.frequency) + ", " + Number((double)octave.samples / octave.frequency));
            Require(Math.Abs((double)original.samples / original.frequency / actualRatio
                - (double)octave.samples / octave.frequency / source.pitch) < .000001d,
                "sample-accurate full-clip duration and glyph onset use the same effective pitch ratio");
        }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (octave != null) UnityEngine.Object.DestroyImmediate(octave);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static void VerifyOpeningQuestGlyph()
    {
        GameObject root = null;
        InGameQuestHUD hud = null;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(InGameQuestHUD);
        try
        {
            root = PrefabUtility.LoadPrefabContents("Assets/!UI/Ingame/InGame_Canvas.prefab");
            hud = root.GetComponent<InGameQuestHUD>();
            Require(hud != null, "authored HUD exists in an isolated prefab scene");
            type.GetMethod("OnDisable", flags).Invoke(hud, null);
            type.GetMethod("OnEnable", flags).Invoke(hud, null);
            type.GetField("openingPresentation", flags).SetValue(hud, true);
            type.GetField("requestedContent", flags).SetValue(hud, "추적자 섬멸");
            type.GetMethod("BeginAppearance", flags).Invoke(hud, null);
            var text = (TMP_Text)type.GetField("questText", flags).GetValue(hud);
            var order = (List<int>)type.GetField("glyphOrder", flags).GetValue(hud);
            Require(order.Count > 0 && text.textInfo.characterInfo[order[0]].character == '추', "first visual glyph is 추");
            Require(OpeningGlyphAlpha(text, order[0]) == 0, "first glyph is invisible at BeginAppearance");
            type.GetMethod("Tick", flags).Invoke(hud, new object[] { 0f });
            Require(OpeningGlyphAlpha(text, order[0]) == 0, "Tick(0) keeps the glyph hidden during the scheduled quiet beginning");
            type.GetMethod("Tick", flags).Invoke(hud, new object[] { 1f / 60f });
            Require(OpeningGlyphAlpha(text, order[0]) > 0, "first glyph becomes visible on the first 60 Hz tick after its deadline");
        }
        finally
        {
            if (hud != null)
            {
                type.GetMethod("OnDisable", flags).Invoke(hud, null);
                // Runtime-generated graphics normally use deferred Destroy. Clean them immediately
                // before unloading this EditMode-only preview so nothing escapes the check.
                foreach (string name in new[] { "lightRoot", "lightSprite", "lightTexture", "backgroundDim" })
                {
                    var field = type.GetField(name, flags);
                    var value = field.GetValue(hud) as UnityEngine.Object;
                    field.SetValue(hud, null);
                    if (value is Component component) UnityEngine.Object.DestroyImmediate(component.gameObject);
                    else if (value != null) UnityEngine.Object.DestroyImmediate(value);
                }
            }
            if (root != null) PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static byte OpeningGlyphAlpha(TMP_Text text, int index)
    {
        var glyph = text.textInfo.characterInfo[index];
        return text.textInfo.meshInfo[glyph.materialReferenceIndex].colors32[glyph.vertexIndex].a;
    }

    private static void Verify()
    {
        var library = Resources.Load<GameSoundLibrary>("Audio/GameSoundLibrary");
        Require(library != null, "build-loadable sound library exists");
        var baseMusic = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/!Music/Base_BGM.wav");
        Require(baseMusic != null && library.baseBgm == baseMusic, "authored Base_BGM is included in the build sound library");
        var marketMusic = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/!Music/Market_BGM.wav");
        Require(marketMusic != null && library.marketBgm == marketMusic,
            "authored Market_BGM is included in the build sound library");
        Require(library.marketMusicFadeDuration > 0 && library.marketMusicVolume > 0 && library.marketMusicVolume <= 1,
            "Market music has a positive fade duration and audible volume");
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
        Group(library.dirtSteps, dirt.Where(c => !Loose(c)).ToArray(), "dirt regular steps");
        Group(library.dirtLooseSteps, dirt.Where(Loose).ToArray(), "dirt loose steps");
        Group(library.concreteSteps, concrete.Where(c => !Loose(c) && !Slide(c)).ToArray(), "concrete regular steps");
        Group(library.concreteLooseSteps, concrete.Where(c => Loose(c) || Slide(c)).ToArray(), "concrete turn/jump slide clips");
        Group(library.concreteLandings, Clips("Walking/Footsteps/Landing_Concrete"), "all landing-folder clips");
        var concreteDeathLandings = Clips("Walking/Footsteps/Landing_Concrete")
            .Where(c => c.name.IndexOf("concrete", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        Require(concreteDeathLandings.Length == 6, "landing folder contains six concrete-named death impact clips");
        Group(library.concreteDeathLandings, concreteDeathLandings, "death impacts use only the six concrete landing clips");
        Group(library.punchHits, Clips("Punches"), "authored punch impact clips");
        Require(library.playerAttack13 == Clip("Attacking/Player_Attack_1_3.wav")
            && library.playerAttack2 == Clip("Attacking/Player_Attack_2.wav")
            && library.gangBAttack == Clip("Attacking/GangB_Attack.wav"), "three authored attack sound references");
        Require(Mathf.Approximately(library.attackVolume, 1f), "attack sound volume is 100 percent before actor-specific gain");
        Require(library.PlayerAttack(1) == library.playerAttack13 && library.PlayerAttack(3) == library.playerAttack13
            && library.PlayerAttack(2) == library.playerAttack2
            && new[] { -1, 0, 4, int.MaxValue }.All(index => library.PlayerAttack(index) == null),
            "player attacks 1/3 share their sound, attack 2 uses its own sound, invalid indices are silent");
        Require(Mathf.Approximately(library.footstepsVolume, .8f), "movement sound volume is 80 percent");
        Require(!library.dirtSteps.Intersect(library.dirtLooseSteps).Any()
            && !library.concreteSteps.Intersect(library.concreteLooseSteps).Any(), "loose clips never belong to the regular step category");
        Require(library.mainButton == Clip("Buttons/Main_Button.wav")
            && library.inventoryOpen == Clip("Buttons/Inventory_01.wav")
            && library.inventorySlide == Clip("Buttons/Inventory_02.wav")
            && library.reservedMetallicButton == Clip("Buttons/metallic_button_02.wav"), "four authored UI clip references");
        var savedRandom = UnityEngine.Random.state;
        try
        {
            UnityEngine.Random.InitState(7219);
            var dirtRegular = new HashSet<AudioClip>(library.dirtSteps);
            var dirtLoose = new HashSet<AudioClip>(library.dirtLooseSteps);
            var concreteRegular = new HashSet<AudioClip>(library.concreteSteps);
            var concreteAccents = new HashSet<AudioClip>(library.concreteLooseSteps);
            for (int i = 0; i < 10000; i++)
            {
                var regular = library.Footstep(GameSoundMap.Forest);
                var loose = library.LooseFootstep(GameSoundMap.Forest);
                if (!dirtRegular.Contains(regular) || Loose(regular))
                    throw new Exception("A regular Forest footstep selected a loose, unrelated or null clip.");
                if (!dirtLoose.Contains(loose) || !Loose(loose))
                    throw new Exception("A Forest turn/jump cue selected a non-loose, unrelated or null clip.");
                foreach (var map in new[] { GameSoundMap.Garbage, GameSoundMap.MarketHouse })
                {
                    var concreteStep = library.Footstep(map);
                    if (!concreteRegular.Contains(concreteStep) || Loose(concreteStep) || Slide(concreteStep))
                        throw new Exception("A concrete footstep selected a loose/slide, unrelated or null clip.");
                    if (!concreteAccents.Contains(library.LooseFootstep(map)))
                        throw new Exception("A concrete turn/jump did not select its slide/loose group.");
                }
            }
            Report.AppendLine("OK 10000 draws per map keep regular steps and turn/jump loose/slide cues strictly separate");
            Require(library.Footstep(GameSoundMap.None) == null && library.LooseFootstep(GameSoundMap.None) == null,
                "regular and loose cues are silent outside gameplay maps");
        }
        finally { UnityEngine.Random.state = savedRandom; }
        VerifyFootstepAnimationEvents();
        VerifyAttackAnimationEvents();
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
        Require(GameSoundService.IsMarketRoom("Market", market, market + "#market"),
            "Market room enables Market_BGM");
        Require(!GameSoundService.IsMarketRoom("Market", market, market + "#garbage")
            && !GameSoundService.IsMarketRoom("Market", market, market)
            && !GameSoundService.IsMarketRoom("Market", market, market + "##5_Neighborhood")
            && !GameSoundService.IsMarketRoom("Market", market, market + "##4_Neighborhood")
            && !GameSoundService.IsMarketRoom("Market", market, null)
            && !GameSoundService.IsMarketRoom("Market", market, forest + "#market")
            && !GameSoundService.IsMarketRoom("Forest", forest, forest + "#market")
            && !GameSoundService.IsMarketRoom("Base_Test", "base", market + "#market"),
            "Garbage, neighborhood, other scenes and stale room IDs never enable Market_BGM");
        VerifyBackgroundScreenFade();
        var gains = typeof(GameSoundService).GetField("EchoGains", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as float[];
        Require(gains != null && gains.SequenceEqual(new[] { 1f, .25f, .1f, .03f })
            && Mathf.Approximately(library.uiEchoInterval, .15f), "UI echoes: four taps at 100/25/10/3 percent, 0.15-second spacing");
        VerifyEchoPCM(library.mainButton, library.uiEchoInterval, true, Folder + "/Main_Button_Echo.wav");
        VerifyEchoPCM(library.inventoryOpen, library.uiEchoInterval, false);
        VerifyEchoPCM(library.inventorySlide, library.uiEchoInterval, true);
        var menu = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/!UI/Main/MainCanvas.prefab");
        Require(menu != null && new[] { "Start", "Quit", "Setting" }.All(name =>
            menu.GetComponentsInChildren<Button>(true).Any(b => b.name == name)), "all three main menu buttons exist in the authored prefab");
        Report.AppendLine("LIMIT: EditMode asset/selection, registered-BGM fade and actual generated PCM checks only; runtime input timing, full scene transitions, audio-device output and speaker listening were not tested here.");
    }

    private static void VerifyBackgroundScreenFade()
    {
        Require(!BackgroundMusicTrack.IsTransitionActive, "BGM fade check starts without an active screen transition");
        var preview = EditorSceneManager.NewPreviewScene();
        GameObject musicObject = null, ambienceObject = null;
        try
        {
            musicObject = new GameObject("BGM Fade Check") { hideFlags = HideFlags.HideAndDontSave };
            ambienceObject = new GameObject("Ambient Fade Exclusion Check") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(musicObject, preview);
            SceneManager.MoveGameObjectToScene(ambienceObject, preview);
            var music = musicObject.AddComponent<AudioSource>();
            var ambience = ambienceObject.AddComponent<AudioSource>();
            music.playOnAwake = ambience.playOnAwake = false;
            music.volume = .6f;
            ambience.volume = .7f;
            var track = BackgroundMusicTrack.Attach(music);
            Require(track != null, "existing BGM source attaches to the transition envelope");
            var transition = BackgroundMusicTrack.BeginScreenFade();
            try
            {
                Require(BackgroundMusicTrack.IsTransitionActive, "BGM transition lease is active");
                foreach (float progress in new[] { 0f, .5f, 1f })
                {
                    transition.SetProgress(progress);
                    Require(Mathf.Abs(music.volume - .6f * (1f - progress)) < .00001f,
                        "registered BGM volume follows screen fade progress " + Number(progress)
                        + ": volume=" + Number(music.volume));
                    Require(Mathf.Abs(ambience.volume - .7f) < .00001f,
                        "unregistered ambience keeps its own volume during screen fade progress " + Number(progress));
                }
            }
            finally { transition.Dispose(); }
            Require(!BackgroundMusicTrack.IsTransitionActive, "disposing the BGM transition lease clears transition state");
        }
        finally
        {
            if (musicObject != null) UnityEngine.Object.DestroyImmediate(musicObject);
            if (ambienceObject != null) UnityEngine.Object.DestroyImmediate(ambienceObject);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static void VerifyFootstepAnimationEvents()
    {
        var running = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/!Animation/Player/Running.anim");
        var cloakRunning = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/!Animation/Player/Cloak/Running.anim");
        Require(running != null && cloakRunning != null, "body and cloak Running clips exist separately");
        Require(Mathf.Approximately(running.frameRate, 60f) && running.length > 36f / running.frameRate,
            "body Running clip has a valid frame 36 at 60 fps");
        var contacts = AnimationUtility.GetAnimationEvents(running)
            .Where(e => e.functionName == "OnFootstepContact").OrderBy(e => e.time).ToArray();
        Require(contacts.Length == 2 && Mathf.Abs(contacts[0].time - 15f / running.frameRate) < .000001f
            && Mathf.Abs(contacts[1].time - 36f / running.frameRate) < .000001f,
            "body Running emits exactly two foot contacts: frame 15 and frame 36 (0.25 and 0.6 seconds)");
        Require(AnimationUtility.GetAnimationEvents(cloakRunning).Length == 0,
            "cloak Running has no animation events and cannot duplicate body footsteps");

        var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/!Animation/Monster/Walk.anim");
        Require(walk != null && Mathf.Approximately(walk.frameRate, 60f) && walk.isLooping
            && walk.length > 42f / walk.frameRate, "GangB Walk loops with a valid frame 42 at 60 fps");
        var monsterContacts = AnimationUtility.GetAnimationEvents(walk)
            .Where(e => e.functionName == "OnFootstepContact").OrderBy(e => e.time).ToArray();
        Require(monsterContacts.Length == 2 && Mathf.Abs(monsterContacts[0].time) < .000001f
            && Mathf.Abs(monsterContacts[1].time - 42f / walk.frameRate) < .000001f,
            "GangB Walk emits exactly two foot contacts: frame 0 and frame 42 (0 and 0.7 seconds)");

        VerifyDeathAnimationEvents("Assets/!Animation/Player/Dead.anim",
            new[] { 23, 54 }, new[] { .5f, 1f }, new[] { 0, 1 });
        VerifyDeathAnimationEvents("Assets/!Animation/Monster/Dead.anim",
            new[] { 32 }, new[] { 1f }, new[] { 0 });
        var cloakDead = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/!Animation/Player/Cloak/Dead.anim");
        Require(cloakDead != null && AnimationUtility.GetAnimationEvents(cloakDead).Length == 0,
            "cloak Dead has no animation events and cannot duplicate body death impacts");
    }

    private static void VerifyDeathAnimationEvents(string path, int[] frames, float[] gains, int[] ids)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        Require(clip != null && Mathf.Approximately(clip.frameRate, 60f) && !clip.isLooping
            && clip.length > frames.Max() / clip.frameRate, path + ": death clip contains the requested frames at 60 fps without looping");
        var impacts = AnimationUtility.GetAnimationEvents(clip).OrderBy(e => e.time).ToArray();
        Require(impacts.Length == frames.Length && impacts.Select((e, i) =>
            e.functionName == "OnDeathLanding" && Mathf.Abs(e.time - frames[i] / clip.frameRate) < .000001f
            && Mathf.Approximately(e.floatParameter, gains[i]) && e.intParameter == ids[i]).All(valid => valid),
            path + ": death impact frames, volume gains and event IDs match exactly");
    }

    private static void VerifyAttackAnimationEvents()
    {
        foreach (string prefix in new[] { "Attack_", "Run_Attack_" })
        for (int index = 1; index <= 3; index++)
        {
            string name = prefix + index;
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/!Animation/Player/" + name + ".anim");
            Require(clip != null && Mathf.Approximately(clip.frameRate, 60f) && !clip.isLooping
                && clip.length > 14f / clip.frameRate, name + ": body attack has frame 14 at 60 fps without looping");
            var sounds = AnimationUtility.GetAnimationEvents(clip).Where(e => e.functionName == "OnAttackSound").ToArray();
            Require(sounds.Length == 1 && Mathf.Abs(sounds[0].time - 14f / clip.frameRate) < .000001f
                && sounds[0].intParameter == index, name + ": exactly one attack sound at frame 14 with its combo index");
            var cloak = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/!Animation/Player/Cloak/" + name + ".anim");
            Require(cloak != null && !AnimationUtility.GetAnimationEvents(cloak).Any(e => e.functionName == "OnAttackSound"),
                name + ": cloak cannot duplicate the body attack sound");
        }
        var gangB = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/!Animation/Monster/Attack.anim");
        Require(gangB != null && Mathf.Approximately(gangB.frameRate, 60f) && !gangB.isLooping
            && gangB.length > 34f / gangB.frameRate, "GangB Attack has frame 34 at 60 fps without looping");
        var impacts = AnimationUtility.GetAnimationEvents(gangB);
        Require(impacts.Length == 1 && impacts[0].functionName == "ApplyAttackHit"
            && Mathf.Abs(impacts[0].time - 34f / gangB.frameRate) < .000001f,
            "GangB preserves its single ApplyAttackHit event at frame 34 for hit resolution and attack audio");
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
    private static bool Slide(AudioClip clip) => clip.name.IndexOf("slide", StringComparison.OrdinalIgnoreCase) >= 0;
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
