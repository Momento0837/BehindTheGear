using System;
using System.Collections;
using UnityEngine;

// Explicitly prepared under the portal's black cover, never on ordinary scene loads.
public sealed class PortalArrival : IDisposable
{
    readonly PlayerMovement2D player;
    readonly Rigidbody2D body;
    readonly PlayerControlLock controls;
    readonly InventoryUI inventory;
    readonly PlayerMapBounds2D bounds;
    readonly PlayerCameraFollow2D follow;
    readonly bool inventoryEnabled, boundsEnabled, simulated, followEnabled;
    readonly Vector3 destination;
    bool disposed;
    public static bool IsEntering { get; private set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => IsEntering = false;

    public static PortalArrival Prepare(PlayerMovement2D player)
    {
        if (player == null || Camera.main == null || StorySceneFlow.GarbageArrivalOwnsMotion || CutsceneVideoSequence.IsAnyVisible) return null;
        return new PortalArrival(player, Camera.main);
    }
    PortalArrival(PlayerMovement2D actor, Camera camera)
    {
        player = actor; body = actor.GetComponent<Rigidbody2D>();
        controls = actor.GetComponent<PlayerControlLock>(); inventory = actor.GetComponent<InventoryUI>();
        bounds = actor.GetComponent<PlayerMapBounds2D>(); follow = camera.GetComponent<PlayerCameraFollow2D>();
        inventoryEnabled = inventory != null && inventory.enabled;
        boundsEnabled = bounds != null && bounds.enabled;
        simulated = body != null && body.simulated;
        followEnabled = follow != null && follow.enabled;
        IsEntering = true;
        controls?.Acquire();
        if (inventory != null) inventory.enabled = false;
        if (bounds != null) bounds.enabled = false;
        if (body != null) body.simulated = true;
        actor.SettleOnGround(40);
        destination = actor.transform.position;
        if (follow != null) follow.enabled = false;
        camera.ResetWorldToCameraMatrix();
        bool fromLeft = bounds != null && bounds.HasHorizontalBounds
            ? destination.x <= (bounds.HorizontalBounds.x + bounds.HorizontalBounds.y) * .5f
            : camera.WorldToViewportPoint(destination).x <= .5f;
        var ray = camera.ViewportPointToRay(new Vector3(fromLeft ? -.12f : 1.12f, .5f, 0));
        var plane = new Plane(Vector3.forward, destination);
        float startX = destination.x + (fromLeft ? -8 : 8);
        if (plane.Raycast(ray, out float distance)) startX = ray.GetPoint(distance).x;
        startX = fromLeft ? Mathf.Min(startX, destination.x - 2) : Mathf.Max(startX, destination.x + 2);
        var start = new Vector3(startX, destination.y, destination.z);
        if (body != null) { body.linearVelocity = Vector2.zero; body.simulated = false; body.position = start; }
        actor.transform.position = start;
        actor.SetCinematicWalking(true);
        actor.GetComponent<PlayerSpriteFacing>()?.FaceAttack(fromLeft ? 1 : -1);
        Physics2D.SyncTransforms();
    }
    public IEnumerator Enter()
    {
        try
        {
            while (SceneFadeTransition.IsRunning) yield return null;
            while (player != null && Mathf.Abs(player.transform.position.x - destination.x) > .001f)
            {
                var position = player.transform.position;
                position.x = Mathf.MoveTowards(position.x, destination.x, Mathf.Max(.1f, player.RunningSpeed) * Time.unscaledDeltaTime);
                player.transform.position = position;
                if (body != null) body.position = position;
                yield return null;
            }
        }
        finally { Dispose(); }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        IsEntering = false;
        if (player != null) player.SetCinematicWalking(false);
        if (body != null) { body.linearVelocity = Vector2.zero; body.simulated = simulated; }
        if (bounds != null) bounds.enabled = boundsEnabled;
        if (follow != null) follow.enabled = followEnabled;
        if (inventory != null) inventory.enabled = inventoryEnabled;
        controls?.Release();
    }
}
