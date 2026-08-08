using MelonLoader;
using UnityEngine;
using WtcArchipelago.Patches;

[assembly: MelonInfo(typeof(WtcArchipelago.Mod), "WtcArchipelago", "0.1.0", "Joxtacy")]
[assembly: MelonGame("Triband", "WHATTHECAR")]

namespace WtcArchipelago;

/// <summary>
/// MelonLoader entry point and main-thread pump.
///
/// MelonLoader rather than BepInEx: this game has no existing modding scene, so
/// there was no convention to match, and the WHAT THE CAR? mod reuses the golf
/// mod's structure almost verbatim. (Golf's BepInEx failure was a Dobby-detour
/// crash specific to that binary, not a Unity-version problem -- it may not even
/// reproduce here. It just wasn't worth a test cycle for no benefit.)
///
/// NOTE the MelonGame attribute is "WHATTHECAR" -- the executable's internal name,
/// which is what MelonLoader matches on. The display title is "WHAT THE CAR?".
/// </summary>
public class Mod : MelonMod
{
    // Periodic data-harvesting dumpers. These call Resources.FindObjectsOfTypeAll
    // (a sweep over every loaded object) and write JSON, which costs a visible
    // frame hitch -- so they stay OFF except during an explicit capture session.
    // Flip to true, rebuild, play through the areas you want captured, flip back.
    public static readonly bool DumpersEnabled = false;

    // Hotkey to dump on demand during a capture session, read from Event.current
    // in OnGUI so it works regardless of the game's input backend.
    private const KeyCode DumpKey = KeyCode.F7;

    public override void OnInitializeMelon()
    {
        Plugin.Client = new ArchipelagoClient();
        Mapping.LocationMap.Load();
        GamePatches.Apply(HarmonyInstance);

        Plugin.Log.LogInfo(
            $"WtcArchipelago loaded (game: {Plugin.GameName}). "
            + (DumpersEnabled ? $"Dumpers ON -- press {DumpKey} to capture. " : "Dumpers off. ")
            + "F9 = overworld nudge (manual unstick).");
    }

    public override void OnUpdate()
    {
        // Drain AP callbacks that need the main thread. Cheap when idle, and it
        // must run even while disconnected so a late queue empties safely.
        Plugin.Client?.Tick();

        if (DumpersEnabled) Mapping.Dumpers.Tick();
        OverworldNudge.Tick();
    }

    public override void OnGUI()
    {
        var e = Event.current;
        if (e == null) return;

        OverworldNudge.HandleEvent(e);

        if (DumpersEnabled && e.type == EventType.KeyDown && e.keyCode == DumpKey)
            Mapping.Dumpers.CaptureNow();
    }
}
