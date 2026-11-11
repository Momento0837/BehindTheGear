using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BehindTheGear.HideOut;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class GameplayRevisionCheck
{
    const string Request = "Temp/GameplayRevision/run-check";
    const string Output = "Temp/GameplayRevision/check-results.txt";
    const string Key = "GameplayRevisionCheck.Running";
    static int stage;
    static double started, since;
    static PlayerSkills skills;
    static PlayerMana mana;
    static int initialMana;
    static HideOutEntrance entrance;
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static GameplayRevisionCheck() { EditorApplication.update += Tick; }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); File.AppendAllText(Output, "PASS " + message + "\n"); }
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Flags).SetValue(target, value);
    static object Get(object target, string field) => target.GetType().GetField(field, Flags).GetValue(target);
    static void Next() { stage++; since = EditorApplication.timeSinceStartup; }
    static void Tick()
    {
        if (File.Exists(Request) && !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(Request);
            File.WriteAllText(Output, "Unity " + Application.unityVersion + "\n");
            if (Application.unityVersion != "6000.3.23f1") return;
            stage = 0; started = since = EditorApplication.timeSinceStartup;
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true; return;
        }
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
        try
        {
            if (started == 0) started = since = EditorApplication.timeSinceStartup;
            double elapsed = EditorApplication.timeSinceStartup - since;
            if (EditorApplication.timeSinceStartup - started > 55) throw new Exception("Timeout stage " + stage);
            if (stage == 0)
            {
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "ControlSchemeSelect" && elapsed > 2 && !SceneFadeTransition.IsRunning)
                { SaveGameSession.BeginNewGame(); SaveGameSession.StartExpedition("Market"); return; }
                skills = UnityEngine.Object.FindFirstObjectByType<PlayerSkills>();
                if (skills == null || !skills.CanUseSkills || elapsed < 3) return;
                mana = skills.GetComponent<PlayerMana>();
                var movement = skills.GetComponent<PlayerMovement2D>();
                Check(!(bool)Get(movement,"dashAfterimages") && Mathf.Approximately((float)Get(movement,"dashDuration"),.25f), $"Dash .25s, no afterimages (actual {Get(movement,"dashDuration")}, {Get(movement,"dashAfterimages")})");
                Check(Mathf.Approximately((float)Get(movement,"walkSpeed"),5.25f), "Walk speed 5.25 (75% run)");
                mana.RestoreMana(mana.MaxMana); initialMana=mana.CurrentMana;
                var dagger=skills.GetComponent<PlayerPoisonDaggerSkill>();
                Check(dagger.TryThrow(), "Dagger can fire");
                Check(UnityEngine.Object.FindObjectsByType<PlayerPoisonDaggerProjectile>(FindObjectsSortMode.None).Length==1,"Exactly one dagger");
                Check(mana.CurrentMana==initialMana-Mathf.CeilToInt(mana.MaxMana*.1f),"Dagger costs max MP 10%");
                Check(dagger.CooldownRemaining>29 && !dagger.TryThrow(),"Dagger 30s cooldown blocks repeat");
                mana.RestoreMana(mana.MaxMana); initialMana=mana.CurrentMana;
                Check(skills.TryUseStealth() && !skills.TryUseStealth(),"Stealth starts; immediate toggle blocked"); Next();
            }
            else if(stage==1 && elapsed>.65)
            {
                Check(skills.IsStealthed,"Stealth remains active");
                Check(mana.CurrentMana==initialMana-Mathf.FloorToInt(mana.MaxMana*skills.StealthDrainPercent/100),"Stealth .5s drain uses stat-adjusted percent");
                Check(skills.TryUseStealth() && !skills.IsStealthed && !skills.TryUseStealth(),"Toggle off and 1.5s reactivation lock"); Next();
            }
            else if(stage==2 && elapsed>1.6)
            {
                Check(skills.TryUseStealth(),"Stealth reactivates after 1.5s"); skills.BreakStealth();
                Set(skills,"stealthReadyAt",0f); mana.TrySpend(mana.CurrentMana);
                Check(!skills.TryUseStealth(),"Zero MP blocks stealth");
                Set(mana,"regenerationFraction",0f); Set(mana,"regenerationAt",Time.unscaledTime);
                Next();
            }
            else if(stage==3 && elapsed>.1)
            {
                Check(mana.CurrentMana==Mathf.FloorToInt(mana.MaxMana*.01f),"MP regen 1% per tick");
                mana.GetType().GetMethod("OnHealthChanged",Flags).Invoke(mana,new object[]{0,100});
                Check((float)Get(mana,"regenerationAt")-Time.unscaledTime>9.9f,"HP loss resets 10s regen delay");
                var shader=Resources.Load<Shader>("Effects/HideOutLockCover");
                Check(shader!=null && !ShaderUtil.ShaderHasError(shader),"Gray cover shader imports");
                entrance=UnityEngine.Object.FindFirstObjectByType<HideOutEntrance>();
                Check(entrance!=null,"Market entrance exists"); entrance.Enter(); Next();
            }
            else if(stage==4 && elapsed>3)
            {
                var view=UnityEngine.Object.FindFirstObjectByType<HideOutScene>(); if(view==null)return;
                var locked=view.GetType().GetMethod("IsLocked",Flags);
                foreach(Facility facility in Enum.GetValues(typeof(Facility)))
                    Check((bool)locked.Invoke(view,new object[]{facility})==(facility!=Facility.Bed),"Facility lock: "+facility);
                Check(UnityEngine.Object.FindObjectsByType<HideOutFacilityLockCover>(FindObjectsSortMode.None).Length>=7,"Gray covers exist");
                Check(UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Any(c=>c.isActiveAndEnabled),"Base camera active");
                Check(!skills.GetComponent<PlayerInputReader>().enabled && !skills.GetComponent<Rigidbody2D>().simulated,"Field controls/physics suspended in base");
                var bridge=skills.GetComponent<HideOutPlayerBridge>(); bridge.StartCoroutine(HideOutTravel.Exit(bridge,view.gameObject.scene)); Next();
            }
            else if(stage==5 && elapsed>2)
            {
                Check(skills.GetComponent<PlayerInputReader>().enabled && skills.GetComponent<Rigidbody2D>().simulated,"Controls restored on return");
                foreach(var hud in UnityEngine.Object.FindObjectsByType<LocationHUD>(FindObjectsSortMode.None))
                    Check(((CanvasGroup)Get(hud,"visibility")).alpha==0,"Info popup hidden on return");
                var clip=UnityEngine.Object.FindFirstObjectByType<CutsceneVideoSequence>();
                Check(clip!=null,"Market cutscene available"); clip.PlayFromInteraction();
                Check(clip.IsPlaying && Time.timeScale==0,"Cutscene freezes world");
                clip.GetType().GetMethod("Finish",Flags).Invoke(clip,new object[]{false});
                Check(Time.timeScale==1,"Cutscene restores world time");
                skills.StartCoroutine(SceneFadeTransition.FadeThrough(()=>{},.15f));
                Check(SceneFadeTransition.IsRunning && Time.timeScale==0,"Portal fade freezes world"); Next();
            }
            else if(stage==6 && elapsed>.5)
            {
                Check(!SceneFadeTransition.IsRunning && Time.timeScale==1,"Portal fade restores world time");
                File.AppendAllText(Output,"COMPLETE\n"); SessionState.SetBool(Key,false); EditorApplication.isPlaying=false;
            }
        }
        catch(Exception e)
        {
            File.AppendAllText(Output,"FAIL "+e+"\n"); SessionState.SetBool(Key,false); EditorApplication.isPlaying=false;
        }
    }
}
