# Reverse engineering notes — WHAT THE CAR?

Findings from the game's IL2CPP metadata. Everything here is read off Cpp2IL's generated
interop assemblies; nothing has been verified in a running game yet unless a line says so.

## Setup

| | |
|---|---|
| Game version | 5.19.0 (Unity 2022.3.69f1, IL2CPP, metadata **v31.1**) |
| Game code assembly | **`Il2CppSpeed.dll`** (16 MB) — "Speed" is the internal project name |
| Interop assemblies | `<game>\MelonLoader\Il2CppAssemblies\` (169 DLLs, generated on first launch) |
| Decompiled source | `C:\Users\Joxtacy\ap-build\wtc_decomp` (3,012 `.cs` files, not committed) |

Regenerate the decompile with:

```powershell
dotnet tool install -g ilspycmd --version 8.0.0.7345   # 'latest' resolves to a non-tool package
$g = "C:\Program Files (x86)\Steam\steamapps\common\WHAT THE CAR\MelonLoader\Il2CppAssemblies"
ilspycmd "$g\Il2CppSpeed.dll" -r "$g" -o C:\Users\Joxtacy\ap-build\wtc_decomp -p --nested-directories
```

**These are interop *proxies*, not the original game code.** Every field becomes an unsafe
property over `il2cpp_field_get_offset` and every method is a trampoline — so you get names,
types and signatures but **no method bodies**. That is the same information golf's `dump.cs`
carried, in a different shape. Reading the raw output is mostly pointer-arithmetic noise, so use
the summariser:

```
python tools/typesummary.py C:/Users/Joxtacy/ap-build/wtc_decomp Island BaseAccessPoint
python tools/typesummary.py C:/Users/Joxtacy/ap-build/wtc_decomp --grep 'chest|card'
```

## Naming

Il2CppInterop prefixes **namespaced** types with `Il2Cpp`: `Speed.Overworld.Island` →
`Il2CppSpeed.Overworld.Island`. Global-namespace types land in a bare `Il2Cpp` namespace:
`IslandConnectionGate` → `Il2Cpp.IslandConnectionGate`. Each decompiled type records its true
IL2CPP identity in its static constructor, which `typesummary.py` surfaces as the `IL2CPP:` line
— **trust that line**, not the file path, when writing a Harmony patch target string.

Game namespaces: `Speed`, `Speed.Overworld`, `Speed.Level`, `Speed.Progression`, `Speed.Saving`,
`Speed.SceneManagement`, `Speed.UI`, `Speed.Achievements`, `Speed.Remixer`, `Speed.DailyMission`,
`Speed.GhostCar`, `Speed.Leaderboards`, and others.

## The world model

The game is a **tree of islands**, not golf's flat set of teleport-reachable chambers.

```
OverworldContinent
└── Island                       (MonoBehaviour, one per themed area)
    ├── IslandId : SaveableID    the stable string id
    ├── islandDef : IslandDef    the ScriptableObject definition
    ├── Levels : List<PlayableContentDef>
    ├── accessPoints : List<BaseAccessPoint>
    ├── OutgoingConnections : List<IslandConnection>
    ├── IngoingIsland : Island
    └── SaveSpots : Dictionary<SaveableID, OverworldSaveSpot>
```

`Island` also exposes `IsCompleted()` and `IsIslandTreeCompleted()` — the latter is a
ready-made goal predicate.

**`IslandConnection`** is just `{ Island Island; IslandConnectionGate Gate; }`, and
**`IslandConnectionGate`** (global namespace) is
`{ SpriteRenderer icon; TextLocalizer choiceText; Island island; IslandConnection connection; }`
with `OnChoicePicked()` / `OnChoiceUnpicked()`. The *choice* wording matters: progression appears
to branch, with the player picking which connection to open. That is a materially different
topology from golf and will shape the region graph.

### Levels

A level is a **`PlayableContentDef`** (ScriptableObject). Relevant fields:

- `contentId` — the stable level id (the mod's join key)
- `levelName`, `debugTitle`, `AnalyticsId`
- `silverTime`, `goldTime` — the medal thresholds
- `completedState : ELevelCompletedState`, `hasBeenFinished`, `hasBeenPlayed`
- `gameplayMode : EGameplayMode`, `gameplaySubType : EGameplaySubtype`

```
ELevelCompletedState : Incomplete=0, Bronze=1, Silver=2, Gold=3
```

So completion is **three medal tiers**, not golf's binary clear/crown. Levels are grouped into a
`Playlist` (`List<PlayableContentDef>` plus `id : SaveableID`), and a playlist is what an access
point launches.

### Access points

**`BaseAccessPoint`** (`Speed.Overworld`) is the cannon that fires the car into a level. It is
the natural unit for both checks and gating:

- `id : SaveableID`, `content : AccessPointContent` → `playlist : Playlist`
- `isPlayable`, `startHidden`, `completionPresented`
- `crowns : AccessPointCrowns` → `{ OverworldAccessCrown gold, silver, bronze }`
- `SetCrowns(ELevelCompletedState completedState, bool cardGained)` — note the `cardGained` flag
- `Refresh()`, `Appear(bool instant)`, `HideCannon()`, `CompleteFirstTime()`, `CompleteInstantly()`

## The native key system — the most important finding

`Speed.Saving.OverworldSaveInfo` carries a **first-class key/lock system the game already
implements**:

```csharp
bool HasKeyBeenRedeemed(SaveableID id)
bool IsKeyOnCar(SaveableID id)
void RedeemKey(SaveableID id)
void AddKeyOnCar(SaveableID id)
// backed by: _redeemedKeys, _currentKeysOnCar, _discoveries, _events (List<string> + HashSet)
```

and `ItemData` (ScriptableObject) is `{ CrossSceneID id; LocTerm Name; LocTerm Description;
Transform chestModel; Rigidbody KeyPrefab; }`, with `IslandDef.items : List<ItemData>`.

**The player physically carries keys on the car and redeems them at chests.** That maps onto
Archipelago almost one-to-one: an AP item can be granted by calling `AddKeyOnCar`/`RedeemKey`,
and a lock can be held shut by *withholding* the key rather than by fighting the game's own
gating logic. This is a far cleaner lever than golf, where gating meant hunting down
`OverworldMainDoorPlate.SetState` and force-holding doors every frame.

**Not yet verified.** Whether withholding a key actually blocks progression — and whether the
game re-derives key state from elsewhere on load — has to be tested in-game before any design
depends on it. Golf's lesson stands: the obvious-looking gate is often not the real one.

## Candidate hooks

Not yet bound in a running game. Signatures still need checking against the
"no `Nullable<T>` or by-value struct parameters" rule before patching.

| Purpose | Candidate |
|---|---|
| Level completed | `OnLevelWon` / `OnLevelDone` / `LevelWonEvent` |
| Level failed (DeathLink) | `OnLevelFailed` / `LevelFailedEvent` |
| Medal earned | `BaseAccessPoint.SetCrowns(ELevelCompletedState, bool)` |
| Chest opened | `OnChestOpen` |
| Card collected | `OnCardBecameVisible`, `OverworldCardCollectionPoint` |
| Car collected | `OverworldCarCollectionPoint` |
| Bear rescued | `OnOverworldBearRescued`, `BearCouncilManager` |
| Island entered | `OnIslandEnter`, `Island.OnTriggerEnter` |
| Overworld completed | `OnOverworldCompleted`, `Island.IsIslandTreeCompleted()` |
| Gating | `Island.UpdateAccessPoints()`, `BaseAccessPoint.Refresh()`, `IslandConnectionGate.OnChoicePicked()` |

## Content to exclude from the randomizer

Dailies, user-generated content and the remixer are not campaign content:
`Speed.DailyMission.*`, `Speed.Remixer.*`, `DailyLevelsProvider`, `LevelContentUGCFromIDLevel`,
`LevelContentRemix*`.

## Corrections to earlier assumptions

Recorded so they don't get re-derived:

- Several names taken from a `global-metadata.dat` string scan turned out to be **string
  literals, not types** — `NormalLevelDef`, `LevelsProvider`, `CardData`, `Chest`, `LevelManager`
  exist as strings but not as types in `Il2CppSpeed.dll`. `AreaData`/`AreaNode` are
  `UnityEngine.UIElements` types, unrelated to this game. The real level type is
  `PlayableContentDef` and the real area type is `Island`. **Do not plan against a metadata
  string scan** — confirm against the interop assemblies.
- Golf's "crown" is a single binary challenge flag; here it is a three-tier medal
  (`Bronze`/`Silver`/`Gold`) driven by lap times (`silverTime`/`goldTime`). The apworld's
  location model has to account for that.
