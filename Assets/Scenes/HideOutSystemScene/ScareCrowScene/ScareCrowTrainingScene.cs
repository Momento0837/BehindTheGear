using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BehindTheGear.HideOut
{
    [DefaultExecutionOrder(-75)]
    public sealed class ScareCrowTrainingScene : MonoBehaviour
    {
        public const string ScenePath = "Assets/Scenes/HideOutSystemScene/ScareCrowScene/ScareCrowScene.unity";
        public static bool IsActive { get; private set; }
        public HideOutSettings settings;
        public GameObject playerPrefab;
        public Transform playerStart;
        public HideOutDummy dummy;
        public Camera trainingCamera;
        public Canvas trainingCanvas;
        public GameObject inputContext;
        public TMP_Text weaponLabel;
        public TMP_Text lastHitLabel;
        public Vector2 horizontalBounds = new(-12.5f, 12.5f);
        public HideOutPlayerBridge Player { get; private set; }
        private HideOutScene baseView;
        private PlayerMapBounds2D bounds;
        private bool previousBoundsEnabled;
        private Vector2 previousBounds;
        private bool bound, leaving, ownsUpgradeLock;
        private PlayerControlLock controlLock;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState() => IsActive = false;

        private void Awake()
        {
            trainingCamera.gameObject.SetActive(false);
            trainingCanvas.gameObject.SetActive(false);
            inputContext.SetActive(false);
        }

        private void Start()
        {
            // Additive entry is bound by HideOutTravel after loading. Direct Play uses the same rig.
            if (bound || FindFirstObjectByType<HideOutScene>() != null) return;
            var inventory = FindFirstObjectByType<PlayerInventory>();
            if (inventory == null)
            {
                var rig = Instantiate(playerPrefab);
                inventory = rig.GetComponentInChildren<PlayerInventory>();
            }
            var bridge = inventory.GetComponent<HideOutPlayerBridge>() ?? inventory.gameObject.AddComponent<HideOutPlayerBridge>();
            bridge.Initialize(settings, true);
            Bind(bridge, null);
        }

        public void Bind(HideOutPlayerBridge bridge, HideOutScene view)
        {
            if (bound) return;
            Player = bridge; baseView = view; bound = true; IsActive = true;
            controlLock = Player.GetComponent<PlayerControlLock>();
            bounds = Player.GetComponent<PlayerMapBounds2D>();
            if (bounds != null)
            {
                previousBoundsEnabled = bounds.HasHorizontalBounds;
                previousBounds = bounds.HorizontalBounds;
                // Temporarily clear old map bounds before placing the live player.
                bounds.ClearHorizontalBounds();
            }
            Player.transform.position = playerStart.position;
            if (Player.TryGetComponent<Rigidbody2D>(out var body))
            { body.linearVelocity = Vector2.zero; body.angularVelocity = 0; }
            bounds?.SetHorizontalBounds(horizontalBounds);
            trainingCamera.gameObject.SetActive(true);
            trainingCamera.GetComponent<PlayerCameraFollow2D>().SetStoryBounds(horizontalBounds);
            trainingCanvas.gameObject.SetActive(true);
            inputContext.SetActive(true);
            dummy.Hit += RefreshLastHit;
            Player.Inventory.Changed += RefreshWeapon;
            RefreshWeapon();
            lastHitLabel.text = "최근 타격  —";
        }

        private void Update()
        {
            if (!bound || leaving) return;
            // Existing inventory consumes ESC first; facility panel then return travel follow it.
            if (ownsUpgradeLock && (baseView == null || !baseView.TrainingUpgradeVisible)) CloseUpgrade();
            if (Keyboard.current?.escapeKey.wasPressedThisFrame != true || InventoryUI.EscapeHandledThisFrame) return;
            if (ownsUpgradeLock) CloseUpgrade();
            else ReturnToBase();
        }

        private void RefreshWeapon()
        {
            var weapon = Player.EquippedWeapon;
            weaponLabel.text = weapon != null ? "장착 무기  " + weapon.DisplayName : "장착된 무기가 없습니다.";
        }

        private void RefreshLastHit() => lastHitLabel.text = $"최근 타격  {dummy.LastDamage}  ·  "
            + (dummy.LastHit.IsCritical ? "크리티컬" : "일반 타격");

        public void OpenUpgrade()
        {
            if (!bound || leaving || ownsUpgradeLock) return;
            PlayerInputReader.SuppressPointerForCurrentFrame();
            if (baseView == null)
            {
                var panelHost = new GameObject("Existing facility panel host", typeof(RectTransform));
                panelHost.transform.SetParent(transform, false);
                panelHost.SetActive(false);
                baseView = panelHost.AddComponent<HideOutScene>();
                baseView.InitializeTrainingPanel(Player, dummy);
            }
            controlLock?.Acquire(); ownsUpgradeLock = true;
            baseView.OpenTrainingUpgrade(trainingCanvas.transform, dummy);
        }

        public void CloseUpgrade()
        {
            baseView?.CloseTrainingUpgrade();
            if (ownsUpgradeLock) controlLock?.Release();
            ownsUpgradeLock = false;
            PlayerInputReader.SuppressPointerForCurrentFrame();
        }

        public void ReturnToBase()
        {
            if (!bound || leaving || SceneFadeTransition.IsRunning) return;
            leaving = true;
            Player.StartCoroutine(HideOutTravel.ExitTraining(this));
        }

        public void PrepareToLeave()
        {
            CloseUpgrade();
            if (bounds != null)
            {
                bounds.ClearHorizontalBounds();
                // Restore after position is reset by travel, avoiding an old-map clamp here.
            }
            trainingCamera.gameObject.SetActive(false);
            trainingCanvas.gameObject.SetActive(false);
            inputContext.SetActive(false);
        }

        public void RestorePlayerBounds()
        {
            if (bounds == null) return;
            if (previousBoundsEnabled) bounds.SetHorizontalBounds(previousBounds);
            else bounds.ClearHorizontalBounds();
        }

        private void OnDestroy()
        {
            if (!bound) return;
            CloseUpgrade();
            if (dummy != null) dummy.Hit -= RefreshLastHit;
            if (Player != null) Player.Inventory.Changed -= RefreshWeapon;
            IsActive = false;
        }
    }
}
