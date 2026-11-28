#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
[InitializeOnLoad]
public static class GarbageEquipmentSequenceCheck
{
    const string Folder="Temp/Nov19/",Key="BTG.GarbageEquipmentSequenceCheck";
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static int stage; static double next,deadline;
    static PlayerMovement2D player;static InventoryUI ui;static StorySceneFlow flow;static ConversationDialogue dialogue;
    static bool sawBubble,sawMonologue,sawReaction;
    static readonly List<string> spokenLines = new();
    static string[] expectedLines;
    static int runtimeErrors;
    static void Observe(string message,string trace,LogType type){if(type is LogType.Error or LogType.Exception)runtimeErrors++;}
    static void RecordLine()=>spokenLines.Add(dialogue.BodyText.text);
    static object Read(object o,string name)=>o.GetType().GetField(name,Flags).GetValue(o);
    static void Call(object o,string name)=>o.GetType().GetMethod(name,Flags).Invoke(o,null);
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static GarbageEquipmentSequenceCheck(){EditorApplication.update+=Tick;}
    static void Finish(string result)
    {
        if(dialogue!=null)dialogue.LineRevealCompleted-=RecordLine;
        Application.logMessageReceived-=Observe;ManualSaveStore.UseValidationDirectory(null);
        File.WriteAllText(Folder+"equipment-sequence-result.txt",result+"\nQuestion="+sawReaction+"; pursuit shout="+sawBubble+"; runtime errors="+runtimeErrors+"\nSpoken lines:\n"+string.Join("\n",spokenLines));
        SessionState.SetBool(Key,false);EditorApplication.isPlaying=false;stage=0;
    }
    static void Tick()
    {
        if(!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&File.Exists(Folder+"check-equipment-sequence"))
        {
            File.Delete(Folder+"check-equipment-sequence");if(Application.unityVersion!="6000.3.23f1")throw new Exception("Wrong Unity version");
            for(int i=0;i<SceneManager.sceneCount;i++){var scene=SceneManager.GetSceneAt(i);if(scene.isDirty)EditorSceneManager.SaveScene(scene,Folder+"BeforeEquipmentCheck-"+i+".unity",true);}
            stage=-2;sawBubble=sawMonologue=sawReaction=false;spokenLines.Clear();runtimeErrors=0;
            SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
        }
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try
        {
            double now=EditorApplication.timeSinceStartup;
            if(stage==-2){stage=-1;next=now+2;return;}
            if(now<next)return;
            if(stage==-1)
            {
                ManualSaveStore.UseValidationDirectory(Path.GetFullPath(Folder+"equipment-slots-"+Guid.NewGuid().ToString("N")));
                Application.logMessageReceived+=Observe;
                expectedLines=ConversationDialogue.GetGreetingLines(Resources.Load<TextAsset>("Dialogue/Garbage/garbage_pursuit_monologue"),int.MaxValue);
                Require(expectedLines.Length>0,"Merged equipment dialogue exists");
                SaveGameSession.BeginNewGame();stage=0;next=now+.2;return;
            }
            if(stage==0){deadline=now+70;SceneManager.LoadScene("Market");stage=1;next=now+1;return;}
            Require(now<deadline,"Timeout at stage "+stage);if(now<next)return;
            if(stage==1)
            {
                player=UnityEngine.Object.FindFirstObjectByType<PlayerMovement2D>();flow=UnityEngine.Object.FindFirstObjectByType<StorySceneFlow>();if(player==null||flow==null)return;
                dialogue=(ConversationDialogue)Read(flow,"dialogue");if(dialogue==null)return;
                if(dialogue.IsPlaying){Call(dialogue,"RequestAdvance");return;}
                if((string)Read(flow,"currentGuide")!="garbage_search"||SceneFadeTransition.IsRunning)return;
                var point=UnityEngine.Object.FindObjectsByType<StoryInteraction2D>(FindObjectsSortMode.None).FirstOrDefault(x=>x.name=="Interaction");if(point==null)return;
                ui=player.GetComponent<InventoryUI>();point.GetComponent<Interactable2D>().Interact(player.gameObject);
                dialogue.LineRevealCompleted+=RecordLine;
                typeof(PlayerMovement2D).GetField("horizontalInput",Flags).SetValue(player,1f);
                typeof(PlayerMovement2D).GetField("jumpRequested",Flags).SetValue(player,true);
                stage=2;next=now+.2;return;
            }
            if(stage==2)
            {
                Require(ui.EquipmentTutorialActive && (string)Read(flow,"currentGuide")=="equipment_tutorial","Pickup directly starts inventory/equipment tutorial");
                Require(!dialogue.IsPlaying,"No dialogue between pickup and equip");
                var inventory=player.GetComponent<PlayerInventory>();
                var pen=Resources.Load<InventoryItemDefinition>("Story/OldBallpointPen");
                Require(Enumerable.Range(0,inventory.QuestCapacity).Any(i=>inventory.GetQuestSlot(i).Item==pen),"Pickup retains the ballpoint pen reward");
                Require(Mathf.Abs(player.GetComponent<Rigidbody2D>().linearVelocity.x)<.01f,"Movement blocked before inventory opens");
                Require(!UnityEngine.Object.FindObjectsByType<AutomaticNpcBubble>(FindObjectsSortMode.None).Any(x=>x.IsPlaying),"No pursuit before equip");
                ui.OpenInventory();Require(ui.IsOpen,"Inventory remains available during tutorial");stage=3;next=now+.8;return;
            }
            if(stage==3){ui.CloseForStoryDialogue();Require(!ui.IsOpen&&ui.EquipmentTutorialActive,"Close before equip keeps tutorial active");stage=4;next=now+.7;return;}
            if(stage==4)
            {
                var hint=ui.GetComponent<InventoryOpeningHint>();
                Require(hint!=null && (GameObject)Read(hint,"overlay")!=null && !(bool)Read(hint,"dismissing"),"Closing without equip restarts inventory emphasis");
                ui.OpenInventory();Require(ui.IsOpen,"Inventory reopens from retry hint");stage=5;next=now+.8;return;
            }
            if(stage==5)
            {
                Require(!dialogue.IsPlaying && spokenLines.Count==0 && !player.GetComponents<AutomaticNpcBubble>().Any(x=>x.IsPlaying),"No reaction or monologue before cloak equip");
                var inventory=player.GetComponent<PlayerInventory>();var cloak=Resources.Load<InventoryItemDefinition>("Story/TutorialGear");
                int slot=Enumerable.Range(0,inventory.Capacity).First(i=>inventory.GetSlot(i).Item==cloak);
                Require(inventory.EquipFromSlot(slot),"Equip cloak");stage=6;return;
            }
            if(stage==6)
            {
                var reaction=player.GetComponents<AutomaticNpcBubble>().FirstOrDefault(x=>x.PlayerReaction&&x.IsPlaying);
                if(reaction!=null)
                {
                    sawReaction=true;Require(!ui.IsOpen && !dialogue.IsPlaying && spokenLines.Count==0,"Equip closes inventory, then question appears before dialogue");
                    Require(player.GetComponent<PlayerControlLock>().IsLocked,"Question keeps player locked");
                    ui.OpenInventory();Require(!ui.IsOpen,"Question blocks reopening inventory");return;
                }
                var bubble=flow.GetComponent<AutomaticNpcBubble>();
                if(bubble!=null&&bubble.IsPlaying)
                {
                    sawBubble=true;Require(sawReaction && spokenLines.Count>0,"Pursuit shout follows question and merged opening dialogue");Require(!ui.IsOpen,"Inventory closes before shout");
                    Require(player.GetComponent<PlayerControlLock>().IsLocked,"Shout locks player");
                    ui.OpenInventory();Require(!ui.IsOpen,"Shout blocks inventory key path");return;
                }
                if(dialogue.IsPlaying)
                {
                    Require(sawReaction,"Question precedes merged monologue");sawMonologue=true;
                    Require(player.GetComponent<PlayerControlLock>().IsLocked,"Monologue keeps player lock");
                    ui.OpenInventory();Require(!ui.IsOpen,"Monologue blocks inventory");
                    Call(dialogue,"RequestAdvance");return;
                }
                if(!sawMonologue)return;
                if(player.GetComponent<PlayerControlLock>().IsLocked)return;
                Require(spokenLines.SequenceEqual(expectedLines),"All merged JSON lines play once in authored order");
                Require(sawBubble,"Existing pursuit shout cue still runs");
                Require((bool)Read(flow,"ready") && (string)Read(flow,"currentGuide")=="garbage_exit","Portal objective follows the complete merged dialogue");
                Require(!ui.EquipmentTutorialActive,"Tutorial releases movement gate");
                ui.OpenInventory();Require(ui.IsOpen,"Inventory unlocks after monologue");
                foreach(var name in new[]{"Forest/cutscene_conversation","Forest_4/selena","Garbage/garbage_pursuit_shout","Market/quest_offer","Hideout/base_upgrade_tutorial"})
                    Require(Resources.Load<TextAsset>("Dialogue/"+name)!=null,"Map resource resolves: "+name);
                ui.CloseForStoryDialogue();
                var resetCloak=Resources.Load<InventoryItemDefinition>("Story/TutorialGear");
                player.GetComponent<PlayerInventory>().Unequip(resetCloak.Slot);
                var bag=player.GetComponent<PlayerInventory>();
                int slot=Enumerable.Range(0,bag.Capacity).First(i=>bag.GetSlot(i).Item==resetCloak);
                Require(bag.EquipFromSlot(slot),"Cloak can be equipped again after tutorial");stage=7;next=now+.7;return;
            }
            if(stage==7)
            {
                Require(!dialogue.IsPlaying && !player.GetComponents<AutomaticNpcBubble>().Any(x=>x.IsPlaying),"Normal re-equip does not replay the question or dialogue");
                Require(runtimeErrors==0,"No runtime errors");
                Finish("PASS Unity "+Application.unityVersion+"; pickup -> equipment tutorial without question/dialogue; inventory retry works; equip -> question -> merged JSON dialogue with authored pursuit cue; continuous input lock until completion; portal/inventory unlock; re-equip does not replay events.");
            }
        }
        catch(Exception e){Finish("FAIL\n"+e);Debug.LogException(e);}
    }
}
#endif
