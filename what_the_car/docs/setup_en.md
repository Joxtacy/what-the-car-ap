# WHAT THE CAR? Archipelago Setup Guide

> **This world is under construction.** It generates seeds, but the game mod does not yet apply
> received items or gate progression — so a seed is not playable end to end. This guide describes
> what exists today and will grow as the mod does.

## Required software

- **WHAT THE CAR?** on Steam (appid `2727650`)
- **[MelonLoader](https://melonloader.co/) 0.7.3** or newer
- The **`what_the_car.apworld`** and the **WtcArchipelago** mod, from
  [the releases page](https://github.com/Joxtacy/what-the-car-ap/releases)

## Installing

1. Install MelonLoader into the game folder (`…\steamapps\common\WHAT THE CAR`) and launch the
   game once. The first launch generates interop assemblies and takes a couple of minutes.
2. Copy `WtcArchipelago.dll` into `<game>\Mods\` — the **game root** `Mods` folder, not
   `MelonLoader\Mods`.
3. Copy `Archipelago.MultiClient.Net.dll` into `<game>\UserLibs\`.
4. Copy `wtc_ids.json` into the game root.
5. Put `what_the_car.apworld` in your Archipelago install's `custom_worlds` folder.

## Use a fresh save slot

**Start an Archipelago run on an empty save slot.** The game has three, and the mod reports your
*best ever* result on a level, not the current run's — so on a save that already has medals, every
check would fire the moment you touch a level. A fresh slot is also the only way progression
gating can be meaningful.

Your existing saves are never modified by generation, but back up
`%USERPROFILE%\AppData\LocalLow\Triband\WHATTHECAR\` before playing anyway.

## What gets randomised

**Checks** come from levels and overworlds:

- **Clear** — finishing a level. Always on.
- **Silver** and **Gold** — the level's medal times, controlled by the `medals` option.
- **Overworld Complete** — finishing an overworld, where the game gives you its bear.

**Items** are the keys to the ten overworlds. How many there are depends on `overworld_access`:

- `separate` (default) — one key per overworld, nine in total.
- `bears` — the game's own progression, where completing an overworld awards a bear that opens the
  next. Five keys, a more linear and more faithful run.

## The overworlds

Progression is a chain with a branch, mirroring the real game:

```
Jumping ──▶ Jobs ──▶ Soccer ──▶ Long ──▶ Wheels ──▶ Beach
                        └────▶ Storm
             └──▶ Among CAR, Goat Simulator, Sneaky Sasquatch
```

Jumping is always open. Beach is the end of the main chain; the three side overworlds branch off
early and are optional unless your goal requires them.

## Goals

| Goal | Win condition |
|---|---|
| `campaign` (default) | Complete Beach, at the end of the main chain. The shortest goal. |
| `all_overworlds` | Complete all ten, side branches included. |
| `all_bears` | Collect every bear. |

## Options

| Option | Values | Default |
|---|---|---|
| `goal` | `campaign`, `all_overworlds`, `all_bears` | `campaign` |
| `overworld_access` | `separate`, `bears` | `separate` |
| `medals` | `clear_only`, `clear_and_gold`, `all_medals` | `clear_only` |
| `overworld_completions` | on / off | on |
| `death_link` | on / off | off |

`medals` drives the length of your game more than anything else: 183 locations for `clear_only`,
356 for `clear_and_gold`, 529 for `all_medals`.

### Logic versus physical access

With `overworld_access: separate`, the game gates **Jobs, Among CAR, Goat Simulator and Sneaky
Sasquatch behind a single shared key**. Receiving any one of those four keys therefore physically
opens all four, even though logic only expects the one you were given.

This can only ever let you reach a check *earlier* than logic requires — it can never make a seed
unwinnable. If you would rather the game match the logic exactly, choose `overworld_access: bears`,
which has no such looseness.
