using System.Collections.Generic;

namespace WtcArchipelago;

/// <summary>
/// Session state that must survive reconnects / save reloads. Slot-data fields
/// are added here as the apworld grows options; right now the apworld doesn't
/// exist yet, so only the connection basics and the replay guards are real.
/// </summary>
public class ArchipelagoData
{
    public string Host = "localhost";
    public int Port = 38281;
    public string SlotName = "Player1";
    public string Password = null;

    // Highest received-item index already applied. Items at or below this were
    // already granted; skip them when the server replays history on connect.
    public long ItemIndex = -1;

    // Locations already reported this session (avoid duplicate sends).
    public readonly HashSet<long> CheckedLocations = new();

    // --- Slot data (from the apworld's fill_slot_data) ---------------------
    // Goal constants will mirror Options.py once the apworld defines them.
    public int Goal;
    public bool DeathLinkEnabled;
}
