using System;
using System.Reflection;
using HarmonyLib;
using Il2CppSpeed.Level;
using WtcArchipelago.Mapping;

namespace WtcArchipelago.Patches;

/// <summary>
/// Harmony hooks into WHAT THE CAR?. Targets are resolved STRONGLY TYPED against
/// the referenced Il2CppSpeed.dll rather than by string, so a renamed or removed
/// method is a compile error instead of a silent runtime "patch target not found"
/// -- and it avoids the AccessTools reflection log noise the golf mod puts up with.
///
/// SAFETY RULE (inherited from the golf mod, learned by crashing that game):
/// never patch a method whose signature contains Nullable&lt;T&gt; or a by-value
/// struct. Il2CppInterop's native->managed trampoline can't marshal those and
/// throws NullReferenceException on every call. Note that Speed.Level.LevelWonEvent
/// IS such a struct, so anything taking it by value is off-limits; we hook
/// no-argument methods and read state out of LevelInstance instead.
///
/// Every postfix wraps its body in try/catch. An exception escaping a postfix
/// lands in the game's own call stack.
/// </summary>
public static class GamePatches
{
    public static void Apply(HarmonyLib.Harmony harmony)
    {
        // Level finished. OnGameplayCompleted takes no arguments (trampoline-safe);
        // we read the outcome off the LevelManager's own LevelInstance afterwards.
        // Whether this is the right moment for the medal to be final -- versus
        // EvaluateGameplayCompletion or OnOutroFinished -- is UNVERIFIED and is
        // exactly what the first in-game run is meant to settle.
        TryPatchPostfix(harmony, typeof(LevelManager), nameof(LevelManager.OnGameplayCompleted),
                        nameof(GameplayCompletedPostfix));

        // Same event, one stage later. Logged only, so we can compare which of the
        // two carries a settled completedStateThisInstance before committing.
        TryPatchPostfix(harmony, typeof(LevelManager), nameof(LevelManager.OnOutroFinished),
                        nameof(OutroFinishedPostfix));
    }

    // --- Postfixes -----------------------------------------------------------

    private static void GameplayCompletedPostfix(LevelManager __instance)
    {
        try { Report("gameplay-completed", __instance); }
        catch (Exception e) { Plugin.Log.LogError($"GameplayCompletedPostfix: {e}"); }
    }

    private static void OutroFinishedPostfix(LevelManager __instance)
    {
        try { Report("outro-finished", __instance); }
        catch (Exception e) { Plugin.Log.LogError($"OutroFinishedPostfix: {e}"); }
    }

    /// <summary>
    /// Log what the game just reported, and send the matching check once the
    /// apworld exists. Until then this is the observation surface that tells us
    /// what contentIds and medal states actually look like in play.
    /// </summary>
    private static void Report(string stage, LevelManager manager)
    {
        LevelInstance level = ReadLevel(manager);
        if (level == null) return;

        Plugin.Log.LogInfo($"[LEVEL] {stage}: {GameState.Describe(level)}");

        // Passive until connected -- an installed mod plays like vanilla otherwise.
        var client = Plugin.Client;
        if (client == null || !client.Connected) return;
        if (!GameState.DidWin(level) || GameState.IsNonCampaign(level)) return;

        string contentId = GameState.ContentId(level);
        if (!LocationMap.IsKnownLevel(contentId))
        {
            Plugin.Log.LogWarning($"[LEVEL] won an unknown level '{contentId}' -- no check sent.");
            return;
        }

        // Medals are cumulative: reaching Gold implies Silver and Clear. Sending
        // all tiers the player has earned keeps checks correct even if they jump
        // straight to Gold on the first attempt. SendCheck dedups.
        var state = GameState.CompletedState(level);
        client.SendCheck(LocationMap.ClearId(contentId));
        if (state >= Il2CppSpeed.Saving.ELevelCompletedState.Silver)
            client.SendCheck(LocationMap.SilverId(contentId));
        if (state >= Il2CppSpeed.Saving.ELevelCompletedState.Gold)
            client.SendCheck(LocationMap.GoldId(contentId));
    }

    /// <summary>
    /// Read the LevelManager's current level.
    ///
    /// Do NOT use Harmony's AccessTools.FieldRefAccess here. An Il2Cpp proxy type
    /// has no managed backing field -- `_level` exists only as a generated property
    /// over il2cpp_field_get_offset -- so FieldRefAccess throws every call. (It did,
    /// live, on 2026-08-08.) The generated property is public, so just read it.
    /// </summary>
    private static LevelInstance ReadLevel(LevelManager manager)
    {
        if (manager == null) return null;
        try { return manager._level; }
        catch (Exception e)
        {
            Plugin.Log.LogError($"GamePatches.ReadLevel: {e.Message}");
            return null;
        }
    }

    // --- Patch helpers -------------------------------------------------------

    private static void TryPatchPostfix(HarmonyLib.Harmony harmony, Type target, string method,
                                        string postfix)
    {
        try
        {
            MethodInfo m = AccessTools.Method(target, method);
            if (m == null)
            {
                Plugin.Log.LogWarning($"patch target not found: {target.Name}:{method}");
                return;
            }
            harmony.Patch(m, postfix: new HarmonyMethod(typeof(GamePatches), postfix));
            Plugin.Log.LogInfo($"patched: {target.Name}:{method}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"failed to patch {target.Name}:{method}: {e.Message}");
        }
    }
}
