using System;
using System.Collections;
using UnityEngine;

public static class PortalDeparture
{
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
        bool followEnabled = follow != null && follow.enabled;
        controls?.Acquire();
        try
        {
            if (inventory != null) inventory.enabled = false;
            if (bounds != null) bounds.enabled = false;
            if (follow != null) follow.enabled = false;
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
            if (follow != null) follow.enabled = followEnabled;
            if (controls != null) controls.Release();
        }
    }
}
