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
            if (!bridge.Change(s => { s.returnScene = origin.path; s.returnX = position.x; s.returnY = position.y; s.returnZ = position.z; return "기지 진입 정보 저장"; })) return;
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
        public static bool CanReturn => origin.IsValid() && origin.isLoaded;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { origin = default; suspended.Clear(); travelling = false; }
        public static IEnumerator Enter(HideOutPlayerBridge bridge, Scene from, Vector3 position, Action finished)
        {
            if (travelling) { finished(); yield break; }
            travelling = true;
            origin = from; returnPosition = position;
            // Suspend before loading so the HideOut creates its own input/camera context.
            foreach (var root in origin.GetRootGameObjects())
                if (root.activeSelf && root != bridge.transform.root.gameObject) { suspended.Add(root); root.SetActive(false); }
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
                suspended.Clear(); origin = default; travelling = false; finished(); yield break;
            }
            while (!operation.isDone) yield return null;
            Scene hideout = SceneManager.GetSceneByPath(bridge.Settings.scenePath);
            SceneManager.SetActiveScene(hideout);
            travelling = false; finished();
        }
        public static IEnumerator Exit(HideOutPlayerBridge bridge, Scene hideout)
        {
            if (travelling || !CanReturn) { bridge.SetMessage("직접 실행한 테스트 씬입니다. 복귀할 플레이 구간이 없습니다."); yield break; }
            if (!bridge.Save()) yield break;
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
            suspended.Clear();
            var operation = SceneManager.UnloadSceneAsync(hideout);
            while (operation != null && !operation.isDone) yield return null;
            origin = default; travelling = false;
        }
    }
}
