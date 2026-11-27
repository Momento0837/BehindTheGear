using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BehindTheGear.HideOut
{
    // Scene-authored XY artwork; only the props explicitly registered as upgrades are toggled.
    public sealed class HideOutSpriteEnvironment : MonoBehaviour
    {
        [Serializable]
        public sealed class Area
        {
            public Facility facility;
            public Transform root;
            public Transform iconAnchor;
            public Transform focusAnchor;
            public Sprite icon;
            [Min(.1f)] public float focusSize = 3f;
        }

        [Serializable]
        public sealed class UpgradeProp
        {
            public Facility facility;
            public GameObject target;
            [Min(1)] public int requiredLevel = 1;
        }

        public Area[] areas = Array.Empty<Area>();
        public UpgradeProp[] upgradeProps = Array.Empty<UpgradeProp>();
        public Bounds worldBounds = new(Vector3.zero, new Vector3(20, 12, 1));
        [Min(0)] public float dropPixels = 10f;
        [Min(.05f)] public float dropDuration = .4f;
        [Min(.1f)] public float minimumCameraSize = 1.5f;
        private readonly Dictionary<GameObject, Vector3> restPositions = new();
        private readonly Dictionary<GameObject, Coroutine> animations = new();
        private readonly HashSet<GameObject> pending = new();
        private Camera viewCamera;
        private bool waitingForFade;

        public void Initialize(Camera camera)
        {
            viewCamera = camera;
            foreach (var prop in upgradeProps)
                if (prop?.target != null && !restPositions.ContainsKey(prop.target))
                    restPositions.Add(prop.target, prop.target.transform.localPosition);
        }

        public Area Find(Facility facility)
        {
            foreach (var area in areas)
                if (area != null && area.facility == facility) return area;
            return null;
        }

        public float OverviewSize(Camera camera)
            => Mathf.Max(worldBounds.extents.y * 1.22f,
                worldBounds.extents.x / Mathf.Max(.1f, camera.aspect) * 1.12f, minimumCameraSize);

        public Vector3 OverviewPosition(Camera camera)
            => new(worldBounds.center.x, worldBounds.center.y, camera.transform.position.z);

        public void ApplyLevels(int[] levels)
        {
            foreach (var prop in upgradeProps)
            {
                if (prop?.target == null) continue;
                bool visible = levels != null && (int)prop.facility < levels.Length
                    && levels[(int)prop.facility] >= prop.requiredLevel;
                bool show = visible && (!pending.Contains(prop.target) || animations.ContainsKey(prop.target));
                if (prop.target.activeSelf != show) prop.target.SetActive(show);
                if (!visible)
                {
                    pending.Remove(prop.target);
                    StopAnimation(prop.target);
                }
            }
        }

        public void PlayUpgradeFeedback(Facility facility, int level)
        {
            foreach (var prop in upgradeProps)
            {
                if (prop?.target == null || prop.facility != facility || prop.requiredLevel != level) continue;
                prop.target.SetActive(true);
                pending.Add(prop.target);
                if (!isActiveAndEnabled || SceneFadeTransition.IsRunning) prop.target.SetActive(false);
            }
            PlayPending();
        }

        private void OnEnable() => PlayPending();

        private void OnDisable()
        {
            // Facility-room travel suspends this root. Resume reveals when the base becomes visible again.
            foreach (var pair in animations)
            {
                if (pair.Key == null) continue;
                pending.Add(pair.Key);
                if (restPositions.TryGetValue(pair.Key, out var rest)) pair.Key.transform.localPosition = rest;
                pair.Key.SetActive(false);
            }
            StopAllCoroutines();
            animations.Clear();
            waitingForFade = false;
        }

        private void PlayPending()
        {
            if (!isActiveAndEnabled || viewCamera == null || !Application.isPlaying) return;
            if (pending.Count == 0) return;
            if (SceneFadeTransition.IsRunning)
            {
                if (!waitingForFade) StartCoroutine(AfterFade());
                return;
            }
            foreach (var target in new List<GameObject>(pending))
            {
                if (target == null) continue;
                target.SetActive(true);
                if (!target.activeInHierarchy) continue;
                StopAnimation(target);
                animations[target] = StartCoroutine(Drop(target));
            }
        }

        private IEnumerator AfterFade()
        {
            waitingForFade = true;
            while (SceneFadeTransition.IsRunning) yield return null;
            waitingForFade = false;
            PlayPending();
        }

        private void StopAnimation(GameObject target)
        {
            if (animations.TryGetValue(target, out var routine))
            {
                if (routine != null) StopCoroutine(routine);
                animations.Remove(target);
            }
            if (restPositions.TryGetValue(target, out var rest)) target.transform.localPosition = rest;
        }

        private IEnumerator Drop(GameObject target)
        {
            if (!restPositions.TryGetValue(target, out var rest)) yield break;
            Vector3 worldOffset = viewCamera.transform.up * (2f * viewCamera.orthographicSize
                / Mathf.Max(1, viewCamera.pixelHeight) * dropPixels);
            Vector3 offset = target.transform.parent != null
                ? target.transform.parent.InverseTransformVector(worldOffset) : worldOffset;
            float elapsed = 0;
            while (elapsed < dropDuration && target != null)
            {
                float t = Mathf.Clamp01(elapsed / dropDuration);
                target.transform.localPosition = rest + offset * (1f - t) * (1f - t) * (1f - t);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            if (target != null) target.transform.localPosition = rest;
            pending.Remove(target);
            animations.Remove(target);
        }
    }
}
