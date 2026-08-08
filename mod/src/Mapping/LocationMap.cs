using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace WtcArchipelago.Mapping;

/// <summary>
/// Maps a level's contentId to Archipelago location ids, using the table the
/// apworld exports (tools/export_ids.py -> ids.json, deployed to the game root
/// as wtc_ids.json).
///
/// The game reports a raw `contentId`; the apworld renames that to a human
/// display name for its location names. So the lookup is
/// contentId -> display -> "{display} - {suffix}" -> id, exactly as the golf mod
/// does with scene names.
///
/// Everything here degrades to "unknown" (-1) rather than throwing: the id table
/// legitimately doesn't exist until the apworld has been built.
/// </summary>
public static class LocationMap
{
    private const long Missing = -1;

    private static Dictionary<string, long> _nameToId = new();
    private static Dictionary<string, long> _itemNameToId = new();
    private static Dictionary<string, string> _nameByContent = new();
    private static Dictionary<string, string> _islandByContent = new();

#pragma warning disable 0649 // fields assigned by Newtonsoft.Json via reflection
    private class IdsFile
    {
        public Dictionary<string, long> items;
        public Dictionary<string, long> locations;
        public Dictionary<string, string> name_by_content;
        public Dictionary<string, string> island_by_content;
    }
#pragma warning restore 0649

    public static bool Loaded { get; private set; }

    public static void Load()
    {
        try
        {
            string path = Path.Combine(
                MelonLoader.Utils.MelonEnvironment.GameRootDirectory, "wtc_ids.json");
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning(
                    $"LocationMap: no id table at {path} (expected until the apworld is built).");
                return;
            }
            var root = JsonConvert.DeserializeObject<IdsFile>(File.ReadAllText(path));
            _nameToId = root?.locations ?? new Dictionary<string, long>();
            _itemNameToId = root?.items ?? new Dictionary<string, long>();
            _nameByContent = root?.name_by_content ?? new Dictionary<string, string>();
            _islandByContent = root?.island_by_content ?? new Dictionary<string, string>();
            Loaded = true;
            Plugin.Log.LogInfo(
                $"LocationMap: loaded {_nameToId.Count} locations, {_itemNameToId.Count} items, "
                + $"{_nameByContent.Count} level names.");
        }
        catch (System.Exception e) { Plugin.Log.LogError($"LocationMap.Load: {e}"); }
    }

    // Completion is a three-tier medal here (ELevelCompletedState), not golf's
    // binary clear/crown, so each level can carry up to three checks. Which of
    // these the apworld actually creates is an option; an absent name resolves
    // to Missing and SendCheck ignores it.
    public static long ClearId(string contentId) => Lookup(contentId, " - Clear");
    public static long SilverId(string contentId) => Lookup(contentId, " - Silver");
    public static long GoldId(string contentId) => Lookup(contentId, " - Gold");

    /// <summary>Resolve a full AP location name to its id (-1 if unknown), for
    /// checks that aren't level+suffix (chests, cards, cars, bears).</summary>
    public static long IdByName(string name) =>
        name != null && _nameToId.TryGetValue(name, out var id) ? id : Missing;

    /// <summary>True if the apworld knows this level -- i.e. it's campaign
    /// content, not a daily, remix or UGC level.</summary>
    public static bool IsKnownLevel(string contentId) =>
        contentId != null && _nameByContent.ContainsKey(contentId);

    /// <summary>Island a level belongs to, or null if unknown.</summary>
    public static string IslandOf(string contentId) =>
        contentId != null && _islandByContent.TryGetValue(contentId, out var i) ? i : null;

    /// <summary>The display name the apworld gave this level (falls back to the
    /// raw contentId, matching the apworld's own fallback).</summary>
    private static string Display(string contentId) =>
        contentId != null && _nameByContent.TryGetValue(contentId, out var d) ? d : contentId;

    private static long Lookup(string contentId, string suffix)
    {
        if (contentId == null) return Missing;
        return _nameToId.TryGetValue(Display(contentId) + suffix, out var id) ? id : Missing;
    }
}
