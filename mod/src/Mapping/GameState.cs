using System;
using Il2CppSpeed.Level;
using Il2CppSpeed.Saving;

namespace WtcArchipelago.Mapping;

/// <summary>
/// Reads the game's current level state. Reading fields OUT of Il2Cpp objects is
/// the safe direction across the interop boundary -- it is *calling* methods with
/// awkward signatures (Nullable&lt;T&gt;, by-value structs) that crashes, which is why
/// nothing here invokes game logic.
///
/// Every accessor swallows its exceptions and returns a null/default: a level can
/// be mid-teardown when a postfix runs, and a throw escaping into the game is
/// never worth it.
/// </summary>
public static class GameState
{
    /// <summary>The stable content id of a level instance, or null.</summary>
    public static string ContentId(LevelInstance level)
    {
        try { return level?.contentId; }
        catch (Exception e) { Plugin.Log.LogError($"GameState.ContentId: {e.Message}"); return null; }
    }

    /// <summary>Medal reached in this attempt: Incomplete / Bronze / Silver / Gold.</summary>
    public static ELevelCompletedState CompletedState(LevelInstance level)
    {
        try { return level != null ? level.completedStateThisInstance : ELevelCompletedState.Incomplete; }
        catch (Exception e)
        {
            Plugin.Log.LogError($"GameState.CompletedState: {e.Message}");
            return ELevelCompletedState.Incomplete;
        }
    }

    /// <summary>Did the player actually win this attempt (vs. quitting/failing)?</summary>
    public static bool DidWin(LevelInstance level)
    {
        try { return level != null && level.didWinThisInstance; }
        catch (Exception e) { Plugin.Log.LogError($"GameState.DidWin: {e.Message}"); return false; }
    }

    /// <summary>True for remixer / UGC / daily content, which is never a check.</summary>
    public static bool IsNonCampaign(LevelInstance level)
    {
        try
        {
            if (level == null) return true;
            return level.isRemix || level.isDraftRemix || level.isPublishedPlayerRemix
                   || level.isDailyChallengePlaylist;
        }
        catch (Exception e)
        {
            // Fail closed: if we can't tell, treat it as non-campaign rather than
            // risk sending a check for a daily or a remix.
            Plugin.Log.LogError($"GameState.IsNonCampaign: {e.Message}");
            return true;
        }
    }

    /// <summary>One-line description for logging.</summary>
    public static string Describe(LevelInstance level)
    {
        string id = ContentId(level) ?? "<null>";
        return $"{id} state={CompletedState(level)} won={DidWin(level)} nonCampaign={IsNonCampaign(level)}";
    }
}
