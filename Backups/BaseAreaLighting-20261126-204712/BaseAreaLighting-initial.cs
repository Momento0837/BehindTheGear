using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BehindTheGear.HideOut
{
    // Lights are authored outside the Base root. Own their intensity only; keep position/color/radius intact.
    [DefaultExecutionOrder(1100), DisallowMultipleComponent]
    public sealed class BaseAreaLighting : MonoBehaviour
    {
        private sealed class Binding
        {
            public Light2D light;
            public Facility facility;
            public float authoredIntensity, progress, entrance;
            public bool unlocked;
        }
        private readonly List<Binding> lights = new();
        private HideOutSpriteEnvironment environment;

        public void Initialize(HideOutSpriteEnvironment owner)
        {
            if (environment != null || !Application.isPlaying) return;
            environment = owner;
            foreach (var root in owner.gameObject.scene.GetRootGameObjects())
                foreach (var light in root.GetComponentsInChildren<Light2D>(true))
                {
                    if (light.lightType == Light2D.LightType.Global || !TryFacility(light.name, out var facility)) continue;
                    lights.Add(new Binding { light = light, facility = facility, authoredIntensity = light.intensity,
                        entrance = owner.entrance != null && owner.entrance.enabled ? 0 : 1 });
                    light.intensity = 0;
                }
        }

        private static bool TryFacility(string name, out Facility facility)
        {
            string prefix = name.Split(new[] { '_', ' ', '-' }, StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
            switch (prefix)
            {
                case "heal": case "clinic": facility = Facility.Clinic; return true;
                case "bed": facility = Facility.Bed; return true;
                case "scarecrow": case "dummy": facility = Facility.Dummy; return true;
                case "storage": facility = Facility.Storage; return true;
                case "potion": case "potions": facility = Facility.Potions; return true;
                case "training": case "gym": facility = Facility.Gym; return true;
                case "meal": case "dining": facility = Facility.Dining; return true;
                case "crafting": case "workshop": facility = Facility.Workshop; return true;
                default: facility = default; return false;
            }
        }

        public void ApplyLocks(Func<Facility, bool> isLocked)
        {
            foreach (var binding in lights)
            {
                bool unlocked = !isLocked(binding.facility);
                if (binding.unlocked != unlocked) { binding.unlocked = unlocked; binding.progress = 0; }
                // Repeated UI refreshes never restart an unlock fade. A locked light is always exactly zero.
                if (!unlocked && binding.light != null) binding.light.intensity = 0;
            }
        }

        public void SetEntranceProgress(Facility facility, float progress)
        {
            foreach (var binding in lights)
                if (binding.facility == facility) binding.entrance = Mathf.Clamp01(progress);
        }

        public void CompleteEntrance()
        {
            foreach (var binding in lights) binding.entrance = 1;
        }

        private void LateUpdate()
        {
            if (environment == null) return;
            foreach (var binding in lights)
            {
                if (binding.light == null) continue;
                if (!binding.unlocked) { binding.light.intensity = 0; continue; }
                if (!SceneFadeTransition.IsRunning)
                    binding.progress = Mathf.Min(1, binding.progress + Mathf.Min(Time.unscaledDeltaTime, 1f / 30f)
                        / Mathf.Max(.05f, environment.areaLightFadeDuration));
                binding.light.intensity = binding.authoredIntensity * Mathf.SmoothStep(0, 1, binding.progress) * binding.entrance;
            }
        }

        private void OnEnable()
        {
            foreach (var binding in lights) binding.progress = 0;
        }
        private void OnDisable()
        {
            // These lights are separate scene roots, so hiding the map alone would leave them shining.
            foreach (var binding in lights)
                if (binding.light != null) binding.light.intensity = 0;
        }
        private void OnDestroy()
        {
            foreach (var binding in lights)
                if (binding.light != null) binding.light.intensity = binding.authoredIntensity;
        }
    }
}
