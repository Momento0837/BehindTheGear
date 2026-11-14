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
            var bridge = player.GetComponent<HideOutPlayerBridge>() ?? player.gameObject.AddComponent<HideOutPlayerBridge>();
            try { bridge.Initialize(settings, false); }
            catch (Exception e) { Debug.LogError(e.Message, this); return; }
            var position = returnPoint != null ? returnPoint.position : player.transform.position;
            Scene origin = gameObject.scene;
            entering = true;
            // Player hosts the coroutine because this entrance's scene roots are suspended.
            bridge.StartCoroutine(HideOutTravel.Enter(bridge, origin, position, () => entering = false));
        }
        private void Reset() => GetComponent<BoxCollider2D>().isTrigger = true;
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
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => ClearSession();
        public static void ClearSession()
        {
            origin = default; suspended.Clear(); travelling = false;
            trainingBase = null; trainingSuspended.Clear();
        }
        public static IEnumerator EnterTraining(HideOutScene view)
        {
            if (travelling || trainingBase != null || view == null) yield break;
            travelling = true;
            try
            {
                yield return SceneFadeTransition.FadeThrough(EnterTrainingWhileBlack(view), view.trainingFadeDuration);
            }
            finally
            {
                travelling = false;
                if (view != null) view.FinishTrainingTransition();
            }
        }

        private static IEnumerator EnterTrainingWhileBlack(HideOutScene view)
        {
            var bridge = view.TrainingPlayer;
            AsyncOperation operation = null;
            try
            {
#if UNITY_EDITOR
                operation = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScareCrowTrainingScene.ScenePath,
                    new LoadSceneParameters(LoadSceneMode.Additive));
#else
                if (!Application.CanStreamedLevelBeLoaded(ScareCrowTrainingScene.ScenePath))
                    throw new InvalidOperationException("빌드 Scene List에 ScareCrowScene을 등록하세요.");
                operation = SceneManager.LoadSceneAsync(ScareCrowTrainingScene.ScenePath, LoadSceneMode.Additive);
#endif
            }
            catch (Exception e) { bridge.SetMessage(e.Message); Debug.LogWarning(e.Message, view); }
            if (operation == null) yield break;
            while (!operation.isDone) yield return null;
            var scene = SceneManager.GetSceneByPath(ScareCrowTrainingScene.ScenePath);
            ScareCrowTrainingScene training = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.TryGetComponent(out ScareCrowTrainingScene candidate)) { training = candidate; break; }
            if (training == null)
            {
                bridge.SetMessage("훈련장 컨트롤러가 없습니다.");
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
            training.Bind(bridge, view);
        }

        public static IEnumerator ExitTraining(ScareCrowTrainingScene training)
        {
            if (travelling || training == null) yield break;
            travelling = true;
            var view = trainingBase;
            var bridge = training.Player;
            training.PrepareToLeave();
            if (view == null)
            {
                // Direct training-scene Play still uses the existing persistent player on return.
                if (bridge != null) SceneFadeTransition.LoadScene(bridge.Settings.scenePath, .4f);
                travelling = false; yield break;
            }
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
            training.RestorePlayerBounds();
            var operation = SceneManager.UnloadSceneAsync(training.gameObject.scene);
            while (operation != null && !operation.isDone) yield return null;
            foreach (var root in trainingSuspended) if (root != null) root.SetActive(true);
            trainingSuspended.Clear();
            trainingBase = null;
            travelling = false;
        }
        public static IEnumerator Enter(HideOutPlayerBridge bridge, Scene from, Vector3 position, Action finished)
        {
            if (travelling) { finished(); yield break; }
            travelling = true;
            origin = from; returnPosition = position;
            foreach (var hud in UnityEngine.Object.FindObjectsByType<LocationHUD>(FindObjectsSortMode.None)) hud.Dismiss();
            var travelLock = bridge.GetComponent<PlayerControlLock>();
            travelLock?.Acquire();
            // Suspend before loading so the HideOut creates its own input/camera context.
            foreach (var root in origin.GetRootGameObjects())
                if (root.activeSelf && root != bridge.transform.root.gameObject)
                {
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
            foreach (var root in suspended) if (root != null) root.SetActive(false);
            travelLock?.Release();
            var baseView = UnityEngine.Object.FindFirstObjectByType<HideOutScene>();
            if (baseView != null) { baseView.ReleasePlayer(); baseView.SuspendPlayer(); }
            RoomDropCache.Clear();
            travelling = false; finished();
        }
        public static IEnumerator Exit(HideOutPlayerBridge bridge, Scene hideout)
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
            if (body != null) { body.linearVelocity = Vector2.zero; body.angularVelocity = 0; }
            foreach (var root in suspended) if (root != null) root.SetActive(true);
            foreach (var hud in UnityEngine.Object.FindObjectsByType<LocationHUD>(FindObjectsSortMode.None)) hud.Dismiss();
            suspended.Clear();
            var operation = SceneManager.UnloadSceneAsync(hideout);
            while (operation != null && !operation.isDone) yield return null;
            origin = default; travelling = false;
        }
    }
}
