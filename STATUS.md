# STATUS

A dated engineering journal. Newest entries at the top. This file exists to record *why*
decisions were made and which approaches were tried and rejected — the golf project's
equivalent turned out to be its single most valuable file, so this one starts on day one.

---

## How to resume

Everything below is machine-specific and lives outside the repo. Paths as of 2026-08-08.

### Where things are

| | |
|---|---|
| Repo | `C:\Users\Joxtacy\Projects\what-the-car-ap` → github.com/Joxtacy/what-the-car-ap |
| Game | `C:\Program Files (x86)\Steam\steamapps\common\WHAT THE CAR` (appid `2727650`) |
| Saves | `%USERPROFILE%\AppData\LocalLow\Triband\WHATTHECAR\` (`CarSave0-2.car`, 3 slots) |
| **Save backup** | `C:\Users\Joxtacy\ap-build\wtc_save_backup` (slot 0 = the ~100% save) |
| Decompiled game | `C:\Users\Joxtacy\ap-build\wtc_decomp` (3,012 `.cs`, not committed) |
| AP for generating | `C:\ProgramData\Archipelago` (released 0.6.7, bundled Python) |
| AP source clone | `C:\Users\Joxtacy\ap-build\Archipelago` (git, at tag 0.6.7) |
| Seed test bed | `C:\Users\Joxtacy\ap-build\wtc_test\{Players,output}` |

### Commands

Run Python from the repo root with **relative paths** — the `python` on PATH is MSYS and
cannot take Windows absolute-path arguments.

```bash
python tools/build_levels.py            # report only
python tools/build_levels.py --write    # -> what_the_car/levels.json
python tools/export_ids.py              # -> mod/ids.json (the apworld<->mod contract)
python tools/typesummary.py C:/Users/Joxtacy/ap-build/wtc_decomp Island BaseAccessPoint
python tools/typesummary.py C:/Users/Joxtacy/ap-build/wtc_decomp --grep 'chest|card'
```

Build + deploy the mod (Debug auto-deploys to `<game>\Mods` and `<game>\UserLibs`):

```powershell
cd mod; dotnet build -c Debug     # CLOSE THE GAME FIRST -- it locks the DLL
```

Launch the game (PowerShell only; the Bash `cmd.exe start` trick fails silently):

```powershell
Start-Process "steam://rungameid/2727650"
```

Package and test the apworld:

```powershell
# zip what_the_car/ (minus __pycache__/*.pyc) to dist/what_the_car.apworld, then:
Copy-Item dist\what_the_car.apworld C:\ProgramData\Archipelago\custom_worlds\ -Force
& "C:\ProgramData\Archipelago\ArchipelagoGenerate.exe" `
    --player_files_path C:\Users\Joxtacy\ap-build\wtc_test\Players `
    --outputpath C:\Users\Joxtacy\ap-build\wtc_test\output --seed 777
```

`wtc_test\Players\wtc_multi.yaml` holds four slots covering every option axis. Host a seed with
`ArchipelagoServer.exe <seed.zip>` (default port 38281).

### Re-capturing game data

Only needed if the game updates. Set `Mod.DumpersEnabled = true`, `dotnet build -c Debug`, launch,
**wait at the main menu** (`OverworldData` loads there — no driving required), then copy
`<game>\wtc_*.json` into `mod\` and set the flag back to `false`. **F7** forces a sweep.

Regenerate the decompile with the `ilspycmd` command in `mod/REVERSE_ENGINEERING.md`
(pin `--version 8.0.0.7345`; plain `latest` resolves to a package that isn't a .NET tool).

### Before playing an AP seed

**Use save slot 1 or 2, never slot 0.** The mod reports best-ever medal state, so a progressed
save fires every check on first touch. See the medal-semantics entry below.

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

### Medal semantics — confirmed, and the name lies

User scored **silver** on a level they had previously golded; the mod logged **Gold**. So
`LevelInstance.completedStateThisInstance` reads through to the SAVED BEST, not the current run.
The true per-attempt value is `LevelInstance.lastResult.completedState` (`Speed.Level.LevelResult`,
which also carries `timeInMs` and `gainedCard`); the persisted best lives on
`PlayedLevelInfo.completedState`.

**Best-ever is the right thing to send**, since an AP location once checked stays checked, and it
self-heals a check missed while disconnected. The cost is that starting a seed on a progressed save
would fire every check on first touch — so **an AP run must use a fresh save slot**, which the
setup guide now states prominently. `GameState` exposes both values and logs both.

Also dropped the `OnOutroFinished` hook — it reported identically to `OnGameplayCompleted`.

### MILESTONE 1 COMPLETE — the apworld generates solvable multiworlds

`tools/build_levels.py` compiles the dumps into `what_the_car/levels.json`: **10 overworlds,
173 campaign levels**. Two levels turned out to be referenced by two overworlds each (GOAT collab
levels also placed in JOB and WHEELS); ownership goes to the alphabetically-first key and the fact
is recorded in `shared_levels` rather than silently resolved. Ownership iteration is **sorted, not
dump-order** — dump order varies per capture and would have silently shifted every location id.

The apworld follows golf's file split with a framework-free `data.py`. **20 items / 529 locations**
in the full id universe, with import-time duplicate asserts. UT support (the
`ut_can_gen_without_yaml` + `generate_early`/`re_gen_passthrough` + `interpret_slot_data` triad)
was built in from the start rather than retrofitted.

**Verified 2026-08-08** — a 4-player multiworld spanning every option axis generated on released
Archipelago 0.6.7, filled 1,587 items, and produced a valid playthrough:

| Slot | Options | Locations | Expected | Progression items |
|---|---|---|---|---|
| CarCampaign | campaign / separate / clear_only | 184 | 183 + Victory | 9 |
| CarAllWorlds | all_overworlds / bears / clear+gold | 357 | 356 + Victory | 5 |
| CarAllMedals | all_bears / separate / all_medals / no completions | 520 | 519 + Victory | 9 |
| CarBearsMax | all_bears / bears / all_medals | 530 | 529 + Victory | 5 |

Cross-world progression placement confirmed in the spoiler, with real level names
("Boost On Springboards - Clear", "I'm Longing It - Gold").

**Known looseness (accepted, documented):** under `overworld_access: separate` the game gates JOB,
AMONGCAR, GOAT and SNEAKY behind a *single* shared key, so unlocking one physically opens all four.
Out-of-logic reachability only — never unwinnable. `bears` mode has no such leak. This is the same
class of compromise golf shipped with its `section` granularity.

### Overworld nudge (F9) — a manual unstick, added on request

User hit an unreachable chest in one episode: the car is meant to swim up a river but never enters
its swimming state, walks instead, and cannot climb the ledge. `mod/src/OverworldNudge.cs` freezes
the car and steps it around by hand (F9 toggle; I/K/J/L move, O/U height, Shift for 4× step).

Design choices worth keeping:

- Moves the car through the game's own **`OverworldPlayer.Teleport(Vector3, Quaternion)`**, not by
  writing the transform. The movement states implement `OnTeleport(position, direction)`, so the
  game re-evaluates which state it belongs in. Deliberately does NOT force
  `OverworldPlayerMovementSwimming` on — poking a state machine from outside leaves it inconsistent
  and there is a supported door.
- Rigidbody made **kinematic** while active so the car holds still rather than sliding off wherever
  it is placed; `isKinematic`/`useGravity` restored on release. Nothing touches the save.
- Keys tracked from **`Event.current`**, not `UnityEngine.Input`. Legacy Input is unreliable in a
  game running its own input backend — the same reason golf's F8 panel reads events. (It also
  compiled fine against `UnityEngine.InputLegacyModule`, so the compiler would not have caught
  this; it needed knowing.)
- Needed a new `UnityEngine.PhysicsModule` reference for `Rigidbody`.

**LIVE-VALIDATED 2026-08-08:** user reached the ledge and opened the chest.

### The swim bug itself — diagnosed, not fixed

Useful negative result from that run: **teleporting the car into the river does NOT trigger
swimming.** That rules out every positional explanation. The entry chain is

```
BuoyancySource (trigger volume on the water)      -- OnTriggerEnter
  -> BuoyancyReceiver.HandleEnterBuoyancySource   -- Speed.Gameplay
  -> OnBuoyancySourceEnter (UnityEvent<GameObject>)
  -> OverworldPlayerMovementController.ChangeState(swimming)
```

`OverworldPlayerMovementController` holds `falling / running / swimming / trainSurfing /
wormRiding` and a `currentState`. Since being physically inside the water changes nothing, the
receiver is registering **no source** there — so the likely cause is that this stretch of river
carries no `BuoyancySource` trigger volume at all, or one whose collider or `buoyancySourceLayers`
mask does not match. Position cannot matter if there is no trigger to enter.

**Testable cheaply:** a read-only probe that, while the car sits in that river, logs
`BuoyancyReceiver.IsInWater()`, `InWaterPct()` and the `buoyancySources` list, plus any
`BuoyancySource` objects near the car. Empty list + nearby sources absent would confirm it.

**If confirmed, the fix has to be the thing the nudge deliberately avoided** — calling
`ChangeState(swimming)` directly — because with no trigger volume there is no supported door to
use. That is a real behaviour change rather than a camera-and-position helper, so it wants its own
toggle.

**AP relevance:** chests are not AP locations in the current world (locations are levels +
overworld completions), so nothing is unreachable in a seed today. But if chests are added later,
or if an access point sits past that river, this becomes a genuine logic hazard — a seed could
place a needed item behind terrain the game will not let the player cross. Worth resolving before
chests become checks.

Dumpers flipped back **off** now that harvesting is done; they cost a visible frame hitch.

### Next

The mod's `ItemApplier` is still a stub — nothing is gated yet. The promising lever is the game's
own key system (`OverworldSaveInfo.RedeemKey` / `AddKeyOnCar` against the `gate_by_item` /
`bear_by_item` maps now in `mod/ids.json`), which still needs its first in-game test on a fresh
save slot.
