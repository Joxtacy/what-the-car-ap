# WHAT THE CAR? — Archipelago

An [Archipelago](https://archipelago.gg) multiworld randomizer integration for Triband's
**WHAT THE CAR?** (Steam appid `2727650`).

> **Status: early.** The apworld and the mod are both under construction. Nothing here is
> playable yet. See [`STATUS.md`](STATUS.md) for where things actually stand.

This is a sibling of [what-the-golf-ap](https://github.com/Joxtacy/what-the-golf-ap) and
deliberately mirrors its architecture.

## The two pieces

An Archipelago integration for a game like this is two separate programs that agree on a
contract:

| | Path | Language | Role |
|---|---|---|---|
| **The apworld** | `what_the_car/` | Python | The "brain". Defines items, locations, regions, logic rules and options. Runs inside Archipelago at seed-generation time. Never sees the game. |
| **The mod** | `mod/` | C# (MelonLoader + Harmony) | The in-game client. Detects checks, applies received items, gates progression, talks to the AP server. Never makes logic decisions. |

They agree via **`mod/ids.json`**, generated from the apworld by `tools/export_ids.py`. It
carries the id tables plus every name and unlock map the mod needs, so the mod contains zero
hardcoded game knowledge — it deserializes that file and does table lookups.

## Architecture notes

**`what_the_car/data.py` is framework-free.** It imports only the standard library — no
`BaseClasses`, no `Options`. That lets the apworld *and* every script in `tools/` load the same
module (via `importlib.util.spec_from_file_location`) without needing an Archipelago checkout.
It is the single source of truth for names and ids, which is what stops the three consumers
from drifting apart.

**Ids are positional indices into an append-only list** covering every item and location that
*any* option combination could produce — a given seed only *creates* its subset, so ids stay
stable as options change. New content appends to the end, never inserts. `data.py` asserts at
import time that no name is duplicated, because a collision would silently shift every later id.

**World data is compiled from live in-game dumps.** The mod's dumpers sweep the game's
ScriptableObjects and write `mod/wtc_*.json`; `tools/build_levels.py` joins those into
`what_the_car/levels.json`, which `data.py` hydrates into frozen dataclasses. The raw dumps are
committed alongside so the derivation is reproducible.

## The game

| | |
|---|---|
| Engine | Unity 2022.3.69f1, IL2CPP (metadata v31) |
| Install | `…\steamapps\common\WHAT THE CAR` |
| Saves | `%USERPROFILE%\AppData\LocalLow\Triband\WHATTHECAR\` (`CarSave0-2.car`, three slots) |
| Mod loader | MelonLoader 0.7.x |

Structurally the game is a **continent of islands**, each island a themed overworld holding
levels, with collectible cards, cars, chests and bears scattered across them.

**Why MelonLoader:** WHAT THE CAR? has no existing modding scene, so there was no convention to
match and code reuse from the golf project decided it. BepInEx 6 was not ruled out on technical
grounds here — it simply offered no benefit to offset rewriting the entry point.

## Layout

```
what-the-car-ap/
├─ what_the_car/     the apworld (Python)
├─ mod/              the game mod (C#, MelonLoader)
├─ tools/            generators that bridge the two (Python, stdlib only)
└─ STATUS.md         dated engineering journal — read this first
```

## Testing the apworld

There is no automated test framework. Verification is:

1. `python -c "import data"` from `what_the_car/` — the import-time asserts catch name collisions.
2. `python tools/export_ids.py` — regenerates `mod/ids.json` cleanly.
3. Zip `what_the_car/` as an `.apworld`, drop it in an Archipelago install's `custom_worlds`,
   and generate a multi-player seed across several option combinations. A solvable fill with a
   valid playthrough is the real test.

## Licence

MIT — see [`LICENSE`](LICENSE).

Not affiliated with Triband.
