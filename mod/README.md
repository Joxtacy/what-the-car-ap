# The mod

MelonLoader plugin for **WHAT THE CAR?** — the in-game half of the Archipelago
integration. It detects checks, applies received items, gates progression, and talks to the AP
server. It makes no logic decisions; those live in the apworld.

> **Status: skeleton.** Connects, binds its Harmony patches, and harvests world data. Item
> application and gating are stubs.

## Build

Requires the .NET 6 SDK and MelonLoader installed in the game directory.

```powershell
cd mod
dotnet build -c Debug     # also deploys, see below
```

A **Debug** build auto-deploys:

| Artifact | Destination |
|---|---|
| `WtcArchipelago.dll` | `<game>\Mods\` — the **game root** Mods folder, *not* `MelonLoader\Mods` |
| `Archipelago.MultiClient.Net.dll` | `<game>\UserLibs\` |
| `ids.json` (when it exists) | `<game>\wtc_ids.json` |

**Close the game before rebuilding** — it holds the DLL open and the copy will fail.

Override the install path with `-p:GameDir="..."`. For a machine without the game, drop the
reference DLLs into `mod/refs/` (see the `HasLocalRefs` switch in the csproj).

## Layout

```
src/
├─ Plugin.cs                 loader-agnostic static holder + LogAdapter
├─ Mod.cs                    MelonMod entry point; per-frame main-thread pump
├─ Archipelago/
│  ├─ ArchipelagoClient.cs   session ownership, checks, item receipt
│  └─ ArchipelagoData.cs     session state + slot data
├─ Patches/GamePatches.cs    Harmony hooks
└─ Mapping/
   ├─ LocationMap.cs         contentId -> display name -> AP location id (from wtc_ids.json)
   ├─ GameState.cs           safe reads off LevelInstance
   ├─ ItemApplier.cs         STUB
   └─ Dumpers.cs             world-structure harvesting
```

Only `Plugin.cs` and `Mod.cs` reference MelonLoader. Everything else is loader-agnostic, which is
what made porting this from the WHAT THE GOLF? mod a rename rather than a rewrite.

## Harvesting world data

The dumpers are **off by default** — they sweep every loaded object and write JSON, which costs a
visible frame hitch. To capture:

1. Set `Mod.DumpersEnabled = true`, `dotnet build -c Debug`.
2. Launch the game and **load a save** — islands are scene `MonoBehaviour`s and do not exist at
   the main menu. Levels are ScriptableObjects and come back wholesale on the first sweep.
3. Sweeps run every ~5 s; **F7** forces one.
4. Copy `<game>\wtc_*.json` into `mod/`, then set `DumpersEnabled` back to `false` and rebuild.

Output accumulates across sessions and merges rather than overwriting — a field is never replaced
by an empty value, because an object can be observed before its parent resolves. (A golf dumper
without that rule silently nulled known fields and corrupted the level data.)

| File | Source | Contents |
|---|---|---|
| `wtc_levels.json` | `Speed.NormalLevelDef` | contentId, levelGuid, levelName, debugTitle, introWords, silver/gold times, gameplay mode + subtype, template + card flags |
| `wtc_islands.json` | `Speed.Overworld.Island` | id, name, levels, access points, outgoing/ingoing connections, item ids |

## Overworld nudge (manual unstick) — **F9**

A hand-operated escape hatch for places the game will not let you reach. Built for a chest up a
river where the car never enters its swimming movement state, so it walks instead and cannot climb
the ledge.

| Key | |
|---|---|
| **F9** | toggle on/off |
| **I / K** | forward / back (camera-relative) |
| **J / L** | left / right |
| **O / U** | up / down |
| **Shift** | 4× step |

While active the rigidbody is made **kinematic** so the car holds still instead of sliding off
wherever you put it; the original `isKinematic` and `useGravity` are restored on release. Nothing
is written to the save.

It moves the car through the game's own `OverworldPlayer.Teleport(Vector3, Quaternion)` rather than
setting a transform directly — the movement states implement `OnTeleport`, so the game gets to
re-evaluate which state it should be in. It deliberately does **not** force
`OverworldPlayerMovementSwimming` on: poking a state machine from outside tends to leave it
inconsistent, and the supported door works.

Keys are read from `Event.current` in `OnGUI`, not `UnityEngine.Input` — the legacy input class is
unreliable in a game driving itself from another backend.

## Gotchas

- **Il2CppInterop prefixes namespaced types with `Il2Cpp`**: `Speed.Overworld.Island` →
  `Il2CppSpeed.Overworld.Island`. Global-namespace types keep their names.
- **Never Harmony-patch a method taking `Nullable<T>` or a by-value struct.** The
  native→managed trampoline can't marshal them and throws on every call. `Speed.Level.LevelWonEvent`
  is such a struct — hook no-argument methods and read state out of `LevelInstance` instead.
  Reading fields *out* is always safe.
- Patch targets are resolved **strongly typed** against the referenced `Il2CppSpeed.dll`, so a
  renamed method is a compile error rather than a silent runtime miss.
- **Never send to the AP socket synchronously from the game thread.** After a socket closes the
  session object lingers but its socket is dead, and sending into it blocks the caller — on the
  main thread that freezes Unity. Sends go via the ThreadPool; inbound work is queued to `Tick()`.
- MelonLoader's `Newtonsoft.Json` and the copy bundled in `Archipelago.MultiClient.Net` have
  **different assembly identities**. Never hand a `JToken` across that boundary; use strings.
- The `MelonGame` attribute matches the executable's internal name, `WHATTHECAR` — not the
  display title.
