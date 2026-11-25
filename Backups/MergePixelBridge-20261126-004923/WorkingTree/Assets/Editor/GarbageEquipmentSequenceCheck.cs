#if UNITY_EDITOR
using System;
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
    static bool sawBubble,sawMonologue;
    static object Read(object o,string name)=>o.GetType().GetField(name,Flags).GetValue(o);
    static void Call(object o,string name)=>o.GetType().GetMethod(name,Flags).Invoke(o,null);
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static GarbageEquipmentSequenceCheck(){EditorApplication.update+=Tick;}
    static void Finish(string result){File.WriteAllText(Folder+"equipment-sequence-result.txt",result);SessionState.SetBool(Key,false);EditorApplication.isPlaying=false;stage=0;}
    static void Tick()
    {
        if(!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating&&File.Exists(Folder+"check-equipment-sequence"))
        {
            File.Delete(Folder+"check-equipment-sequence");if(Application.unityVersion!="6000.3.23f1")throw new Exception("Wrong Unity version");
            for(int i=0;i<SceneManager.sceneCount;i++){var scene=SceneManager.GetSceneAt(i);if(scene.isDirty)EditorSceneManager.SaveScene(scene,Folder+"BeforeEquipmentCheck-"+i+".unity",true);}
            stage=-2;sawBubble=sawMonologue=false;SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
        }
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try
        {
            double now=EditorApplication.timeSinceStartup;
            if(stage==-2){stage=-1;next=now+2;return;}
            if(now<next)return;
            if(stage==-1){SaveGameSession.BeginNewGame();stage=0;next=now+.2;return;}
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
                Require(ui.EquipmentTutorialActive,"Pickup starts inventory/equipment tutorial");
                typeof(PlayerMovement2D).GetField("horizontalInput",Flags).SetValue(player,1f);
                typeof(PlayerMovement2D).GetField("jumpRequested",Flags).SetValue(player,true);
                stage=2;next=now+.2;return;
            }
            if(stage==2)
            {
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
                var inventory=player.GetComponent<PlayerInventory>();var cloak=Resources.Load<InventoryItemDefinition>("Story/TutorialGear");
                int slot=Enumerable.Range(0,inventory.Capacity).First(i=>inventory.GetSlot(i).Item==cloak);
                Require(inventory.EquipFromSlot(slot),"Equip cloak");stage=6;return;
            }
            if(stage==6)
            {
                var bubble=flow.GetComponent<AutomaticNpcBubble>();
                if(bubble!=null&&bubble.IsPlaying)
                {
                    sawBubble=true;Require(!ui.IsOpen,"Inventory closes before shout");
                    Require(player.GetComponent<PlayerControlLock>().IsLocked,"Shout locks player");
                    ui.OpenInventory();Require(!ui.IsOpen,"Shout blocks inventory key path");return;
                }
                if(dialogue.IsPlaying)
                {
                    Require(sawBubble,"Shout precedes monologue");sawMonologue=true;
                    Require(player.GetComponent<PlayerControlLock>().IsLocked,"Monologue keeps player lock");
                    ui.OpenInventory();Require(!ui.IsOpen,"Monologue blocks inventory");
                    Require(ConversationDialogue.GetOpeningText(Resources.Load<TextAsset>("Dialogue/Garbage/garbage_pursuit_monologue"))=="쳇, 도망가야겠어.","Editable monologue text");
                    Call(dialogue,"RequestAdvance");return;
                }
                if(!sawMonologue)return;
                if(player.GetComponent<PlayerControlLock>().IsLocked)return;
                Require(!ui.EquipmentTutorialActive,"Tutorial releases movement gate");
                ui.OpenInventory();Require(ui.IsOpen,"Inventory unlocks after monologue");
                foreach(var name in new[]{"Forest/cutscene_conversation","Forest_4/selena","Garbage/garbage_pursuit_shout","Market/quest_offer","Neighborhood/neighborhood_conversation","Hideout/base_upgrade_tutorial"})
                    Require(Resources.Load<TextAsset>("Dialogue/"+name)!=null,"Map resource resolves: "+name);
                ui.CloseForStoryDialogue();
                var resetCloak=Resources.Load<InventoryItemDefinition>("Story/TutorialGear");
                player.GetComponent<PlayerInventory>().Unequip(resetCloak.Slot);
                ui.BeginEquipmentHint(resetCloak);
                Require(ui.EquipmentTutorialActive,"Tutorial restart active");
                ui.SetTutorialInventoryUnlocked(false);
                Require(!ui.EquipmentTutorialActive,"Restart/reset cancels movement gate");
                Finish("PASS Unity "+Application.unityVersion+"; pickup blocks movement but permits inventory; close without equip restarts emphasis; equip closes inventory and starts shout; shout -> JSON monologue hold continuous input lock; inventory unlocks after dialogue; map JSON resources resolve.");
            }
        }
        catch(Exception e){Finish("FAIL\n"+e);Debug.LogException(e);}
    }
}
#endif
