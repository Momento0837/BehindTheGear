using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class CutsceneInputRecoveryCheck
{
    const string Request = "Temp/CutsceneInputFix/run-check";
    const string Output = "Temp/CutsceneInputFix/results.txt";
    const string Key = "CutsceneInputRecoveryCheck.Active";
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static double started, since;
    static int stage;
    static CutsceneVideoSequence clip;
    static PlayerSkills skills;
    static bool resumedDuringQuest;
    static CutsceneInputRecoveryCheck() => EditorApplication.update += Tick;
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        File.AppendAllText(Output, "PASS " + message + "\n");
    }
    static void Next() { stage++; since = EditorApplication.timeSinceStartup; }
    static void Tick()
    {
        if (File.Exists(Request) && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(Request);
            if (Application.unityVersion != "6000.3.23f1") return;
            File.WriteAllText(Output, "Unity " + Application.unityVersion + "\n");
            SessionState.SetBool(Key, true); stage=0; started=since=0; resumedDuringQuest=false;
            EditorApplication.isPlaying=true;
            return;
        }
        if (!SessionState.GetBool(Key,false) || !EditorApplication.isPlaying) return;
        try
        {
            if (started==0) started=since=EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup-started>50) throw new Exception("Timeout stage " + stage);
            double elapsed=EditorApplication.timeSinceStartup-since;
            if(stage==0 && elapsed>2)
            {
                if(SceneManager.GetActiveScene().name!="ControlSchemeSelect") return;
                SaveGameSession.BeginNewGame();
                SceneFadeTransition.LoadScene("Forest",.8f); Next();
            }
            else if(stage==1)
            {
                clip=UnityEngine.Object.FindFirstObjectByType<CutsceneVideoSequence>();
                if(clip==null || !clip.IsPlaying) return;
                Check(SceneFadeTransition.IsRunning && Time.timeScale==0,"Real Forest intro overlaps scene fade"); Next();
            }
            else if(stage==2 && !SceneFadeTransition.IsRunning)
            {
                Check(clip.IsPlaying && Time.timeScale==0,"Fade ending keeps active cutscene paused");
                Check(clip.EndingTransition!=null,"Actual opening quest transition connected");
                clip.GetType().GetMethod("OnVideoFinished",Flags).Invoke(clip,new object[]{null}); Next();
            }
            else if(stage==3 && clip.IsPlaying && !resumedDuringQuest
                && !((GameObject)clip.GetType().GetField("videoRoot",Flags).GetValue(clip)).activeSelf)
            {
                Check(Time.timeScale==1,"World time resumes while opening quest animation is still running");
                resumedDuringQuest=true;
            }
            else if(stage==3 && !clip.IsPlaying)
            {
                Check(resumedDuringQuest,"Opening quest did not hold the cinematic pause");
                Check(Time.timeScale==1,"Natural end plus opening quest restores world time");
                skills=UnityEngine.Object.FindFirstObjectByType<PlayerSkills>();
                Check(skills!=null && skills.CanUseSkills && !skills.GetComponent<PlayerControlLock>().IsLocked,
                    "Player movement/input/skill controls unlocked after intro");
                // Reverse overlap: finish the video while a second fade still owns a pause.
                clip.PlayFromInteraction();
                skills.StartCoroutine(SceneFadeTransition.FadeThrough(()=>{},.4f));
                clip.GetType().GetMethod("Finish",Flags).Invoke(clip,new object[]{true});
                Check(SceneFadeTransition.IsRunning && Time.timeScale==0,"Skip keeps unfinished fade paused"); Next();
            }
            else if(stage==4 && elapsed>1)
            {
                Check(!clip.IsPlaying && !SceneFadeTransition.IsRunning && Time.timeScale==1,"Skip overlap restores normal time");
                Check(skills.CanUseSkills && skills.GetComponent<PlayerInputReader>().enabled,"Input restored after skip overlap");
                File.AppendAllText(Output,"COMPLETE\n"); SessionState.SetBool(Key,false); EditorApplication.isPlaying=false;
            }
        }
        catch(Exception e)
        {
            File.AppendAllText(Output,"FAIL "+e+"\n"); SessionState.SetBool(Key,false); EditorApplication.isPlaying=false;
        }
    }
}
