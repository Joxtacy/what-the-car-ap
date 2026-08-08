# STATUS

A dated engineering journal. Newest entries at the top. This file exists to record *why*
decisions were made and which approaches were tried and rejected — the golf project's
equivalent turned out to be its single most valuable file, so this one starts on day one.

---

## 2026-08-08 — Project started

### Decisions made

**Target.** Archipelago integration for WHAT THE CAR? (Triband, Steam appid `2727650`),
mirroring the architecture of `what-the-golf-ap`.

**Mod loader: MelonLoader.** Searched Thunderstore, Nexus and GitHub — WHAT THE CAR? has no
mods, no mod loader convention, and no existing AP world. With nothing to match, code reuse
decided it: the golf mod is loader-agnostic except `Plugin.cs` and `Mod.cs`, so MelonLoader is a
near-verbatim port whereas BepInEx would mean rewriting the entry point and relearning a second
set of gotchas for no identified benefit.

Worth recording precisely, because it is easy to misremember: **golf's BepInEx failure was not a
Unity-version incompatibility.** It was BepInEx's Dobby runtime-invoke detour hard-crashing that
game at `GfxDevice` init, reproducible with zero plugins. WHAT THE CAR? is a different binary, so
it might well not reproduce — that was simply not a strong enough reason to spend the test cycle.

**Milestone 1 scope.** Ends with an apworld that generates a solvable seed from the game's real
island/level data, plus the mod skeleton and dumpers that produced that data. In-game gating and
item application come *after*, deliberately: every gating decision depends on structure not yet
harvested.

### Recon (read-only, no files touched)

| | WHAT THE GOLF? | WHAT THE CAR? |
|---|---|---|
| Steam appid | 785790 | **2727650** |
| Unity | 2020.3.48f1 IL2CPP | **2022.3.69f1 IL2CPP** |
| IL2CPP metadata | v27 | **v31** |
| Saves | `…\LocalLow\Triband\WHAT THE GOLF_\` | `…\LocalLow\Triband\WHATTHECAR\` |

**Save slots.** `CarSave0.car` (36 KB — the ~100% save), `CarSave1.car` / `CarSave2.car`
(~780 B — empty). **Slot 1 or 2 is the fresh-save test bed; slot 0 is never written to.** This
matters: on golf, a progressed save masked the fact that gating wasn't actually holding, which
cost days. The empty slots here let us test gating properly without risking the real save.

**`global-metadata.dat` is unobfuscated.** Extracted 205,858 strings and read the structure off
them. Key types:

- **Continent → Islands → levels:** `OverworldContinent`, `IslandDef`, `IslandConnection`,
  `IslandConnectionGate`, `IsIslandTreeCompleted`, `AreaData`/`AreaNode`, `OverworldAccessPoint`,
  `LevelAccessPoint`, `LevelAccessPointCrown`, `OverworldAccessCrown`, `ArchwayUnlocker`.
- **10 overworlds**, each with Complete + Gold achievements: AmongUs, Beach, GOAT, Jobs, Jump,
  Long, Sneaky, Soccer, Storm, Wheels. Plus `OverworldDungeon_8_Bears`, `OverworldDungeon_Maze`.
- **Four collectible families**, all natural AP location groups: Cards (`CardData`,
  `OverworldCardCollectionPoint`), Cars (`OverworldCarCollectionPoint`), Chests (`Chest`,
  `ChestStateEnum`), Bears (`BearCouncilManager`).
- **Events to hook:** `OnLevelWon`, `OnLevelDone`, `OnLevelFailed`, `OnChestOpen`,
  `OnOverworldBearRescued`, `OnOverworldCompleted`, `OnIslandEnter`, `OnCardBecameVisible`.
  All are no-arg or take reference types — so none should hit golf's `Nullable<T>`
  interop-trampoline crash. (Rule still stands: never Harmony-patch a method whose signature has
  `Nullable<T>` or a by-value struct.)
- **To exclude from the randomizer:** `DailyLevelsProvider`, `Remixer*`,
  `LevelContentUGCFromIDLevel` — dailies and user-generated content are not campaign content.

### Known unknowns

- **Which object actually gates island access.** `IslandConnectionGate` is the promising name,
  with `ArchwayUnlocker` and `LevelAccessPoint` as fallbacks. Unresolved until probed in-game.
  Golf's lesson: the obvious-looking gate is often not the real one (goal-hiding looked right and
  didn't block entry at all; the real lever was `OverworldMainDoorPlate.SetState`). This gets
  settled empirically before any gating code is written.
- **Whether Il2CppDumper parses metadata v31.** The installed copy is from July 2024. Not a
  blocker — MelonLoader generates a real managed `Assembly-CSharp.dll` that ILSpy/dnSpy can read,
  which covers everything except method bodies.

### Carried-forward gotchas from the golf project

- Il2CppInterop prefixes **namespaced** game types with `Il2Cpp` (`Core.Level` →
  `Il2CppCore.Level`); global-namespace types keep their names.
- Never Harmony-patch a method with `Nullable<T>` or by-value struct parameters — the
  native→managed trampoline crashes. Hook a sibling method with reference-type params instead.
- Deploy the mod DLL to `<game>\Mods\` (game root, **not** `MelonLoader\Mods\`); deps go to
  `<game>\UserLibs\`.
- Kill the game before rebuilding — it locks the DLL.
- Never send to the AP socket synchronously from the game thread; a send into a dead socket
  blocks and freezes Unity. Queue inbound onto the main thread, push outbound to the ThreadPool.
- MelonLoader's `Newtonsoft.Json` and the copy bundled in `Archipelago.MultiClient.Net` have
  different assembly identities — never hand a `JToken` to `session.DataStorage`. Use strings.
- `python` on PATH here is MSYS: it cannot take Windows absolute-path arguments. Run tools from
  the repo root with relative paths.
- Launch the game with PowerShell `Start-Process "steam://rungameid/2727650"`; the Bash
  `cmd.exe start` trick fails silently.

### MelonLoader works — no BepInEx-style crash

MelonLoader **v0.7.3** (the current release, and the same version golf uses) installed into the
game dir. First launch generated interop cleanly:

- Cpp2IL handled **metadata v31.1** without complaint — 184,857 methods mapped, 169 interop
  assemblies produced in ~85 s.
- The game **passed `GfxDevice: creating device client`** and reached the title screen. That is
  the precise point where BepInEx's Dobby detour hard-crashed WHAT THE GOLF?. Unlike golf, the
  game does *not* exit itself after interop generation.
- Log confirms `Game Name: WHATTHECAR / Developer: Triband / Unity 2022.3.69f1 / Game Version 5.19.0`.

**Game code lives in `Il2CppSpeed.dll` (16 MB), not `Assembly-CSharp.dll` (90 KB).** "Speed" is
the internal project name — consistent with `SpeedMetaSave.SpeedMetaSave` in the save directory.
Namespaces are `Speed.*`.

### Reverse engineering — see `mod/REVERSE_ENGINEERING.md`

Decompiled `Il2CppSpeed.dll` to 3,012 C# files with `ilspycmd` (`--version 8.0.0.7345`; plain
`latest` resolves to a package that isn't a .NET tool). Added `tools/typesummary.py` to render a
type's base class, real IL2CPP identity, fields and method signatures — the interop proxies are
otherwise unreadable, being ~200 lines of pointer arithmetic per type.

Two findings that change the design:

1. **The game already has a key system.** `Speed.Saving.OverworldSaveInfo` exposes
   `HasKeyBeenRedeemed` / `IsKeyOnCar` / `RedeemKey` / `AddKeyOnCar` over `_redeemedKeys` and
   `_currentKeysOnCar`, and `IslandDef.items : List<ItemData>` where `ItemData` carries a
   `KeyPrefab` and a `chestModel`. The player physically carries keys and redeems them at chests.
   That is a near one-to-one fit for AP items and a much cleaner gating lever than golf's
   force-hold-the-door-plate-every-frame approach. **Unverified in-game** — whether withholding a
   key really blocks progression still has to be tested.
2. **The world is a tree of islands, not a flat teleport graph.** `Island` has
   `OutgoingConnections : List<IslandConnection>` / `IngoingIsland`, and `IslandConnectionGate`
   has `OnChoicePicked()` / `OnChoiceUnpicked()` — progression appears to *branch* with the
   player choosing which connection to open. Golf's "every region hangs off Menu" region layout
   will not transfer unchanged.

Also: completion is a **three-tier medal** (`ELevelCompletedState`: Incomplete/Bronze/Silver/Gold,
driven by `PlayableContentDef.silverTime`/`goldTime`), not golf's binary clear + crown. Levels are
`PlayableContentDef` (`contentId` is the stable key); access points (`BaseAccessPoint`) are
cannons that launch a `Playlist` of them.

**Correction worth keeping:** `global-metadata.dat` holds string literals alongside type names,
so a hit there does not prove a type exists — it is reconnaissance only. `AreaData`/`AreaNode`
looked like game types but are `UnityEngine.UIElements`; the real area type is `Island`.

**Correction to that correction (same session):** I then claimed `NormalLevelDef`,
`LevelsProvider`, `CardData` and `LevelManager` were literals rather than types. **Wrong** — all
four are real declared types, and `NormalLevelDef : PlayableContentDef` is in fact *the* concrete
campaign-level asset the dumper should target. The check was flawed: it ran `strings` over the
assembly and matched with `grep -x`, but .NET concatenates type names in the `#Strings` heap with
no line breaks, so an exact-whole-line match can never hit either way. **Confirm a type by
finding its declaration in the decompile.** (`Chest` genuinely isn't a class.)

### Mod skeleton live; first dump run done

Mod loads, both Harmony patches bind (`LevelManager:OnGameplayCompleted`,
`LevelManager:OnOutroFinished`), build is 0 warnings / 0 errors and auto-deploys.

**Levels: 275 defs captured in one sweep**, no walking needed — `NormalLevelDef` is a
ScriptableObject so `FindObjectsOfTypeAll` returns them wholesale. Data is good: real
`levelName` ("All Aboard The Wind Umbrella"), `debugTitle`, `silverTime`/`goldTime`, and a
`gameplaySubType` split of Normal 198 / Intermezzo 33 / OnlyTalk 28 / Hard 15 / Custom 1. So
roughly **213 real playable levels** once Intermezzo and OnlyTalk are filtered out. 5 are
`isUnplayableTemplate` (remixer seeds) and must be dropped.

**Islands: 95 captured** after the user visited each episode — islands are scene MonoBehaviours,
so they do NOT exist at the main menu, and (unlike golf's overworld) they load per-episode rather
than all at once.

### Two bugs found by that run

1. **`AccessTools.FieldRefAccess` does not work on Il2Cpp proxy types.** `GamePatches.ReadLevel`
   threw on every level completion (`FieldRefAccess<LevelManager, LevelInstance> ... caused an
   exception`). An interop proxy has no managed backing field — `_level` exists only as a
   generated property over `il2cpp_field_get_offset`. The property is public, so the fix is to
   read `manager._level` directly. Worth remembering as a general rule: **on interop types, use
   the generated property; never Harmony's field-ref helpers.** The patches themselves fired
   correctly (twice, once per hooked stage), so only the field read was wrong.
2. **`FindObjectsOfTypeAll` returns prefab templates alongside live instances**, and both carry
   the same `IslandName` — `ISLAND_JUMPING_START` appeared 5 times, `ISLAND_BEACH_TOPUGC` 7. This
   is the same duplicate-template trap golf hit with its crown doors. Evidence: the connection
   graph came back as **46 components for ~10 overworlds**, because prefab copies form their own
   disconnected sub-graphs. The data is not usable for the apworld until they are filtered.

Both fixed. The dumper now also records `scene` (null for a prefab, since a prefab's scene handle
is invalid), `activeInHierarchy` and `defName`, which is what lets `build_levels.py` drop the
templates. Added a third capture, `wtc_accesspoints.json` (id → island, playlist levels,
`startHidden`), because `Island.Levels` only accounted for **164 of the 275** level defs — the
per-cannon playlists should carry the rest of the level→island mapping.

### Second run: both fixes confirmed, and the prefab theory was wrong

`ReadLevel` fix verified live, no errors:

```
[LEVEL] gameplay-completed: bbe6df47776712248b70ef1eaac1bfc3 state=Gold won=True nonCampaign=False
[LEVEL] outro-finished:     bbe6df47776712248b70ef1eaac1bfc3 state=Gold won=True nonCampaign=False
```

Both hooked stages report identically, so `OnOutroFinished` is redundant — keep one.

**The prefab-duplicate diagnosis was WRONG.** 94 of the 95 islands have a valid scene, i.e. they
are all live instances. The duplicate `IslandName`s are simply a shared localisation term across
genuinely distinct islands (`ISLAND_BEACH_TOPUGC` ×7 really is seven separate UGC billboards).
The 46-component graph was fragmentation from islands whose connections hadn't been walked, not
from templates. **`(scene, defName)` is a unique key across all 95 — zero collisions** — so
island identity is solved regardless, and the `scene` field turned out to be the real prize: it
names the owning overworld.

### `OverworldData` is the authoritative asset — no driving required

`Speed.OverworldData` is the exact analogue of golf's `OverworldLevelData`: a ScriptableObject
holding an overworld's whole island list *and* its progression graph. It loads at the **main
menu** — 72 captured in one sweep with the game sitting on the title screen. Every future capture
is a launch-and-wait, not a playthrough.

- `islands : List<IslandDef>` → `IslandDef.playlists` → `Playlist.playables` gives level→island
  without the live scene objects, which only populate once the player physically drives up to them.
- `paths : List<SerializedProgressionPath>` → `ProgressionNode` {`nodeType`, `progressionCheckID`,
  `placementID`, `islandId`}. `NodeType` = PlayableAccessPoint / Generator / ProgressionStartArea /
  MiniGame.

Coverage went from 137/202 to **187/202** real campaign levels. The remaining 15 are correctly
excluded: 9 are dailies (`"Carp DAILY 1"`, `"Monowheel Daily 1"`), the rest tutorial "Reduction N"
and first-level content. So **"referenced by a MainGame overworld" is itself the campaign filter** —
better than filtering on `gameplaySubType`, which let the dailies through.

**Exactly 10 overworlds carry `pack == MainGame`**, matching the 10 achievements precisely:

| Overworld | islands | levels | paths |
|---|---|---|---|
| JUMPING | 10 | 19 | 2 |
| JOB | 12 | 24 | 3 |
| SOCCER | 12 | 21 | 3 |
| LONG | 7 | 20 | 2 |
| STORM | 9 | 21 | 2 |
| WHEELS | 12 | 22 | 4 |
| BEACH | 7 | 22 | 2 |
| AmongCAR | 15 | 5 | 3 |
| GoatSimulatorCollab | 2 | 9 | 1 |
| SneakySasquatch | 5 | 12 | 1 |

The other 62 are `MainGameHomeDungeon` — seasonal, UGC drops, Best-of-year, Daily, Community — and
are excludable, though a few (Golf Area ×8 levels, CowsInSpace ×6, DrivingSchool ×4, IceWeek ×11)
might be worth an option later.

### THE PROGRESSION MODEL — the game already implements Archipelago's shape

Every MainGame overworld has both a `requiredIdToAccess` (its gate) and a `completionId`, and
crucially **`completionId == givesBear`**. Completing an overworld awards a bear, and that bear
*is* the key to the next one. Matching the ids up gives the real graph:

```
JUMPING ──▶ JOB ──▶ SOCCER ──▶ LONG ──▶ WHEELS ──▶ BEACH
                        └────▶ STORM (shares LONG's gate)
             └──▶ AmongCAR, GOAT, SneakySasquatch (all gated on JOB's key)
```

This is a near-perfect fit for AP: the bear items become the progression items, and gating means
withholding a bear rather than fighting the game's own logic. It also settles the earlier open
question about branching — the topology is a chain with a three-way dungeon branch off JOB's key
and a STORM/LONG split, **not** the free choice `IslandConnectionGate.OnChoicePicked` suggested.

### Next

`tools/build_levels.py` → `what_the_car/levels.json`, then `data.py` and the apworld. All four
captures are committed under `mod/`; no further in-game work is needed to build the world.
