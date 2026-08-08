using System;
using System.Collections.Generic;
using Il2CppSpeed.Overworld;
using UnityEngine;

namespace WtcArchipelago;

/// <summary>
/// A manual "unstick" for the overworld car: freeze it and step it around by
/// hand, to reach somewhere the game will not let you reach on its own.
///
/// Built for a specific case -- a chest up a river where the car never enters
/// its swimming movement state, so it walks instead and cannot climb the ledge.
///
/// It deliberately does NOT try to force `OverworldPlayerMovementSwimming` on.
/// Poking a state machine from outside tends to leave it inconsistent, and the
/// movement states already implement `OnTeleport(position, direction)` -- so
/// moving the car through the game's own `OverworldPlayer.Teleport` lets the game
/// re-evaluate which state it should be in. We use the supported door.
///
/// Off until toggled, and every effect is reversible: the rigidbody is made
/// kinematic while active (so the car holds still instead of sliding off the
/// ledge you just placed it on) and restored on exit. Nothing is written to the
/// save.
/// </summary>
public static class OverworldNudge
{
    // Hold-to-move keys. Letters rather than the numpad, since this is a laptop.
    private const KeyCode Toggle = KeyCode.F9;
    private const KeyCode Forward = KeyCode.I;
    private const KeyCode Back = KeyCode.K;
    private const KeyCode Left = KeyCode.J;
    private const KeyCode Right = KeyCode.L;
    private const KeyCode Up = KeyCode.O;
    private const KeyCode Down = KeyCode.U;

    private const float Step = 0.35f;       // world units per frame held
    private const float FastMultiplier = 4f;

    public static bool Active { get; private set; }

    private static OverworldPlayer _player;
    private static bool _hadGravity;
    private static bool _wasKinematic;

    // Held keys, tracked from IMGUI events rather than UnityEngine.Input.
    // The legacy Input class is unreliable in a game that drives itself from
    // another input backend, whereas Event.current sees keystrokes regardless.
    private static readonly HashSet<KeyCode> _held = new();

    /// <summary>Called from Mod.OnGUI for every event.</summary>
    public static void HandleEvent(Event e)
    {
        if (e == null) return;

        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == Toggle)
            {
                if (Active) Stop(); else Start();
                return;
            }
            if (Active) _held.Add(e.keyCode);
        }
        else if (e.type == EventType.KeyUp)
        {
            _held.Remove(e.keyCode);
        }
    }

    /// <summary>Called every frame from Mod.OnUpdate. Cheap no-op when inactive.</summary>
    public static void Tick()
    {
        if (!Active) return;
        try
        {
            if (_player == null) { Stop(); return; }

            Vector3 delta = ReadInput();
            if (delta != Vector3.zero)
            {
                var t = _player.transform;
                _player.Teleport(t.position + delta, t.rotation);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[NUDGE] {ex.Message}");
            Stop();
        }
    }

    private static Vector3 ReadInput()
    {
        // Camera-relative so the controls match what is on screen. Flattened to
        // horizontal so looking down doesn't drive the car into the ground.
        Vector3 fwd = Vector3.forward, right = Vector3.right;
        var cam = Camera.main;
        if (cam != null)
        {
            Transform ct = cam.transform;
            fwd = Vector3.ProjectOnPlane(ct.forward, Vector3.up);
            fwd = fwd.sqrMagnitude < 0.001f ? Vector3.forward : fwd.normalized;
            right = Vector3.ProjectOnPlane(ct.right, Vector3.up).normalized;
        }

        Vector3 delta = Vector3.zero;
        if (_held.Contains(Forward)) delta += fwd;
        if (_held.Contains(Back)) delta -= fwd;
        if (_held.Contains(Right)) delta += right;
        if (_held.Contains(Left)) delta -= right;
        if (_held.Contains(Up)) delta += Vector3.up;
        if (_held.Contains(Down)) delta -= Vector3.up;

        if (delta == Vector3.zero) return delta;

        float step = Step;
        if (_held.Contains(KeyCode.LeftShift) || _held.Contains(KeyCode.RightShift))
            step *= FastMultiplier;
        return delta.normalized * step;
    }

    private static void Start()
    {
        _player = FindActivePlayer();
        if (_player == null)
        {
            Plugin.Log.LogWarning("[NUDGE] no active OverworldPlayer -- are you in an overworld?");
            return;
        }

        try
        {
            // Hold the car still. Without this it falls or slides the moment you
            // place it somewhere it wouldn't naturally rest.
            var rb = _player.Rigidbody;
            if (rb != null)
            {
                _hadGravity = rb.useGravity;
                _wasKinematic = rb.isKinematic;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.useGravity = false;
                rb.isKinematic = true;
            }
            Active = true;
            Plugin.Log.LogInfo(
                $"[NUDGE] ON -- {Forward}/{Back}/{Left}/{Right} move, {Up}/{Down} height, "
                + $"Shift = faster, {Toggle} to release.");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[NUDGE] could not start: {e.Message}");
            _player = null;
        }
    }

    private static void Stop()
    {
        try
        {
            var rb = _player?.Rigidbody;
            if (rb != null)
            {
                rb.isKinematic = _wasKinematic;
                rb.useGravity = _hadGravity;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"[NUDGE] could not restore: {e.Message}"); }
        finally
        {
            Active = false;
            _player = null;
            // A KeyUp can be missed if focus changes mid-hold, which would leave a
            // key stuck down and the car drifting on the next activation.
            _held.Clear();
            Plugin.Log.LogInfo("[NUDGE] OFF -- physics restored.");
        }
    }

    /// <summary>
    /// FindObjectsOfTypeAll returns inactive objects and prefab assets too, so
    /// filter to the live one -- the same lesson the island dump taught.
    /// </summary>
    private static OverworldPlayer FindActivePlayer()
    {
        try
        {
            var all = Resources.FindObjectsOfTypeAll<OverworldPlayer>();
            if (all == null) return null;
            for (int i = 0; i < all.Length; i++)
            {
                var p = all[i];
                if (p != null && p.gameObject != null && p.gameObject.activeInHierarchy)
                    return p;
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"[NUDGE] FindActivePlayer: {e.Message}"); }
        return null;
    }
}
