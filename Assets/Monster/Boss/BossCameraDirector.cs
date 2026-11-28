using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Camera work for the boss fight, without changing PlayerCameraFollow2D:
///  - Shake(): a short, decaying jolt. While the follow camera is in charge the offset is removed again before the
///    follow script runs, so it never feeds into the follow smoothing.
///  - Hold(): takes the camera over (the follow script is switched off), slides it to a point and then keeps it on
///    TrackX (the boss in phase 1, between boss and player in phase 2). Release() hands it back.
///  - ZoomTo(): widens the view for phase 2 (orthographic size, or the field of view of a perspective camera, which is
///    what Forest uses). A Pixel Perfect Camera would force the lens back, so it is switched off meanwhile. The forest art is only as tall as the normal view; the camera
///    background is made black meanwhile, so the extra height reads as wide-screen bars.
/// </summary>
[DefaultExecutionOrder(10000)] // after PlayerCameraFollow2D (1000) and PlayerMapBounds2D (500)
public sealed class BossCameraDirector : MonoBehaviour
{
    private Camera view;
    private PlayerCameraFollow2D follow;
    private bool holding, followWasEnabled, sliding;
    private Vector3 held;          // camera position without shake, while holding
    private Vector3 applied;       // shake offset added last frame, while not holding
    private float amplitude, shakeEnds, shakeSeconds;

    private PixelPerfectCamera pixelPerfect;
    private bool zoomOwned, pixelPerfectWasEnabled;
    private float baseSize, baseFov, zoom = 1f, zoomFrom = 1f, zoomTarget = 1f, zoomSeconds = 1f, zoomAge;
    private Color baseBackground;

    public bool Holding => holding;
    /// <summary>While the camera is held, where its centre should be (null = stay put).</summary>
    public Func<float> TrackX { get; set; }
    /// <summary>Centre of the view, without shake.</summary>
    public float CentreX => holding ? held.x : view != null ? view.transform.position.x - applied.x : 0f;
    /// <summary>Half the width of what the camera shows right now (world units).</summary>
    public float HalfWidth => view != null ? VisibleHalfHeight(view) * view.aspect : 8.9f;

    /// <summary>Half the height the camera shows on the gameplay plane z = 0. Forest and Market play through a
    /// perspective camera (FOV 20); its orthographicSize field is a leftover and does not describe the view.</summary>
    public static float VisibleHalfHeight(Camera camera)
    {
        if (camera == null) return 5f;
        if (camera.orthographic) return camera.orthographicSize;
        float distance = Mathf.Abs(camera.transform.position.z);
        return distance * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
    }
    public float Zoom => zoom;

    public static BossCameraDirector Create(BossGoliath boss)
    {
        var director = boss.gameObject.AddComponent<BossCameraDirector>();
        director.view = Camera.main;
        if (director.view != null) director.follow = director.view.GetComponent<PlayerCameraFollow2D>();
        return director;
    }

    /// <summary>World-unit jolt that fades out over `seconds` (real time). A stronger shake replaces a weaker one.</summary>
    public void Shake(float worldUnits, float seconds)
    {
        if (worldUnits < amplitude * Mathf.Clamp01((shakeEnds - Time.unscaledTime) / Mathf.Max(.01f, shakeSeconds))) return;
        amplitude = worldUnits;
        shakeSeconds = Mathf.Max(.01f, seconds);
        shakeEnds = Time.unscaledTime + shakeSeconds;
    }

    /// <summary>Takes the camera and slides it to `worldX` over `seconds`; afterwards it follows TrackX until Release().</summary>
    public IEnumerator Hold(float worldX, float seconds)
    {
        if (view == null) yield break;
        if (!holding)
        {
            RemoveApplied();
            followWasEnabled = follow != null && follow.enabled;
            if (follow != null) follow.enabled = false;
            held = view.transform.position;
            holding = true;
        }
        float from = held.x;
        sliding = true;
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            float u = Mathf.Clamp01(t / seconds);
            held.x = Mathf.Lerp(from, worldX, u * u * (3f - 2f * u));
            yield return null;
        }
        held.x = worldX;
        sliding = false;
    }

    /// <summary>Widens (factor above 1) or restores (1) the view over `seconds` of real time.</summary>
    public void ZoomTo(float factor, float seconds)
    {
        if (view == null) return;
        factor = Mathf.Max(1f, factor);
        if (!zoomOwned)
        {
            if (Mathf.Approximately(factor, 1f)) return;
            zoomOwned = true;
            baseSize = view.orthographicSize;
            baseFov = view.fieldOfView;
            baseBackground = view.backgroundColor;
            view.backgroundColor = Color.black;
            pixelPerfect = view.GetComponent<PixelPerfectCamera>();
            pixelPerfectWasEnabled = pixelPerfect != null && pixelPerfect.enabled;
            if (pixelPerfect != null) pixelPerfect.enabled = false; // it would force the lens back every frame
        }
        zoomFrom = zoom; zoomTarget = factor; zoomAge = 0f; zoomSeconds = Mathf.Max(.01f, seconds);
    }

    /// <summary>Gives the camera back to the follow script (which eases back to the player by itself).</summary>
    public void Release()
    {
        RestoreLens();
        if (!holding) return;
        holding = false;
        if (view != null) view.transform.position = held;
        if (follow != null) follow.enabled = followWasEnabled;
    }

    private void RestoreLens()
    {
        if (!zoomOwned) return;
        zoomOwned = false;
        zoom = zoomFrom = zoomTarget = 1f;
        if (view == null) return;
        view.orthographicSize = baseSize;
        view.fieldOfView = baseFov;
        view.backgroundColor = baseBackground;
        if (pixelPerfect != null) pixelPerfect.enabled = pixelPerfectWasEnabled;
    }

    // Before any LateUpdate: take last frame's shake off again, so the follow script starts from its own position.
    private void Update() { if (!holding) RemoveApplied(); }

    private void LateUpdate()
    {
        if (view == null) return;
        if (zoomOwned)
        {
            zoomAge += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(zoomAge / zoomSeconds);
            zoom = Mathf.Lerp(zoomFrom, zoomTarget, u * u * (3f - 2f * u));
            // Same widening for either projection: an orthographic size, or the perspective angle whose half-tangent
            // grows by the zoom factor (so every depth layer widens by the same factor).
            if (view.orthographic) view.orthographicSize = baseSize * zoom;
            else view.fieldOfView = 2f * Mathf.Atan(Mathf.Tan(baseFov * .5f * Mathf.Deg2Rad) * zoom) * Mathf.Rad2Deg;
            if (u >= 1f && Mathf.Approximately(zoomTarget, 1f)) RestoreLens();
        }

        Vector3 offset = Vector3.zero;
        float left = shakeEnds - Time.unscaledTime;
        if (left > 0f && amplitude > 0f)
        {
            float strength = amplitude * left / shakeSeconds;
            offset = (Vector3)(UnityEngine.Random.insideUnitCircle * strength);
        }
        else amplitude = 0f;

        if (holding && !sliding && TrackX != null)
            held.x = Mathf.Lerp(held.x, TrackX(), 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime));
        if (holding) view.transform.position = held + offset;
        else if (offset != Vector3.zero) { view.transform.position += offset; applied = offset; }
    }

    private void RemoveApplied()
    {
        if (applied == Vector3.zero || view == null) return;
        view.transform.position -= applied;
        applied = Vector3.zero;
    }

    private void OnDestroy()
    {
        RemoveApplied();
        RestoreLens();
        if (holding && follow != null) follow.enabled = followWasEnabled; // never leave the scene without its camera
    }
}
