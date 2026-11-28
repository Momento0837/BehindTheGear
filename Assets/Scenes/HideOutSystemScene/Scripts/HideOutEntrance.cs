using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BehindTheGear.HideOut
{
    [RequireComponent(typeof(Interactable2D), typeof(BoxCollider2D))]
    public sealed class HideOutEntrance : MonoBehaviour
    {
        public HideOutSettings settings;
        public Transform returnPoint;
        private bool entering;
        private void Awake()
        {
            if (gameObject.scene.name == "Forest") gameObject.SetActive(false);
        }
        public void Enter()
        {
            if (entering || settings == null) return;
            var player = PlayerTargetRegistry.Current != null ? PlayerTargetRegistry.Current.GetComponent<PersistentPlayer2D>() : FindFirstObjectByType<PersistentPlayer2D>();
            if (player == null) { Debug.LogWarning("[HideOut] PersistentPlayer2D가 있는 플레이어가 필요합니다.", this); return; }
            if (player.GetComponent<PlayerInventory>() == null || player.GetComponent<PlayerHealth>() == null)
            { Debug.LogWarning("[HideOut] 기존 플레이어에 PlayerInventory / PlayerHealth를 연결하세요.", this); return; }
            var bridge = player.GetComponent<HideOutPlayerBridge>();
            if (bridge == null) bridge = player.gameObject.AddComponent<HideOutPlayerBridge>();
            try { bridge.Initialize(settings, false); }
            catch (Exception e) { Debug.LogError(e.Message, this); return; }
            var position = player.transform.position;
            Scene origin = gameObject.scene;
            entering = true;
            // Player hosts the coroutine because this entrance's scene roots are suspended.
            bridge.StartCoroutine(HideOutTravel.Enter(bridge, origin, position, () => entering = false));
        }
        private void Reset() => GetComponent<BoxCollider2D>().isTrigger = true;
    }

    // Facility rooms entered from the base (ScareCrow training, Treatment booth) share one additive travel path.
    // Each room places the live player, hosts the base's existing facility panel and undoes its changes on leave.
    public interface IHideOutFacilityRoom
    {
        HideOutPlayerBridge Player { get; }
        void Bind(HideOutPlayerBridge bridge, HideOutScene view);
        void PrepareToLeave();
        void RestorePlayerBounds();
    }

    // Additive visit keeps live quests, NPCs and every original scene object intact.
    // Existing PersistentPlayer2D continues to own the player; no second persistent manager.
    public static class HideOutTravel
    {
        private static Scene origin;
        private static Vector3 returnPosition;
        private static readonly List<GameObject> suspended = new();
        private static bool travelling;
        private static HideOutScene trainingBase;
        private static Vector3 trainingReturnPosition;
        private static readonly List<GameObject> trainingSuspended = new();
        public static bool CanReturn => origin.IsValid() && origin.isLoaded;
        public static bool IsTravelling => travelling;
        public static bool IsVisiting => CanReturn;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => ClearSession();
        public static void ClearSession()
        {
            origin = default; suspended.Clear(); travelling = false;
            trainingBase = null; trainingSuspended.Clear();
        }
        public static IEnumerator EnterTraining(HideOutScene view)
            => EnterRoom<ScareCrowTrainingScene>(view, ScareCrowTrainingScene.ScenePath, "ScareCrowScene", "훈련장 컨트롤러가 없습니다.", null);

        // Clinic: same camera-first, fade-through entry as the training room. If TreatmentScene has not been converted
        // yet (Tools > HideOut > Build Treatment room), fall back to the existing clinic panel in the base.
        public static IEnumerator EnterTreatment(HideOutScene view)
            => EnterRoom<TreatmentRoomScene>(view, TreatmentRoomScene.ScenePath, "TreatmentScene",
                "치료실 컨트롤러가 없습니다. Tools > HideOut > Build Treatment room을 실행하세요.",
                v => v.OpenFacilityPanelFallback(Facility.Clinic));

        // Bed: same entry into BedScene (save / bed upgrade / Selena). Until Tools > HideOut > Create Bed room has made
        // the scene, the bed keeps working with the existing in-base panel (save button included).
        public static IEnumerator EnterBedroom(HideOutScene view)
            => EnterRoom<BedRoomScene>(view, BedRoomScene.ScenePath, "BedScene",
                "침실 컨트롤러가 없습니다. Tools > HideOut > Create Bed room을 실행하세요.",
                v => v.OpenFacilityPanelFallback(Facility.Bed));

        public static IEnumerator ExitTraining(ScareCrowTrainingScene training) => ExitRoom(training);
        public static IEnumerator ExitTreatment(TreatmentRoomScene room) => ExitRoom(room);
        public static IEnumerator ExitBedroom(BedRoomScene room) => ExitRoom(room);

        private static IEnumerator EnterRoom<T>(HideOutScene view, string scenePath, string sceneLabel, string missingController,
            Action<HideOutScene> onUnavailable) where T : MonoBehaviour, IHideOutFacilityRoom
        {
            if (travelling || trainingBase != null || view == null) yield break;
            travelling = true;
            bool entered = false;
            try
            {
                yield return SceneFadeTransition.FadeThrough(
                    EnterRoomWhileBlack<T>(view, scenePath, sceneLabel, missingController, () => entered = true),
                    view.trainingFadeDuration, fadeMusic: false);
            }
            finally
            {
                travelling = false;
                if (view != null) view.FinishTrainingTransition();
            }
            if (!entered && view != null) onUnavailable?.Invoke(view);
        }

        private static IEnumerator EnterRoomWhileBlack<T>(HideOutScene view, string scenePath, string sceneLabel, string missingController,
            Action entered) where T : MonoBehaviour, IHideOutFacilityRoom
        {
            var bridge = view.TrainingPlayer;
            AsyncOperation operation = null;
            try
            {
#if UNITY_EDITOR
                operation = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath,
                    new LoadSceneParameters(LoadSceneMode.Additive));
#else
                if (!Application.CanStreamedLevelBeLoaded(scenePath))
                    throw new InvalidOperationException($"빌드 Scene List에 {sceneLabel}을 등록하세요.");
                operation = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
#endif
            }
            catch (Exception e) { bridge.SetMessage(e.Message); Debug.LogWarning(e.Message, view); }
            if (operation == null) yield break;
            while (!operation.isDone) yield return null;
            var scene = SceneManager.GetSceneByPath(scenePath);
            T room = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.TryGetComponent(out T candidate)) { room = candidate; break; }
            if (room == null)
            {
                bridge.SetMessage(missingController);
                yield return SceneManager.UnloadSceneAsync(scene);
                yield break;
            }
            trainingBase = view;
            trainingReturnPosition = bridge.transform.position;
            view.ReleasePlayer();
            foreach (var root in view.gameObject.scene.GetRootGameObjects())
                if (root.activeSelf && root != bridge.transform.root.gameObject)
                { trainingSuspended.Add(root); root.SetActive(false); }
            SceneManager.SetActiveScene(scene);
            room.Bind(bridge, view);
            entered();
        }

        private static IEnumerator ExitRoom<T>(T room) where T : MonoBehaviour, IHideOutFacilityRoom
        {
            if (travelling || room == null) yield break;
            var view = trainingBase;
            var bridge = room.Player;
            if (view == null)
            {
                // Direct room-scene Play still uses the existing persistent player on return (LoadScene fades itself).
                travelling = true;
                room.PrepareToLeave();
                if (bridge != null) SceneFadeTransition.LoadScene(bridge.Settings.scenePath, .4f, fadeMusic: false);
                travelling = false; yield break;
            }
            travelling = true;
            try
            {
                // Fade out over the room, swap back to the retained base while opaque, then fade the base in.
                // (Previously the room was unloaded with no fade, so returning cut straight to the base.)
                yield return SceneFadeTransition.FadeThrough(ExitRoomWhileBlack(room, view, bridge),
                    view.trainingFadeDuration, fadeMusic: false);
            }
            finally { travelling = false; }
        }

        private static IEnumerator ExitRoomWhileBlack<T>(T room, HideOutScene view, HideOutPlayerBridge bridge)
            where T : MonoBehaviour, IHideOutFacilityRoom
        {
            room.PrepareToLeave();
            // Freeze with the existing base policy before yielding to asynchronous unloading.
            // Otherwise gravity can move the restored player for a frame before the base resumes.
            view.SuspendPlayer();
            SceneManager.SetActiveScene(view.gameObject.scene);
            if (bridge != null)
            {
                bridge.transform.position = trainingReturnPosition;
                var body = bridge.GetComponent<Rigidbody2D>();
                if (body != null) { body.linearVelocity = Vector2.zero; body.angularVelocity = 0; }
            }
            room.RestorePlayerBounds();
            var operation = SceneManager.UnloadSceneAsync(room.gameObject.scene);
            while (operation != null && !operation.isDone) yield return null;
            foreach (var root in trainingSuspended) if (root != null) root.SetActive(true);
            trainingSuspended.Clear();
            trainingBase = null;
        }
        public static IEnumerator Enter(HideOutPlayerBridge bridge, Scene from, Vector3 position, Action finished)
        {
            yield return SceneFadeTransition.FadeThrough(EnterWhileBlack(bridge, from, position, finished));
        }
        private static IEnumerator EnterWhileBlack(HideOutPlayerBridge bridge, Scene from, Vector3 position, Action finished)
        {
            if (travelling) { finished(); yield break; }
            travelling = true;
            origin = from; returnPosition = position;
            bridge.State.fieldReturn = FieldReturnState.Capture(bridge.gameObject, from);
            bridge.State.fieldReturn.position = position;
            // Selena's "returned" line names the map shown by the field location HUD (read before it is dismissed).
            string selenaLastMap = string.Empty;
            foreach (var hud in UnityEngine.Object.FindObjectsByType<LocationHUD>(FindObjectsSortMode.None))
                if (!string.IsNullOrEmpty(hud.CurrentLocation)) selenaLastMap = hud.CurrentLocation;
            foreach (var hud in UnityEngine.Object.FindObjectsByType<LocationHUD>(FindObjectsSortMode.None)) hud.Dismiss();
            var travelLock = bridge.GetComponent<PlayerControlLock>();
            travelLock?.Acquire();
            // Suspend before loading so the HideOut creates its own input/camera context.
            foreach (var root in origin.GetRootGameObjects())
                if (root.activeSelf && root != bridge.transform.root.gameObject)
                {
                    // Disabling the flow's GameObject would permanently stop its quest coroutines.
                    if (root.GetComponent<StorySceneFlow>() != null) continue;
                    suspended.Add(root);
                    if (root.GetComponentInChildren<Camera>() == null) root.SetActive(false);
                }
            AsyncOperation operation = null;
            try
            {
#if UNITY_EDITOR
                operation = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(bridge.Settings.scenePath, new LoadSceneParameters(LoadSceneMode.Additive));
#else
                if (!Application.CanStreamedLevelBeLoaded(bridge.Settings.scenePath)) throw new InvalidOperationException("빌드의 Scene List에 HideOut 씬을 등록해야 합니다.");
                operation = SceneManager.LoadSceneAsync(bridge.Settings.scenePath, LoadSceneMode.Additive);
#endif
            }
            catch (Exception e) { bridge.SetMessage(e.Message); Debug.LogWarning(e.Message, bridge); }
            if (operation == null)
            {
                foreach (var root in suspended) if (root != null) root.SetActive(true);
                suspended.Clear(); origin = default; travelling = false; travelLock?.Release(); finished(); yield break;
            }
            while (!operation.isDone) yield return null;
            Scene hideout = SceneManager.GetSceneByPath(bridge.Settings.scenePath);
            SceneManager.SetActiveScene(hideout);
            yield return null;
            foreach (var root in origin.GetRootGameObjects())
            {
                var flow = root.GetComponent<StorySceneFlow>();
                // Complete the entrance objective only after the destination has loaded successfully.
                if (flow != null) flow.CompleteHideoutEntranceQuest(bridge.State.fieldReturn);
                if (flow != null && flow.MoveSelenaIntoBase() && bridge.State.fieldReturn?.story != null)
                    bridge.State.fieldReturn.story.hasSelena = false;
            }
            foreach (var root in suspended) if (root != null) root.SetActive(false);
            travelLock?.Release();
            var baseView = UnityEngine.Object.FindFirstObjectByType<HideOutScene>();
            if (baseView != null) { baseView.ReleasePlayer(); baseView.SuspendPlayer(); }
            RoomDropCache.Clear();
            SelenaGameState.MarkReturnedFromField(bridge, selenaLastMap);
            travelling = false; finished();
        }
        public static IEnumerator Exit(HideOutPlayerBridge bridge, Scene hideout)
        {
            yield return SceneFadeTransition.FadeThrough(ExitWhileBlack(bridge, hideout));
        }
        private static IEnumerator ExitWhileBlack(HideOutPlayerBridge bridge, Scene hideout)
        {
            if (travelling || !CanReturn) { bridge.SetMessage("직접 실행한 테스트 씬입니다. 복귀할 플레이 구간이 없습니다."); yield break; }
            travelling = true;
            var view = UnityEngine.Object.FindFirstObjectByType<HideOutScene>();
            if (view != null)
            {
                view.ReleasePlayer();
                // Remove the HideOut EventSystem before restoring the origin's input context.
                view.gameObject.SetActive(false);
            }
            SceneManager.SetActiveScene(origin);
            bridge.transform.position = returnPosition;
            var body = bridge.GetComponent<Rigidbody2D>();
            if (body != null) { body.position = returnPosition; body.linearVelocity = Vector2.zero; body.angularVelocity = 0; }
            foreach (var root in suspended) if (root != null) root.SetActive(true);
            if (bridge.State.fieldReturn != null)
            {
                bridge.State.fieldReturn.RestorePlacement(bridge.gameObject, origin);
                var flow = Array.Find(UnityEngine.Object.FindObjectsByType<StorySceneFlow>(FindObjectsSortMode.None), f => f.gameObject.scene == origin);
                bridge.State.fieldReturn.RestoreQuest(flow != null ? (object)flow : typeof(HideOutTravel));
            }
            foreach (var hud in UnityEngine.Object.FindObjectsByType<LocationHUD>(FindObjectsSortMode.None)) hud.Dismiss();
            suspended.Clear();
            var operation = SceneManager.UnloadSceneAsync(hideout);
            while (operation != null && !operation.isDone) yield return null;
            origin = default; travelling = false;
        }
    }
}
