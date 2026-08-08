using System;
using System.Collections.Generic;
using System.IO;
using Il2CppSpeed;
using Il2CppSpeed.Overworld;
using Il2Cpp;
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
    private static readonly Dictionary<string, Dictionary<string, object>> _accessPoints = new();
    private static readonly Dictionary<string, Dictionary<string, object>> _overworlds = new();
    private static readonly Dictionary<string, Dictionary<string, object>> _cards = new();

    private static string GameRoot => MelonLoader.Utils.MelonEnvironment.GameRootDirectory;
    private static string LevelsPath => Path.Combine(GameRoot, "wtc_levels.json");
    private static string IslandsPath => Path.Combine(GameRoot, "wtc_islands.json");
    private static string AccessPointsPath => Path.Combine(GameRoot, "wtc_accesspoints.json");
    private static string OverworldsPath => Path.Combine(GameRoot, "wtc_overworlds.json");
    private static string CardsPath => Path.Combine(GameRoot, "wtc_cards.json");

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
            int apBefore = _accessPoints.Count, owBefore = _overworlds.Count;
            int cardsBefore = _cards.Count;

            SweepLevels();
            SweepOverworlds();
            SweepCards();
            SweepIslands();

            bool grew = _levels.Count != levelsBefore || _islands.Count != islandsBefore
                        || _accessPoints.Count != apBefore || _overworlds.Count != owBefore
                        || _cards.Count != cardsBefore;
            if (grew || verbose)
            {
                Write();
                Plugin.Log.LogInfo(
                    $"[DUMP] levels={_levels.Count} (+{_levels.Count - levelsBefore}) "
                    + $"overworlds={_overworlds.Count} (+{_overworlds.Count - owBefore}) "
                    + $"islands={_islands.Count} (+{_islands.Count - islandsBefore}) "
                    + $"accessPoints={_accessPoints.Count} (+{_accessPoints.Count - apBefore}) "
                    + $"cards={_cards.Count} (+{_cards.Count - cardsBefore}) -> game root");
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

    /// <summary>
    /// Collectible cards. Each CardData points at the PlayableContentDef whose
    /// played info drives its colour (`CardData.GetPlayedLevelInfo()`), so this is
    /// what tells us which level a given card is actually reading -- the question
    /// behind "I have gold on every level but six cards still aren't gold".
    /// </summary>
    private static void SweepCards()
    {
        var found = Resources.FindObjectsOfTypeAll<CardData>();
        if (found == null) return;

        for (int i = 0; i < found.Length; i++)
        {
            var card = found[i];
            if (card == null) continue;
            try
            {
                string assetName = Str(card.name);
                string content = null;
                try { content = Str(card.ContentDef?.contentId); } catch { }
                string key = content ?? assetName;
                if (key == null) continue;

                var record = new Dictionary<string, object>
                {
                    ["assetName"] = assetName,
                    ["contentId"] = content,
                    ["cardId"] = CrossId(card.id),
                    ["overworldId"] = Str(card.OverworldID),
                    ["type"] = card.Type.ToString(),
                    ["isUnclaimed"] = card.IsUnclaimed,
                };

                // Ask the card which PlayedLevelInfo it ACTUALLY resolves to. This is
                // the only way to settle whether a card reads the record keyed by its
                // own contentId or something else (e.g. best-of-template) -- the
                // interop assemblies carry no method bodies, so it cannot be read off
                // the decompile, and inferring it from the save was already wrong once.
                try
                {
                    var info = card.GetPlayedLevelInfo();
                    if (info != null)
                    {
                        record["resolvedLevelId"] = Str(info.levelId);
                        record["resolvedTemplateId"] = Str(info.templateId);
                        record["resolvedState"] = (int)info.completedState;
                        record["resolvedBestTimeMs"] = info.bestTimeMs;
                        record["resolvedGainedCard"] = info.gainedCard;
                        record["resolvedFinishedCount"] = info.finishedCount;
                    }
                    else record["resolvedLevelId"] = "<null>";
                }
                catch (Exception e) { record["resolvedError"] = e.Message; }

                MergeInto(_cards, key, record);
            }
            catch (Exception e) { Plugin.Log.LogError($"[DUMP] card #{i}: {e.Message}"); }
        }
    }

    /// <summary>
    /// The authoritative structure sweep. OverworldData is a ScriptableObject
    /// holding every island of an overworld plus its progression graph, so ONE
    /// pass captures the lot -- no driving required. (Golf's equivalent was
    /// OverworldLevelData, and reading it was likewise what turned a walk-the-map
    /// chore into a single capture.)
    ///
    /// The live Island MonoBehaviours only populate their level lists once the
    /// player physically activates them, which is why the first two runs left 65
    /// of 202 real levels unmapped. IslandDef.playlists reaches the same levels
    /// off the asset instead.
    /// </summary>
    private static void SweepOverworlds()
    {
        var found = Resources.FindObjectsOfTypeAll<OverworldData>();
        if (found == null) return;

        for (int i = 0; i < found.Length; i++)
        {
            var ow = found[i];
            if (ow == null) continue;
            try
            {
                string id = Str(ow.id.id) ?? Str(ow.name);
                if (id == null) continue;

                MergeInto(_overworlds, id, new Dictionary<string, object>
                {
                    ["id"] = Str(ow.id.id),
                    ["assetName"] = Str(ow.name),
                    ["pack"] = ow.overworldPack.ToString(),
                    ["featuredTag"] = Str(ow.featuredTag),
                    // The access gate: the key/id the player must hold to enter.
                    ["requiredIdToAccess"] = CrossId(ow.RequiredIdToAccess),
                    ["completionId"] = CrossId(ow.CompletionId),
                    ["carwashId"] = CrossId(ow.carwashID),
                    ["accessId"] = CrossId(ow.accessID),
                    ["givesBear"] = CrossId(ow.GivesBear?.id),
                    ["bypassRedeemBear"] = ow.BypassRedeemBear,
                    ["progressAchievement"] = ow.progressAchievement.ToString(),
                    ["goldAchievement"] = ow.goldAchievement.ToString(),
                    ["islands"] = OverworldIslands(ow),
                    ["paths"] = ProgressionPaths(ow),
                });
            }
            catch (Exception e) { Plugin.Log.LogError($"[DUMP] overworld #{i}: {e.Message}"); }
        }
    }

    private static List<Dictionary<string, object>> OverworldIslands(OverworldData ow)
    {
        var list = new List<Dictionary<string, object>>();
        try
        {
            var defs = ow.islands;
            if (defs == null) return list;
            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                if (def == null) continue;
                list.Add(new Dictionary<string, object>
                {
                    ["id"] = Str(def.id.id),
                    ["name"] = Str(def.name),
                    ["order"] = i,
                    ["levels"] = IslandDefLevels(def),
                    ["items"] = IslandDefItems(def),
                });
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"[DUMP] OverworldIslands: {e.Message}"); }
        return list;
    }

    private static List<string> IslandDefLevels(IslandDef def)
    {
        var ids = new List<string>();
        try
        {
            var playlists = def.playlists;
            if (playlists == null) return ids;
            for (int i = 0; i < playlists.Count; i++)
            {
                foreach (var id in ContentIds(playlists[i]?.playables))
                    if (!ids.Contains(id)) ids.Add(id);
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"[DUMP] IslandDefLevels: {e.Message}"); }
        return ids;
    }

    private static List<string> IslandDefItems(IslandDef def)
    {
        var ids = new List<string>();
        try
        {
            var items = def.items;
            if (items == null) return ids;
            for (int i = 0; i < items.Count; i++)
            {
                string id = CrossId(items[i]?.id);
                if (id != null) ids.Add(id);
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"[DUMP] IslandDefItems: {e.Message}"); }
        return ids;
    }

    /// <summary>
    /// The progression graph: ordered nodes per path, each naming the island it
    /// sits on and the id whose completion advances it. ProgressionNode is a
    /// by-value struct -- fine to READ, never patch a method that takes one.
    /// </summary>
    private static List<List<Dictionary<string, object>>> ProgressionPaths(OverworldData ow)
    {
        var paths = new List<List<Dictionary<string, object>>>();
        try
        {
            var serialized = ow.paths;
            if (serialized == null) return paths;
            for (int p = 0; p < serialized.Count; p++)
            {
                var nodes = serialized[p]?.progressionNodes;
                if (nodes == null) continue;
                var one = new List<Dictionary<string, object>>();
                for (int n = 0; n < nodes.Count; n++)
                {
                    var node = nodes[n];
                    if (node == null) continue;
                    one.Add(new Dictionary<string, object>
                    {
                        ["nodeType"] = node.nodeType.ToString(),
                        ["progressionCheckId"] = Str(node.progressionCheckID.id),
                        ["placementId"] = Str(node.placementID.id),
                        ["islandId"] = Str(node.islandId.id),
                    });
                }
                paths.Add(one);
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"[DUMP] ProgressionPaths: {e.Message}"); }
        return paths;
    }

    private static string CrossId(Il2CppSpeed.Saving.CrossSceneID id)
    {
        try { return id == null ? null : Str(id.id); }
        catch { return null; }
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
                    // IslandName is a localisation term and is NOT unique -- several
                    // distinct islands share one. The def's own asset name usually
                    // disambiguates, so capture both.
                    ["defName"] = DefName(island),
                    ["isDefault"] = island.IsDefaultIsland,
                    ["supportsLocationSave"] = island.SupportsLocationSave,
                    ["levels"] = ContentIds(island.Levels),
                    ["accessPoints"] = AccessPointIds(island, id),
                    ["outgoing"] = OutgoingIslandIds(island),
                    ["ingoing"] = IngoingIslandId(island),
                    ["items"] = ItemIds(island),
                    // FindObjectsOfTypeAll returns loaded-but-not-instantiated PREFABS
                    // alongside live scene instances, and both carry the same
                    // IslandName. A prefab has no valid scene, so the scene name is
                    // what lets build_levels.py drop the templates. (Golf hit the same
                    // duplicate-template problem with its crown doors.)
                    ["scene"] = SceneName(island),
                    ["activeInHierarchy"] = ActiveInHierarchy(island),
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

    /// <summary>
    /// Collect this island's access-point ids, and as a side effect record each
    /// one's own playlist into _accessPoints. Island.Levels alone came back with
    /// only 164 of 275 known level defs, so the per-cannon playlists are where the
    /// rest of the level-to-island mapping has to come from.
    /// </summary>
    private static List<string> AccessPointIds(Island island, string islandId)
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
                if (id == null) continue;
                ids.Add(id);

                MergeInto(_accessPoints, id, new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["island"] = islandId,
                    ["levels"] = PlaylistLevels(p),
                    ["startHidden"] = p.startHidden,
                });
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"[DUMP] AccessPointIds: {e.Message}"); }
        return ids;
    }

    /// <summary>
    /// Levels a cannon launches. The playlist may legitimately be null until the
    /// game generates it, so an empty list here is normal and MergeInto will keep
    /// whatever a later pass finds.
    /// </summary>
    private static List<string> PlaylistLevels(BaseAccessPoint point)
    {
        try
        {
            var playlist = point.content?.playlist ?? point.playlist;
            return playlist == null ? new List<string>() : ContentIds(playlist.playables);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[DUMP] PlaylistLevels: {e.Message}");
            return new List<string>();
        }
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

    private static string DefName(Island island)
    {
        try { return Str(island.islandDef?.name); }
        catch (Exception e) { Plugin.Log.LogError($"[DUMP] DefName: {e.Message}"); return null; }
    }

    private static string SceneName(Island island)
    {
        try
        {
            var go = island.gameObject;
            if (go == null) return null;
            var scene = go.scene;
            // A prefab asset's scene handle is invalid; a live instance's is not.
            return scene.IsValid() ? Str(scene.name) : null;
        }
        catch (Exception e) { Plugin.Log.LogError($"[DUMP] SceneName: {e.Message}"); return null; }
    }

    private static bool ActiveInHierarchy(Island island)
    {
        try { return island.gameObject != null && island.gameObject.activeInHierarchy; }
        catch { return false; }
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
        Load(AccessPointsPath, _accessPoints);
        Load(OverworldsPath, _overworlds);
        Load(CardsPath, _cards);
        if (_levels.Count > 0 || _islands.Count > 0 || _accessPoints.Count > 0)
            Plugin.Log.LogInfo(
                $"[DUMP] resumed from disk: {_levels.Count} levels, {_overworlds.Count} overworlds, "
                + $"{_islands.Count} islands, {_accessPoints.Count} access points.");
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
        Save(AccessPointsPath, _accessPoints);
        Save(OverworldsPath, _overworlds);
        Save(CardsPath, _cards);
    }

    private static void Save(string path, Dictionary<string, Dictionary<string, object>> data)
    {
        try { File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented)); }
        catch (Exception e) { Plugin.Log.LogError($"[DUMP] write {path}: {e.Message}"); }
    }

    /// <summary>Normalise an Il2Cpp string to null-or-non-empty.</summary>
    private static string Str(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
