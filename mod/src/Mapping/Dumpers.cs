using System;
using System.Collections.Generic;
using System.IO;
using Il2CppSpeed;
using Il2CppSpeed.Overworld;
using Newtonsoft.Json;
using UnityEngine;

namespace WtcArchipelago.Mapping;

/// <summary>
/// Harvests the game's world structure to JSON, for tools/build_levels.py to
/// compile into the apworld's levels.json.
///
/// Design notes, most of them scar tissue from the golf mod:
///
///  * <b>Accumulate, never clobber.</b> Records merge across passes and sessions,
///    and a field is only written when the new value is non-empty. A golf dumper
///    that overwrote fields every pass nulled out already-known values (an object
///    can be seen before its parent resolves) and silently corrupted the level
///    data, which shifted location ids. See MergeInto.
///  * <b>Wall-clock throttled</b>, not frame-counted, so a 144 Hz display isn't
///    punished, and skipped entirely while a level is loaded.
///  * <b>ScriptableObject sweeps beat walking.</b> FindObjectsOfTypeAll returns
///    assets loaded but not instantiated, so NormalLevelDef comes back wholesale
///    rather than needing the player to visit every island.
///  * Output goes to the GAME ROOT, not the repo. Copy it into mod/ afterwards.
/// </summary>
public static class Dumpers
{
    private const double IntervalSeconds = 5.0;
    private static float _nextSweep;
    private static bool _loaded;

    // contentId -> record, islandId -> record. Plain BCL types (not Il2Cpp ones)
    // so Newtonsoft can serialise them directly.
    private static readonly Dictionary<string, Dictionary<string, object>> _levels = new();
    private static readonly Dictionary<string, Dictionary<string, object>> _islands = new();

    private static string GameRoot => MelonLoader.Utils.MelonEnvironment.GameRootDirectory;
    private static string LevelsPath => Path.Combine(GameRoot, "wtc_levels.json");
    private static string IslandsPath => Path.Combine(GameRoot, "wtc_islands.json");

    public static void Tick()
    {
        if (Time.realtimeSinceStartup < _nextSweep) return;
        _nextSweep = Time.realtimeSinceStartup + (float)IntervalSeconds;
        Capture(verbose: false);
    }

    /// <summary>Force a sweep + write now (hotkey).</summary>
    public static void CaptureNow() => Capture(verbose: true);

    private static void Capture(bool verbose)
    {
        try
        {
            EnsureLoaded();
            int levelsBefore = _levels.Count, islandsBefore = _islands.Count;

            SweepLevels();
            SweepIslands();

            bool grew = _levels.Count != levelsBefore || _islands.Count != islandsBefore;
            if (grew || verbose)
            {
                Write();
                Plugin.Log.LogInfo(
                    $"[DUMP] levels={_levels.Count} (+{_levels.Count - levelsBefore}) "
                    + $"islands={_islands.Count} (+{_islands.Count - islandsBefore}) -> game root");
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"Dumpers.Capture: {e}"); }
    }

    // --- Sweeps --------------------------------------------------------------

    private static void SweepLevels()
    {
        // NormalLevelDef : PlayableContentDef is the concrete campaign-level asset.
        var found = Resources.FindObjectsOfTypeAll<NormalLevelDef>();
        if (found == null) return;

        for (int i = 0; i < found.Length; i++)
        {
            var def = found[i];
            if (def == null) continue;
            try
            {
                string contentId = Str(def.contentId);
                if (contentId == null) continue;

                MergeInto(_levels, contentId, new Dictionary<string, object>
                {
                    ["contentId"] = contentId,
                    ["originalContentId"] = Str(def.originalContentId),
                    ["levelGuid"] = Str(def._levelGuid),
                    ["levelName"] = Str(def.levelName),
                    ["debugTitle"] = Str(def.debugTitle),
                    ["introWords"] = Str(def.introWordsTogether),
                    ["silverTime"] = def.silverTime,
                    ["goldTime"] = def.goldTime,
                    ["gameplayMode"] = def.gameplayMode.ToString(),
                    ["gameplaySubType"] = def.gameplaySubType.ToString(),
                    // Templates exist only to seed the remixer -- never campaign checks.
                    ["isUnplayableTemplate"] = def.isUnplayableTemplate,
                    // Ties a level to the card collectible.
                    ["giveCardAutomatically"] = def.GiveCardAutomatically,
                });
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[DUMP] level #{i}: {e.Message}");
            }
        }
    }

    private static void SweepIslands()
    {
        var found = Resources.FindObjectsOfTypeAll<Island>();
        if (found == null) return;

        for (int i = 0; i < found.Length; i++)
        {
            var island = found[i];
            if (island == null) continue;
            try
            {
                string id = Str(island.IslandId.id);
                if (id == null) continue;

                var record = new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["name"] = Str(island.IslandName),
                    ["isDefault"] = island.IsDefaultIsland,
                    ["supportsLocationSave"] = island.SupportsLocationSave,
                    ["levels"] = ContentIds(island.Levels),
                    ["accessPoints"] = AccessPointIds(island),
                    ["outgoing"] = OutgoingIslandIds(island),
                    ["ingoing"] = IngoingIslandId(island),
                    ["items"] = ItemIds(island),
                };
                MergeInto(_islands, id, record);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[DUMP] island #{i}: {e.Message}");
            }
        }
    }

    // --- Field readers -------------------------------------------------------
    // Each is independently guarded: one unresolvable reference must not cost us
    // the whole record.

    private static List<string> ContentIds(Il2CppSystem.Collections.Generic.List<PlayableContentDef> levels)
    {
        var ids = new List<string>();
        try
        {
            if (levels == null) return ids;
            for (int i = 0; i < levels.Count; i++)
            {
                string id = Str(levels[i]?.contentId);
                if (id != null) ids.Add(id);
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"[DUMP] ContentIds: {e.Message}"); }
        return ids;
    }

    private static List<string> AccessPointIds(Island island)
    {
        var ids = new List<string>();
        try
        {
            var points = island.accessPoints;
            if (points == null) return ids;
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                if (p == null) continue;
                string id = Str(p.id.id);
                if (id != null) ids.Add(id);
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"[DUMP] AccessPointIds: {e.Message}"); }
        return ids;
    }

    private static List<string> OutgoingIslandIds(Island island)
    {
        var ids = new List<string>();
        try
        {
            var conns = island.OutgoingConnections;
            if (conns == null) return ids;
            for (int i = 0; i < conns.Count; i++)
            {
                var target = conns[i]?.Island;
                if (target == null) continue;
                string id = Str(target.IslandId.id);
                if (id != null) ids.Add(id);
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"[DUMP] OutgoingIslandIds: {e.Message}"); }
        return ids;
    }

    private static string IngoingIslandId(Island island)
    {
        try { return Str(island.IngoingIsland?.IslandId.id); }
        catch (Exception e) { Plugin.Log.LogError($"[DUMP] IngoingIslandId: {e.Message}"); return null; }
    }

    private static List<string> ItemIds(Island island)
    {
        var ids = new List<string>();
        try
        {
            var items = island.islandDef?.items;
            if (items == null) return ids;
            for (int i = 0; i < items.Count; i++)
            {
                // ItemData.id is a CrossSceneID (a ScriptableObject), whose own
                // `id` is the stable string -- not `.ID`.
                string id = Str(items[i]?.id?.id);
                if (id != null) ids.Add(id);
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"[DUMP] ItemIds: {e.Message}"); }
        return ids;
    }

    // --- Merge + persistence -------------------------------------------------

    /// <summary>
    /// Merge a freshly-observed record into the accumulated one, keeping any
    /// value we already know. THE RULE: a new null/empty never replaces a known
    /// value. This is the golf bug that silently corrupted level data.
    /// </summary>
    private static void MergeInto(Dictionary<string, Dictionary<string, object>> store,
                                  string key, Dictionary<string, object> fresh)
    {
        if (!store.TryGetValue(key, out var existing))
        {
            store[key] = fresh;
            return;
        }
        foreach (var kv in fresh)
        {
            if (IsEmpty(kv.Value)) continue;                    // never clobber with nothing
            if (existing.TryGetValue(kv.Key, out var prev) && !IsEmpty(prev)
                && IsEmpty(kv.Value)) continue;
            existing[kv.Key] = kv.Value;
        }
    }

    private static bool IsEmpty(object v) => v switch
    {
        null => true,
        string s => s.Length == 0,
        List<string> l => l.Count == 0,
        _ => false,
    };

    /// <summary>Seed from any existing capture so a new session extends it.</summary>
    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        Load(LevelsPath, _levels);
        Load(IslandsPath, _islands);
        if (_levels.Count > 0 || _islands.Count > 0)
            Plugin.Log.LogInfo($"[DUMP] resumed from disk: {_levels.Count} levels, {_islands.Count} islands.");
    }

    private static void Load(string path, Dictionary<string, Dictionary<string, object>> into)
    {
        try
        {
            if (!File.Exists(path)) return;
            var parsed = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, object>>>(
                File.ReadAllText(path));
            if (parsed == null) return;
            foreach (var kv in parsed) into[kv.Key] = kv.Value;
        }
        catch (Exception e) { Plugin.Log.LogError($"[DUMP] load {path}: {e.Message}"); }
    }

    private static void Write()
    {
        Save(LevelsPath, _levels);
        Save(IslandsPath, _islands);
    }

    private static void Save(string path, Dictionary<string, Dictionary<string, object>> data)
    {
        try { File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented)); }
        catch (Exception e) { Plugin.Log.LogError($"[DUMP] write {path}: {e.Message}"); }
    }

    /// <summary>Normalise an Il2Cpp string to null-or-non-empty.</summary>
    private static string Str(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
