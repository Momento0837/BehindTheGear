using System;
using System.Collections.Generic;
using UnityEngine;

namespace BehindTheGear.HideOut
{
    // Temporary procedural diorama. Geometry is deliberately isolated so final room art can replace it.
    public sealed class HideOutIsometricEnvironment : MonoBehaviour
    {
        private readonly Dictionary<Facility, Transform> iconAnchors = new();
        private readonly List<Mesh> generatedMeshes = new();
        private Material geometryMaterial;

        public IReadOnlyDictionary<Facility, Transform> IconAnchors => iconAnchors;
        public GameObject[] StorageTiers { get; } = new GameObject[4];
        public HideOutDummy Dummy { get; private set; }
        public Transform DummyBodyAnchor { get; private set; }
        public Bounds WorldBounds { get; private set; }
        private readonly Dictionary<Facility, GameObject[]> expansionTiers = new();
        private readonly Dictionary<Facility, int> appliedArtTier = new();
        private readonly Dictionary<Facility, GameObject> replacementArt = new();
        private readonly Dictionary<Facility, Transform> visualRoots = new();
        private readonly Dictionary<Facility, GameObject> appliedPrefab = new();
        private readonly Dictionary<Facility, GameObject[]> stageFrames = new();
        private readonly Dictionary<Facility, GameObject> upgradeFeedback = new();
        public int UpgradeFeedbackCount { get; private set; }

        public void PlayUpgradeFeedback(Facility facility)
        {
            if (!Application.isPlaying || HideOutModel.IsExhibition(facility) || !upgradeFeedback.TryGetValue(facility, out var effect)) return;
            UpgradeFeedbackCount++;
            StartCoroutine(FlashUpgrade(effect));
        }

        private System.Collections.IEnumerator FlashUpgrade(GameObject effect)
        {
            effect.SetActive(true);
            float until = Time.unscaledTime + .55f;
            while (Time.unscaledTime < until) yield return null;
            effect.SetActive(false);
        }

        public void ApplyArt(HideOutSettings settings, int[] levels)
        {
            foreach (var pair in visualRoots)
            {
                var presentation = settings.FindPresentation(pair.Key);
                int tier = HideOutModel.IsExhibition(pair.Key) ? 0 : HideOutModel.Tier(levels[(int)pair.Key]);
                GameObject prefab = null;
                if (presentation != null)
                {
                    for (int i = Math.Min(tier, (presentation.tierVisualPrefabs?.Length ?? 0) - 1); i >= 0 && prefab == null; i--)
                        prefab = presentation.tierVisualPrefabs[i];
                    prefab ??= presentation.visualPrefab;
                }
                pair.Value.gameObject.SetActive(prefab == null);
                if (appliedArtTier.TryGetValue(pair.Key, out int oldTier) && oldTier == tier
                    && appliedPrefab.TryGetValue(pair.Key, out var oldPrefab) && oldPrefab == prefab) continue;
                if (replacementArt.TryGetValue(pair.Key, out GameObject previous) && previous != null)
                { previous.SetActive(false); if (Application.isPlaying) Destroy(previous); else DestroyImmediate(previous); }
                if (prefab != null)
                {
                    var instance = Instantiate(prefab, pair.Value.parent);
                    instance.name = "Registered Visual Root";
                    instance.transform.localPosition = presentation.visualPosition;
                    instance.transform.localRotation = Quaternion.Euler(presentation.visualEuler);
                    instance.transform.localScale = presentation.visualScale;
                    replacementArt[pair.Key] = instance;
                }
                appliedPrefab[pair.Key] = prefab;
                appliedArtTier[pair.Key] = tier;
            }
        }

        public void RefreshFacilityVisuals(int[] levels)
        {
            foreach (var pair in expansionTiers)
                for (int i = 0; i < pair.Value.Length; i++) pair.Value[i].SetActive(i <= HideOutModel.Tier(levels[(int)pair.Key]));
            for (int i = 0; i < StorageTiers.Length; i++) StorageTiers[i].SetActive(i <= HideOutModel.Tier(levels[(int)Facility.Storage]));
            foreach (var pair in stageFrames)
                for (int i = 0; i < pair.Value.Length; i++) pair.Value[i].SetActive(i == HideOutModel.Tier(levels[(int)pair.Key]));
        }

        public void Build(HideOutSettings settings = null)
        {
            Shader shader = Shader.Find("BehindTheGear/HideOut Vertex Color");
            if (shader == null) throw new InvalidOperationException("HideOut vertex-color shader is missing.");
            geometryMaterial = new Material(shader) { name = "HideOut Runtime Diorama Material" };

            BuildGroundShadow();
            BuildStorageRoom();
            BuildClinicRoom();
            BuildTrainingRoom();
            BuildExpansion(Facility.Bed, new Vector3(-7f, 0f, -5f));
            BuildExpansion(Facility.Potions, new Vector3(0f, 0f, -6f));
            BuildExpansion(Facility.Gym, new Vector3(7f, 0f, -5f));
            BuildExhibitionRoom(Facility.Dining, settings?.FindPresentation(Facility.Dining), new Vector3(-15f, 0f, 0f));
            BuildExhibitionRoom(Facility.Workshop, settings?.FindPresentation(Facility.Workshop), new Vector3(15f, 0f, 0f));
            BuildConnectors();
            BuildAnchors();
            BuildFunctionalTierDetails(Facility.Clinic, "Treatment Booth Room", new Vector3(-7.5f, 0f, 5.25f));
            BuildFunctionalTierDetails(Facility.Dummy, "Training Dummy Room", new Vector3(7.5f, 0f, 5.25f));
            SeparateVisualRoots();
            BuildStageFrames();
            CalculateWorldBounds();
        }

        private void BuildStageFrames()
        {
            foreach (var pair in visualRoots)
            {
                if (HideOutModel.IsExhibition(pair.Key)) continue;
                Vector3 center = iconAnchors[pair.Key].position; center.y = .09f;
                var frames = new GameObject[4];
                for (int tier = 0; tier < 4; tier++)
                {
                    frames[tier] = new GameObject("Visible Stage Frame " + tier);
                    frames[tier].transform.SetParent(pair.Value, false);
                    if (tier == 0) continue;
                    Color color = tier == 1 ? C("7c805b") : tier == 2 ? C("8aa795") : C("d5ac61");
                    FloorBorder(frames[tier].transform, center, color);
                    if (tier >= 2)
                        for (int side = -1; side <= 1; side += 2)
                            Box(frames[tier].transform, "Upgraded Support", center + new Vector3(side * 2f, .7f, .8f), new Vector3(.13f, 1.5f, .13f), color);
                    if (tier == 3) Box(frames[tier].transform, "Upgraded Warm Light Rail", center + new Vector3(0f, 2.4f, .8f), new Vector3(4.1f, .12f, .12f), color);
                }
                stageFrames[pair.Key] = frames;
                var flash = new GameObject("Upgrade Success Highlight"); flash.transform.SetParent(transform, false);
                FloorBorder(flash.transform, center + Vector3.up * .04f, C("ffe7a2"));
                flash.SetActive(false); upgradeFeedback[pair.Key] = flash;
            }
        }

        private void FloorBorder(Transform parent, Vector3 center, Color color)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Box(parent, "Stage Floor Edge", center + new Vector3(0f, 0f, side * 1.3f), new Vector3(4.1f, .055f, .1f), color);
                Box(parent, "Stage Floor Side", center + new Vector3(side * 2f, 0f, 0f), new Vector3(.1f, .055f, 2.6f), color);
            }
        }

        private void SeparateVisualRoots()
        {
            foreach (Facility facility in Enum.GetValues(typeof(Facility)))
            {
                string name = facility switch
                {
                    Facility.Storage => "Warehouse Room", Facility.Clinic => "Treatment Booth Room",
                    Facility.Dummy => "Training Dummy Room", _ => facility + " Room"
                };
                Transform room = transform.Find(name);
                if (room == null) continue;
                var root = new GameObject("Procedural Visual Root").transform;
                root.SetParent(room, false);
                var children = new List<Transform>();
                foreach (Transform child in room)
                    if (child != root && child.GetComponent<HideOutDummy>() == null) children.Add(child);
                foreach (Transform child in children) child.SetParent(root, true);
                visualRoots[facility] = root;
            }
        }

        private void BuildFunctionalTierDetails(Facility facility, string roomName, Vector3 center)
        {
            var tiers = new GameObject[4];
            for (int i = 0; i < 4; i++)
            {
                tiers[i] = new GameObject("Appearance Tier " + i);
                tiers[i].transform.SetParent(transform.Find(roomName), false);
                if (i == 1) Cabinet(tiers[i].transform, center + new Vector3(-1.8f, .6f, -1f), new Vector3(.65f, 1.2f, .6f), C("53645e"));
                if (i == 2) Box(tiers[i].transform, "Reinforced Wall Rail", center + new Vector3(0f, 1.7f, 2.5f), new Vector3(3.4f, .15f, .13f), C("89958a"));
                if (i == 3) Pendant(tiers[i].transform, center + new Vector3(1.9f, 2.55f, .4f), C("e4ae60"));
            }
            expansionTiers[facility] = tiers;
        }

        private void BuildExhibitionRoom(Facility facility, HideOutFacilityPresentation presentation, Vector3 fallback)
        {
            Vector3 center = presentation?.roomPosition ?? fallback;
            Vector2 size = presentation?.roomSize ?? new Vector2(7f, 6.8f);
            bool dining = facility == Facility.Dining;
            Transform room = Group(facility + " Room");
            TiledFloor(room, center, size, dining ? C("51483a") : C("35434a"), C("151d20"));
            Wall(room, "Back Service Wall", center + new Vector3(0f, 1.4f, size.y * .5f), new Vector3(size.x, 2.8f, .3f));
            Wall(room, "Outer Service Wall", center + new Vector3((dining ? -1f : 1f) * size.x * .5f, 1.4f, 0f), new Vector3(.3f, 2.8f, size.y));
            LowWall(room, "Cutaway Front Wall", center + new Vector3(0f, .35f, -size.y * .5f), new Vector3(size.x, .7f, .32f));
            Pipe(room, center + new Vector3(0f, 2.5f, 3.08f), new Vector3(6.6f, .14f, .14f));
            // Warm floor tiles suggest local work lights without changing the project's renderer.
            for (int x = -2; x <= 2; x++)
                for (int z = -2; z <= 2; z++)
                {
                    float light = 1f - Mathf.Clamp01(new Vector2(x, z).magnitude / 4f);
                    Color dark = dining ? C("494030") : C("354047");
                    Color bright = dining ? C("94724b") : C("657366");
                    Box(room, "Local Light Floor Tile", center + new Vector3(x, .035f, z), new Vector3(.96f, .035f, .96f), Color.Lerp(dark, bright, light));
                }
            if (dining) BuildDiningFurniture(room, center); else BuildWorkshopFurniture(room, center);
            float inward = dining ? 1f : -1f;
            Vector3 passage = center + new Vector3(inward * (size.x * .5f + .7f), 0f, 0f);
            TiledFloor(room, passage, new Vector2(1.8f, 2.1f), C("474536"), C("192022"));
            Box(room, "Passage Guide Light", passage + new Vector3(0f, .06f, -.95f), new Vector3(1.8f, .05f, .07f), C("bb8c49"));
            iconAnchors[facility] = Anchor(facility + "IconAnchor", center + (presentation?.iconOffset ?? new Vector3(0f, 3.4f, .2f)));
        }

        private void BuildDiningFurniture(Transform room, Vector3 center)
        {
            Box(room, "Communal Table Top", center + Vector3.up * 1.04f, new Vector3(3.6f, .22f, 1.55f), C("9b7046"));
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Box(room, "Table Leg", center + new Vector3(x * 1.4f, .48f, z * .52f), new Vector3(.14f, 1f, .14f), C("3e3428"));
                    Chair(room, center + new Vector3(x * 1.05f, 0f, z * 1.35f), z);
                    Cylinder(room, "Enamel Plate", center + new Vector3(x * .98f, 1.17f, z * .43f), .24f, .045f, C("c8c5ac"));
                    Cylinder(room, "Tin Cup", center + new Vector3(x * .55f, 1.28f, z * .5f), .09f, .23f, C("9daaa1"));
                }
            Rack(room, center + new Vector3(-1.8f, 1.1f, 2.65f), 2.2f, 2f, .65f);
            Cabinet(room, center + new Vector3(1.65f, .65f, 2.7f), new Vector3(2f, 1.3f, .7f), C("617064"));
            Cylinder(room, "Serving Pot", center + new Vector3(1.65f, 1.48f, 2.65f), .34f, .36f, C("a3aaa0"));
            Box(room, "Cloth Runner", center + new Vector3(0f, 1.17f, 0f), new Vector3(.5f, .025f, 1.45f), C("7a4436"));
            Pendant(room, center + new Vector3(-1.15f, 2.65f, .1f), C("edb76b"));
            Pendant(room, center + new Vector3(1.15f, 2.65f, .1f), C("edb76b"));
            Box(room, "Wall Notice Board", center + new Vector3(.3f, 1.9f, 3.15f), new Vector3(1.35f, .7f, .08f), C("746044"));
            Box(room, "Duty Roster Paper", center + new Vector3(.3f, 1.9f, 3.09f), new Vector3(.85f, .5f, .03f), C("b9ad84"));
        }

        private void Chair(Transform parent, Vector3 position, int direction)
        {
            Box(parent, "Chair Seat", position + Vector3.up * .55f, new Vector3(.7f, .13f, .65f), C("755a39"));
            Box(parent, "Chair Back", position + new Vector3(0f, .95f, direction * .28f), new Vector3(.7f, .7f, .12f), C("665235"));
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    Box(parent, "Chair Foot", position + new Vector3(x * .26f, .25f, z * .23f), new Vector3(.08f, .5f, .08f), C("303a36"));
        }

        private void BuildWorkshopFurniture(Transform room, Vector3 center)
        {
            Box(room, "Steel Workbench Top", center + new Vector3(0f, 1.05f, .6f), new Vector3(4.5f, .18f, 1.3f), C("84918b"));
            Cabinet(room, center + new Vector3(-1.45f, .48f, .6f), new Vector3(1.1f, .95f, 1.05f), C("36515a"));
            Cabinet(room, center + new Vector3(1.45f, .48f, .6f), new Vector3(1.1f, .95f, 1.05f), C("36515a"));
            Box(room, "Tool Pegboard", center + new Vector3(0f, 1.8f, 2.95f), new Vector3(3.6f, 1.5f, .14f), C("263b42"));
            for (int i = 0; i < 6; i++)
            {
                Box(room, "Hanging Tool Handle", center + new Vector3(-1.4f + i * .55f, 1.8f, 2.82f), new Vector3(.09f, .56f, .08f), C("b6b4a0"));
                Box(room, "Tool Head", center + new Vector3(-1.4f + i * .55f, 2.08f, 2.8f), new Vector3(.3f, .14f, .09f), C("899a9a"));
            }
            Box(room, "Bench Vice", center + new Vector3(1.5f, 1.3f, .15f), new Vector3(.42f, .4f, .45f), C("4b646a"));
            Box(room, "Blueprint Mat", center + new Vector3(-.4f, 1.155f, .6f), new Vector3(1.65f, .025f, .85f), C("335f73"));
            Box(room, "Blueprint Guide", center + new Vector3(-.4f, 1.175f, .6f), new Vector3(1.25f, .01f, .035f), C("93b9b4"));
            Rack(room, center + new Vector3(2.75f, 1.15f, 2.35f), 1f, 2.1f, .65f);
            Rack(room, center + new Vector3(-2.7f, 1.1f, 2.35f), 1f, 2f, .65f);
            // Decorative geometry only: no item, collider, receiver, or pickup component.
            for (int i = 0; i < 3; i++)
            {
                Box(room, "Display Weapon Stock", center + new Vector3(3f, .6f, -.9f - i * .5f), new Vector3(.16f, .7f, .18f), C("604c38"));
                Box(room, "Display Weapon Barrel", center + new Vector3(3f, 1.3f, -.9f - i * .5f), new Vector3(.09f, .8f, .09f), C("8c9897"));
            }
            HazardStrip(room, center + new Vector3(0f, .09f, -1.3f), 5f, true);
            Cylinder(room, "Parts Barrel", center + new Vector3(-2.75f, .55f, -2.25f), .42f, 1.1f, C("415658"));
            Pendant(room, center + new Vector3(0f, 2.7f, .75f), C("b9d0bd"));
        }

        private void Pendant(Transform parent, Vector3 position, Color light)
        {
            Box(parent, "Lamp Support", position + Vector3.up * .35f, new Vector3(.035f, .7f, .035f), C("394140"));
            Cylinder(parent, "Lamp Shade", position, .3f, .14f, C("46514b"));
            Cylinder(parent, "Lamp Diffuser", position + Vector3.down * .09f, .23f, .04f, light);
        }

        private void Cylinder(Transform parent, string name, Vector3 position, float radius, float height, Color color)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            obj.name = name; obj.transform.SetParent(parent, false);
            obj.transform.position = position; obj.transform.localScale = new Vector3(radius * 2f, height * .5f, radius * 2f);
            var collider = obj.GetComponent<Collider>(); collider.enabled = false;
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            var mesh = Instantiate(obj.GetComponent<MeshFilter>().sharedMesh);
            var normals = mesh.normals; var colors = new Color[normals.Length];
            for (int i = 0; i < colors.Length; i++) colors[i] = Lighten(color, .8f + Mathf.Max(0f, normals[i].y) * .4f);
            mesh.colors = colors; generatedMeshes.Add(mesh);
            obj.GetComponent<MeshFilter>().sharedMesh = mesh;
            obj.GetComponent<MeshRenderer>().sharedMaterial = geometryMaterial;
        }

        private void BuildExpansion(Facility facility, Vector3 center)
        {
            Transform room = Group(facility + " Room");
            Transform visual = new GameObject("Visual Root - replace artwork here").transform;
            visual.SetParent(room, false);
            TiledFloor(visual, center, new Vector2(5.5f, 3.5f), C("303632"), C("151b1b"));
            LowWall(visual, "Side Partition", center + new Vector3(-2.7f, .5f, 0f), new Vector3(.2f, 1f, 3.5f));
            var tiers = new GameObject[4];
            for (int i = 0; i < 4; i++)
            {
                tiers[i] = new GameObject("Appearance Tier " + i);
                tiers[i].transform.SetParent(visual, false);
                if (i > 0) Crate(tiers[i].transform, center + new Vector3(-1.9f + i * .9f, .25f, 1.1f), new Vector3(.7f, .5f, .6f), C("665332"));
            }
            expansionTiers[facility] = tiers;
            Transform initial = tiers[0].transform;
            if (facility == Facility.Bed)
            {
                Box(initial, "Rest Bed Frame", center + Vector3.up * .4f, new Vector3(3f, .65f, 1.2f), C("51402b"));
                Box(initial, "Rest Mattress", center + Vector3.up * .8f, new Vector3(2.9f, .2f, 1.1f), C("797e62"));
                Box(initial, "Rest Pillow", center + new Vector3(-1f, .95f, 0f), new Vector3(.6f, .15f, .9f), C("d0c9ad"));
            }
            else if (facility == Facility.Potions)
            {
                Rack(initial, center + Vector3.up, 3f, 1.8f, .7f);
                for (int i = 0; i < 5; i++) Box(initial, "Potion Bottle", center + new Vector3(-1f + i * .5f, 1.35f, -.25f), new Vector3(.2f, .45f, .2f), C("469f88"));
            }
            else
            {
                Box(initial, "Workout Mat", center + Vector3.up * .07f, new Vector3(3.8f, .1f, 2.4f), C("283934"));
                Box(initial, "Workout Bench", center + Vector3.up * .5f, new Vector3(2.2f, .3f, .8f), C("71523a"));
                Box(initial, "Barbell", center + new Vector3(0f, 1.3f, .7f), new Vector3(3.5f, .13f, .13f), C("889493"));
                for (int i = -1; i <= 1; i += 2) Box(initial, "Barbell Weight", center + new Vector3(i * 1.4f, 1.3f, .7f), new Vector3(.4f, .8f, .8f), C("222c2c"));
            }
            iconAnchors[facility] = Anchor(facility + "IconAnchor", center + Vector3.up * 2.7f);
        }

        private void CalculateWorldBounds()
        {
            var renderers = GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0) { WorldBounds = new Bounds(Vector3.zero, new Vector3(24f, 4f, 14f)); return; }
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            WorldBounds = bounds;
        }

        private void BuildGroundShadow()
        {
            Box(transform, "Bunker Foundation", new Vector3(0f, -.55f, 2.2f), new Vector3(24f, .7f, 13.5f),
                C("07090a"));
            Box(transform, "Lower Blackout", new Vector3(0f, -.35f, -5.4f), new Vector3(28f, .25f, 3f),
                C("030405"));
        }

        private void BuildStorageRoom()
        {
            Transform room = Group("Warehouse Room");
            TiledFloor(room, Vector3.zero, new Vector2(10.5f, 7.2f), C("353631"), C("1c201f"));
            Wall(room, "Warehouse Left Wall", new Vector3(-5.25f, 1.45f, .15f), new Vector3(.38f, 3f, 7.5f));
            Wall(room, "Warehouse Back Wall A", new Vector3(-3.85f, 1.45f, 3.55f), new Vector3(2.8f, 3f, .38f));
            Wall(room, "Warehouse Back Wall B", new Vector3(0f, 1.45f, 3.55f), new Vector3(2.6f, 3f, .38f));
            Wall(room, "Warehouse Back Wall C", new Vector3(4.05f, 1.45f, 3.55f), new Vector3(2.4f, 3f, .38f));
            Wall(room, "Warehouse Right Wall", new Vector3(5.25f, 1.45f, -.6f), new Vector3(.38f, 3f, 5.8f));
            LowWall(room, "Warehouse Front Left", new Vector3(-3.9f, .55f, -3.55f), new Vector3(2.7f, 1.1f, .35f));
            LowWall(room, "Warehouse Front Right", new Vector3(3.9f, .55f, -3.55f), new Vector3(2.7f, 1.1f, .35f));

            Pipe(room, new Vector3(-5.02f, 2.45f, .4f), new Vector3(.18f, .18f, 5.8f));
            Pipe(room, new Vector3(0f, 2.48f, 3.32f), new Vector3(8.2f, .16f, .16f));
            HazardStrip(room, new Vector3(0f, .08f, -3.25f), 9.4f, true);

            for (int i = 0; i < StorageTiers.Length; i++)
            {
                StorageTiers[i] = new GameObject($"Storage Appearance Tier {i}");
                StorageTiers[i].transform.SetParent(room, false);
            }

            Transform initial = StorageTiers[0].transform;
            Rack(initial, new Vector3(-3.7f, 1.15f, 2.65f), 2.2f, 2.1f, .65f);
            Rack(initial, new Vector3(3.65f, 1.15f, 2.55f), 2.25f, 2.1f, .72f);
            Rack(initial, new Vector3(-.3f, 1.05f, 1.45f), 3.1f, 1.9f, .8f);
            Crate(initial, new Vector3(-3.6f, .4f, -.55f), new Vector3(1.15f, .75f, .9f), C("5b4527"));
            Crate(initial, new Vector3(3.55f, .48f, .1f), new Vector3(1.3f, .9f, 1f), C("364239"));
            Box(initial, "Work Table", new Vector3(.2f, .65f, -1.65f), new Vector3(3f, .18f, 1f), C("665038"));
            Box(initial, "Table Left Leg", new Vector3(-1f, .3f, -1.65f), new Vector3(.18f, .7f, .18f), C("282c2b"));
            Box(initial, "Table Right Leg", new Vector3(1.4f, .3f, -1.65f), new Vector3(.18f, .7f, .18f), C("282c2b"));
            Box(initial, "Warm Table Lamp", new Vector3(.95f, .98f, -1.55f), new Vector3(.25f, .45f, .25f), C("e7a342"));

            Transform tier2 = StorageTiers[1].transform;
            Pallet(tier2, new Vector3(2.2f, .18f, -2.45f));
            Crate(tier2, new Vector3(2.2f, .64f, -2.45f), new Vector3(1.55f, .9f, 1.2f), C("70512b"));
            Crate(tier2, new Vector3(-2.25f, .55f, -2.2f), new Vector3(1.3f, 1.05f, 1.1f), C("303b38"));

            Transform tier4 = StorageTiers[2].transform;
            Crate(tier4, new Vector3(4.25f, .65f, -2.4f), new Vector3(1.35f, 1.25f, 1.15f), C("26343c"));
            Crate(tier4, new Vector3(-4.3f, .62f, -2.25f), new Vector3(1.2f, 1.2f, 1.05f), C("4a5137"));

            Transform tier6 = StorageTiers[3].transform;
            Box(tier6, "Secured Cage Back", new Vector3(4.55f, 1.25f, .8f), new Vector3(.12f, 2.4f, 2.1f), C("8c6531"));
            Box(tier6, "Secured Cage Bar A", new Vector3(4.45f, 1.25f, -.05f), new Vector3(.25f, 2.4f, .12f), C("bd7f31"));
            Box(tier6, "Secured Cage Bar B", new Vector3(4.45f, 1.25f, 1.65f), new Vector3(.25f, 2.4f, .12f), C("bd7f31"));
            Box(tier6, "Secured Light", new Vector3(3.85f, 2.15f, 2.95f), new Vector3(.55f, .18f, .18f), C("f0ad42"));
        }

        private void BuildClinicRoom()
        {
            Transform room = Group("Treatment Booth Room");
            Vector3 center = new(-7.5f, 0f, 5.25f);
            TiledFloor(room, center, new Vector2(6.2f, 5.5f), C("303836"), C("19201f"));
            Wall(room, "Clinic Back Wall", new Vector3(-7.5f, 1.45f, 8f), new Vector3(6.3f, 3f, .38f));
            Wall(room, "Clinic Left Wall", new Vector3(-10.6f, 1.45f, 5.25f), new Vector3(.38f, 3f, 5.8f));
            Wall(room, "Clinic Right Return", new Vector3(-4.4f, 1.45f, 6.95f), new Vector3(.38f, 3f, 2.2f));
            LowWall(room, "Clinic Front Wall", new Vector3(-8.85f, .55f, 2.5f), new Vector3(3.5f, 1.1f, .35f));
            Pipe(room, new Vector3(-7.5f, 2.48f, 7.76f), new Vector3(5.4f, .16f, .16f));

            Box(room, "Treatment Bed Base", new Vector3(-7.35f, .42f, 4.85f), new Vector3(2.7f, .65f, 1.05f), C("415b58"));
            Box(room, "Treatment Mattress", new Vector3(-7.35f, .82f, 4.85f), new Vector3(2.62f, .22f, .98f), C("a9b5a7"));
            Box(room, "Treatment Pillow", new Vector3(-8.25f, 1f, 4.85f), new Vector3(.58f, .18f, .72f), C("d3d6c5"));
            Cabinet(room, new Vector3(-9.7f, .95f, 7.35f), new Vector3(1.1f, 1.9f, .55f), C("59645c"));
            Cabinet(room, new Vector3(-5.45f, .8f, 7.45f), new Vector3(1.4f, 1.6f, .5f), C("4d5953"));
            Rack(room, new Vector3(-8.05f, 1.2f, 7.42f), 1.7f, 2.2f, .48f);
            Box(room, "Clinic Curtain Rail", new Vector3(-5.2f, 2.2f, 4.85f), new Vector3(.12f, .12f, 2.8f), C("638b84"));
            Box(room, "Clinic Curtain", new Vector3(-5.25f, 1.2f, 4.85f), new Vector3(.12f, 1.9f, 2.55f), C("2f6a66"));
            Box(room, "Medical Cross Vertical", new Vector3(-7.5f, 1.75f, 7.78f), new Vector3(.38f, 1.05f, .12f), C("b93832"));
            Box(room, "Medical Cross Horizontal", new Vector3(-7.5f, 1.75f, 7.75f), new Vector3(1.05f, .38f, .12f), C("b93832"));
            Box(room, "Clinic Warm Lamp", new Vector3(-5.15f, 2.35f, 7.55f), new Vector3(.45f, .18f, .2f), C("e3a64a"));
        }

        private void BuildTrainingRoom()
        {
            Transform room = Group("Training Dummy Room");
            Vector3 center = new(7.5f, 0f, 5.25f);
            TiledFloor(room, center, new Vector2(6.2f, 5.5f), C("38332b"), C("201d19"));
            Wall(room, "Training Back Wall", new Vector3(7.5f, 1.45f, 8f), new Vector3(6.3f, 3f, .38f));
            Wall(room, "Training Right Wall", new Vector3(10.6f, 1.45f, 5.25f), new Vector3(.38f, 3f, 5.8f));
            Wall(room, "Training Left Return", new Vector3(4.4f, 1.45f, 6.95f), new Vector3(.38f, 3f, 2.2f));
            LowWall(room, "Training Front Wall", new Vector3(8.85f, .55f, 2.5f), new Vector3(3.5f, 1.1f, .35f));
            Pipe(room, new Vector3(7.5f, 2.48f, 7.76f), new Vector3(5.4f, .16f, .16f));

            Box(room, "Training Mat", new Vector3(7.35f, .08f, 5.05f), new Vector3(2.8f, .12f, 2.2f), C("3a2e21"));
            Box(room, "Dummy Base", new Vector3(7.35f, .22f, 5.1f), new Vector3(1.25f, .28f, 1.05f), C("5c3c1f"));
            Box(room, "Dummy Post", new Vector3(7.35f, 1.15f, 5.1f), new Vector3(.22f, 1.8f, .22f), C("6c4723"));
            Box(room, "Dummy Torso", new Vector3(7.35f, 1.62f, 5.1f), new Vector3(.9f, 1.15f, .55f), C("9a6d30"));
            Box(room, "Dummy Arms", new Vector3(7.35f, 1.78f, 5.1f), new Vector3(2.1f, .2f, .2f), C("775027"));
            Box(room, "Dummy Head", new Vector3(7.35f, 2.48f, 5.1f), new Vector3(.72f, .72f, .68f), C("aa7937"));
            Box(room, "Dummy Target", new Vector3(7.35f, 1.6f, 4.8f), new Vector3(.36f, .36f, .05f), C("b64232"));
            DummyBodyAnchor = Anchor("TrainingDummyBodyAnchor", new Vector3(7.35f, 2.8f, 5.1f));
            var receiver = new GameObject("Training Dummy Hit Receiver");
            receiver.transform.SetParent(room, false);
            receiver.transform.position = new Vector3(7.35f, 1.4f, 5.1f);
            Dummy = receiver.AddComponent<HideOutDummy>();

            Rack(room, new Vector3(9.55f, 1.25f, 7.45f), 1.45f, 2.2f, .5f);
            Box(room, "Practice Sword", new Vector3(9.35f, 1.45f, 7.05f), new Vector3(.12f, 1.65f, .12f), C("a7aaa4"), -12f);
            Crate(room, new Vector3(5.35f, .42f, 6.7f), new Vector3(1.05f, .8f, .9f), C("674822"));
            Crate(room, new Vector3(9.55f, .36f, 3.65f), new Vector3(.9f, .68f, .85f), C("4a3520"));
            Box(room, "Training Warm Lamp", new Vector3(5.2f, 2.35f, 7.55f), new Vector3(.45f, .18f, .2f), C("efa63b"));
        }

        private void BuildConnectors()
        {
            Transform root = Group("Bunker Connectors");
            TiledFloor(root, new Vector3(-4.8f, 0f, 3.35f), new Vector2(3.6f, 2.2f), C("373630"), C("1c201f"));
            TiledFloor(root, new Vector3(4.8f, 0f, 3.35f), new Vector2(3.6f, 2.2f), C("373630"), C("1c201f"));
            HazardStrip(root, new Vector3(-4.7f, .08f, 2.35f), 3f, true);
            HazardStrip(root, new Vector3(4.7f, .08f, 2.35f), 3f, true);
            Box(root, "Central Cable A", new Vector3(-.5f, .09f, -2.75f), new Vector3(7.4f, .08f, .09f), C("765a2c"), 4f);
            Box(root, "Central Cable B", new Vector3(.5f, .1f, -2.5f), new Vector3(6f, .06f, .08f), C("24565a"), -5f);
        }

        private void BuildAnchors()
        {
            iconAnchors[Facility.Clinic] = Anchor("TreatmentBoothIconAnchor", new Vector3(-7.5f, 3.35f, 5.25f));
            iconAnchors[Facility.Storage] = Anchor("WarehouseIconAnchor", new Vector3(0f, 3.5f, .25f));
            iconAnchors[Facility.Dummy] = Anchor("TrainingDummyIconAnchor", new Vector3(7.5f, 3.35f, 5.25f));
        }

        private Transform Group(string name)
        {
            var group = new GameObject(name).transform;
            group.SetParent(transform, false);
            return group;
        }

        private Transform Anchor(string name, Vector3 position)
        {
            var anchor = new GameObject(name).transform;
            anchor.SetParent(transform, false);
            anchor.position = position;
            return anchor;
        }

        private void TiledFloor(Transform parent, Vector3 center, Vector2 size, Color baseColor, Color seamColor)
        {
            Box(parent, "Floor Slab", center + Vector3.down * .08f, new Vector3(size.x, .18f, size.y), baseColor);
            for (float x = -size.x * .5f + 1f; x < size.x * .5f; x += 1f)
                Box(parent, "Floor Seam X", center + new Vector3(x, .025f, 0f), new Vector3(.035f, .025f, size.y - .12f), seamColor);
            for (float z = -size.y * .5f + 1f; z < size.y * .5f; z += 1f)
                Box(parent, "Floor Seam Z", center + new Vector3(0f, .026f, z), new Vector3(size.x - .12f, .025f, .035f), seamColor);
        }

        private void Wall(Transform parent, string name, Vector3 center, Vector3 size)
        {
            Box(parent, name, center, size, C("252b2b"));
            Vector3 capSize = size.x > size.z ? new Vector3(size.x + .08f, .16f, size.z + .08f) : new Vector3(size.x + .08f, .16f, size.z + .08f);
            Box(parent, name + " Cap", center + Vector3.up * (size.y * .5f + .07f), capSize, C("111616"));
        }

        private void LowWall(Transform parent, string name, Vector3 center, Vector3 size)
        {
            Box(parent, name, center, size, C("191e1e"));
            Box(parent, name + " Edge", center + Vector3.up * (size.y * .5f + .05f), new Vector3(size.x, .12f, size.z), C("343837"));
        }

        private void Pipe(Transform parent, Vector3 center, Vector3 size)
        {
            Box(parent, "Utility Pipe", center, size, C("394140"));
            Box(parent, "Pipe Highlight", center + new Vector3(0f, .06f, -.04f), size * .72f, C("58605b"));
        }

        private void HazardStrip(Transform parent, Vector3 center, float length, bool alongX)
        {
            int count = Mathf.Max(1, Mathf.FloorToInt(length / .45f));
            for (int i = 0; i < count; i++)
            {
                float offset = -length * .5f + (i + .5f) * length / count;
                Vector3 position = center + (alongX ? Vector3.right : Vector3.forward) * offset;
                Vector3 size = alongX ? new Vector3(length / count * .78f, .04f, .18f) : new Vector3(.18f, .04f, length / count * .78f);
                Box(parent, "Hazard Stripe", position, size, i % 2 == 0 ? C("bd7c25") : C("202222"), alongX ? -28f : 28f);
            }
        }

        private void Rack(Transform parent, Vector3 center, float width, float height, float depth)
        {
            Color frame = C("5b4b34");
            Color shelf = C("82613a");
            float x = width * .5f - .08f;
            float z = depth * .5f - .06f;
            Box(parent, "Rack Post", center + new Vector3(-x, 0f, -z), new Vector3(.12f, height, .12f), frame);
            Box(parent, "Rack Post", center + new Vector3(x, 0f, -z), new Vector3(.12f, height, .12f), frame);
            Box(parent, "Rack Post", center + new Vector3(-x, 0f, z), new Vector3(.12f, height, .12f), frame);
            Box(parent, "Rack Post", center + new Vector3(x, 0f, z), new Vector3(.12f, height, .12f), frame);
            for (int i = 0; i < 3; i++)
            {
                float y = -height * .42f + i * height * .4f;
                Box(parent, "Rack Shelf", center + new Vector3(0f, y, 0f), new Vector3(width, .12f, depth), shelf);
                if (i < 2)
                    Crate(parent, center + new Vector3((i == 0 ? -.22f : .2f) * width, y + .28f, 0f),
                        new Vector3(width * .38f, .46f, depth * .72f), i == 0 ? C("5a472b") : C("3d473d"));
            }
        }

        private void Cabinet(Transform parent, Vector3 center, Vector3 size, Color color)
        {
            Box(parent, "Cabinet", center, size, color);
            Box(parent, "Cabinet Split", center + new Vector3(0f, 0f, -size.z * .51f), new Vector3(.04f, size.y * .88f, .04f), C("252b29"));
            Box(parent, "Cabinet Handle", center + new Vector3(size.x * .18f, 0f, -size.z * .56f), new Vector3(.09f, .24f, .06f), C("b3ad91"));
        }

        private void Crate(Transform parent, Vector3 center, Vector3 size, Color color)
        {
            Box(parent, "Cargo Crate", center, size, color);
            Box(parent, "Crate Brace", center + new Vector3(0f, 0f, -size.z * .51f), new Vector3(.12f, size.y * .92f, .05f), Darken(color, .55f));
            Box(parent, "Crate Lid", center + Vector3.up * (size.y * .52f), new Vector3(size.x * 1.04f, .09f, size.z * 1.04f), Lighten(color, 1.18f));
        }

        private void Pallet(Transform parent, Vector3 center)
        {
            for (int i = -2; i <= 2; i++)
                Box(parent, "Pallet Slat", center + new Vector3(i * .3f, 0f, 0f), new Vector3(.22f, .14f, 1.35f), C("76502b"));
            Box(parent, "Pallet Foot", center + new Vector3(-.55f, -.12f, 0f), new Vector3(.16f, .18f, 1.1f), C("3e2a18"));
            Box(parent, "Pallet Foot", center + new Vector3(.55f, -.12f, 0f), new Vector3(.16f, .18f, 1.1f), C("3e2a18"));
        }

        private GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Color color, float yaw = 0f)
        {
            var gameObject = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.position = center;
            gameObject.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            gameObject.transform.localScale = size;
            Mesh mesh = CreateCubeMesh(color);
            generatedMeshes.Add(mesh);
            gameObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            gameObject.GetComponent<MeshRenderer>().sharedMaterial = geometryMaterial;
            return gameObject;
        }

        private static Color C(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color color);
            return color;
        }

        private static Color Lighten(Color color, float amount)
            => new(Mathf.Clamp01(color.r * amount), Mathf.Clamp01(color.g * amount), Mathf.Clamp01(color.b * amount), 1f);

        private static Color Darken(Color color, float amount) => Lighten(color, amount);

        private static Mesh CreateCubeMesh(Color color)
        {
            Vector3[] vertices =
            {
                // front (-z)
                new(-.5f,-.5f,-.5f), new(.5f,-.5f,-.5f), new(.5f,.5f,-.5f), new(-.5f,.5f,-.5f),
                // back (+z)
                new(.5f,-.5f,.5f), new(-.5f,-.5f,.5f), new(-.5f,.5f,.5f), new(.5f,.5f,.5f),
                // left (-x)
                new(-.5f,-.5f,.5f), new(-.5f,-.5f,-.5f), new(-.5f,.5f,-.5f), new(-.5f,.5f,.5f),
                // right (+x)
                new(.5f,-.5f,-.5f), new(.5f,-.5f,.5f), new(.5f,.5f,.5f), new(.5f,.5f,-.5f),
                // top
                new(-.5f,.5f,-.5f), new(.5f,.5f,-.5f), new(.5f,.5f,.5f), new(-.5f,.5f,.5f),
                // bottom
                new(-.5f,-.5f,.5f), new(.5f,-.5f,.5f), new(.5f,-.5f,-.5f), new(-.5f,-.5f,-.5f)
            };
            int[] triangles =
            {
                0,2,1, 0,3,2, 4,6,5, 4,7,6, 8,10,9, 8,11,10,
                12,14,13, 12,15,14, 16,18,17, 16,19,18, 20,22,21, 20,23,22
            };
            Color[] colors = new Color[24];
            Fill(colors, 0, Lighten(color, .95f));
            Fill(colors, 4, Darken(color, .62f));
            Fill(colors, 8, Darken(color, .72f));
            Fill(colors, 12, Darken(color, .82f));
            Fill(colors, 16, Lighten(color, 1.22f));
            Fill(colors, 20, Darken(color, .42f));
            var mesh = new Mesh { name = "HideOut Runtime Cuboid" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.colors = colors;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void Fill(Color[] colors, int start, Color color)
        {
            for (int i = 0; i < 4; i++) colors[start + i] = color;
        }

        private void OnDestroy()
        {
            foreach (Mesh mesh in generatedMeshes) if (mesh != null) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
            if (geometryMaterial != null) { if (Application.isPlaying) Destroy(geometryMaterial); else DestroyImmediate(geometryMaterial); }
        }
    }
}
