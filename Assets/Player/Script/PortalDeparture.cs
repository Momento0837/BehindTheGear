using System;
using System.Collections;
using UnityEngine;

public static class PortalDeparture
{
    private sealed class CameraSuspension
    {
        public readonly PlayerMovement2D Player;
        private readonly PlayerCameraFollow2D follow;
        private readonly bool wasEnabled;
        private bool released;

        public CameraSuspension(PlayerMovement2D player, PlayerCameraFollow2D cameraFollow)
        {
            Player = player;
            follow = cameraFollow;
            wasEnabled = follow != null && follow.enabled;
            activeCameraSuspension = this;
            if (follow != null) follow.enabled = false;
        }

        public void Release()
        {
            if (released) return;
            released = true;
            if (follow != null) follow.enabled = wasEnabled;
            if (ReferenceEquals(activeCameraSuspension, this)) activeCameraSuspension = null;
        }
    }

    private static CameraSuspension activeCameraSuspension;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => activeCameraSuspension = null;

    // Called under the black cover, before arrival captures and temporarily disables the camera.
    public static void ResumeCameraForArrival(PlayerMovement2D player)
    {
        if (activeCameraSuspension != null && activeCameraSuspension.Player == player)
            activeCameraSuspension.Release();
    }
    public static IEnumerator Travel(PlayerMovement2D player, ScenePortal2D portal, Func<IEnumerator> next)
    {
        var controls = player.GetComponent<PlayerControlLock>();
        var inventory = player.GetComponent<InventoryUI>();
        var bounds = player.GetComponent<PlayerMapBounds2D>();
        var body = player.GetComponent<Rigidbody2D>();
        var camera = Camera.main;
        var follow = camera != null ? camera.GetComponent<PlayerCameraFollow2D>() : null;
        bool inventoryEnabled = inventory != null && inventory.enabled;
        bool boundsEnabled = bounds != null && bounds.enabled;
        bool simulated = body != null && body.simulated;
        CameraSuspension cameraSuspension = null;
        controls?.Acquire();
        try
        {
            if (inventory != null) inventory.enabled = false;
            if (bounds != null) bounds.enabled = false;
            cameraSuspension = new CameraSuspension(player, follow);
            if (body != null) { body.linearVelocity = Vector2.zero; body.simulated = false; }
            int direction = portal.ExitDirection;
            player.SetCinematicWalking(true);
            player.GetComponent<PlayerSpriteFacing>()?.FaceAttack(direction);
            var position = player.transform.position;
            float targetX = position.x + direction * 8;
            if (camera != null)
                targetX = camera.ViewportToWorldPoint(new Vector3(direction < 0 ? -.15f : 1.15f, .5f,
                    Mathf.Abs(camera.transform.position.z - position.z))).x;
            while (direction * (targetX - position.x) > .01f)
            {
                position.x = Mathf.MoveTowards(position.x, targetX, player.RunningSpeed * Time.unscaledDeltaTime);
                player.transform.position = position;
                if (body != null) body.position = position;
                yield return null;
            }
            yield return next();
        }
        finally
        {
            // The new Garbage scene has already placed the player outside its camera.
            // Do not clamp it back inside or erase its launch velocity when this old portal finishes.
            if (!StorySceneFlow.GarbageArrivalOwnsMotion)
            {
                if (player != null) player.SetCinematicWalking(false);
                if (body != null) { body.simulated = simulated; body.linearVelocity = Vector2.zero; }
                if (bounds != null) bounds.enabled = boundsEnabled;
            }
            if (inventory != null) inventory.enabled = inventoryEnabled;
            cameraSuspension?.Release();
            if (controls != null) controls.Release();
        }
    }
}
