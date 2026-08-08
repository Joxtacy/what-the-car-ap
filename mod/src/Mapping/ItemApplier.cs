using Archipelago.MultiClient.Net.Models;

namespace WtcArchipelago.Mapping;

/// <summary>
/// Applies a received Archipelago item to the game. STUB -- the apworld has no
/// item pool yet, so this only logs.
///
/// When it is built it should route purely by item NAME (string), the way the
/// golf mod does, so the mod holds no hardcoded game knowledge. The promising
/// lever here is the game's own key system: Speed.Saving.OverworldSaveInfo
/// exposes AddKeyOnCar / RedeemKey / IsKeyOnCar / HasKeyBeenRedeemed over
/// _currentKeysOnCar and _redeemedKeys, and IslandDef.items lists the ItemData
/// each island's keys come from. Granting an AP item may be as simple as calling
/// AddKeyOnCar -- which would make this far cleaner than golf, where item
/// application meant force-holding door plates open every frame.
///
/// UNVERIFIED. Whether the game re-derives key state on load, and whether
/// withholding a key actually blocks progression, has to be tested in-game
/// before any design depends on it.
/// </summary>
public static class ItemApplier
{
    public static void Apply(ItemInfo item)
    {
        if (item == null) return;
        string name = item.ItemName ?? $"id:{item.ItemId}";
        Plugin.Log.LogInfo($"[ITEM] received '{name}' (not applied -- ItemApplier is a stub)");
    }
}
