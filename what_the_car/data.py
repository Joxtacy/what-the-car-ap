"""Single source of truth for the WHAT THE CAR? apworld.

FRAMEWORK-FREE ON PURPOSE: this module imports only the standard library -- no
BaseClasses, no Options. That lets the apworld *and* every script in tools/ load
it via importlib without an Archipelago checkout, which is what stops names and
ids drifting between the apworld, the mod's ids.json, and any future tracker pack.

Everything is hydrated from levels.json, which tools/build_levels.py compiles from
the in-game dumps. Nothing about the game is hardcoded here except display names
and the option vocabulary.
"""

import json
import os
from dataclasses import dataclass
from typing import Dict, List, Optional, Tuple

# Steam appid 2727650 x 100, matching the convention the WHAT THE GOLF? world uses
# (785790 -> 78579000). Locations sit 5000 above items, which leaves ample room for
# the item table to grow without ever colliding.
BASE_ID = 272765000
LOC_BASE = BASE_ID + 5000

_LEVELS_PATH = os.path.join(os.path.dirname(__file__), "levels.json")


def _read_levels_json() -> dict:
    """Read levels.json, working both on disk and inside a zipped .apworld.

    zipimport makes the package importable but leaves open() unable to see the
    file, so fall back to pkgutil, which reads through the zip.
    """
    if os.path.exists(_LEVELS_PATH):
        with open(_LEVELS_PATH, encoding="utf-8") as f:
            return json.load(f)
    import pkgutil
    raw = pkgutil.get_data(__name__, "levels.json")
    if raw is None:
        raise FileNotFoundError("levels.json not found on disk or in the apworld zip")
    return json.loads(raw.decode("utf-8"))


@dataclass(frozen=True)
class Level:
    id: str            # the game's contentId -- the mod's join key
    display: str       # human name; the basis of every location name
    overworld: str     # owning overworld key, e.g. "JUMPING"
    island: str
    subtype: str       # Normal / Hard / Intermezzo
    silver: float
    gold: float


@dataclass(frozen=True)
class Overworld:
    key: str           # "JUMPING"
    display: str       # "Jumping"
    gate_id: str       # the game's requiredIdToAccess
    bear_id: Optional[str]     # == completion_id; the key it awards on completion
    requires: Tuple[str, ...]  # overworld keys whose bear opens this one
    levels: Tuple[str, ...]


def _load():
    raw = _read_levels_json()
    overworlds = tuple(
        Overworld(
            key=o["key"],
            display=o["display"],
            gate_id=o["gate_id"],
            bear_id=o.get("bear_id"),
            requires=tuple(o["requires"]),
            levels=tuple(o["levels"]),
        )
        for o in raw["overworlds"]
    )
    levels = tuple(
        Level(
            id=l["id"],
            display=l["display"],
            overworld=l["overworld"],
            island=l["island"],
            subtype=l["subtype"],
            silver=l.get("silver") or 0.0,
            gold=l.get("gold") or 0.0,
        )
        for l in raw["levels"]
    )
    return overworlds, levels, tuple(raw["start_overworlds"]), tuple(raw["final_overworlds"])


OVERWORLDS, LEVELS, START_OVERWORLDS, FINAL_OVERWORLDS = _load()

OVERWORLD_BY_KEY: Dict[str, Overworld] = {o.key: o for o in OVERWORLDS}
LEVEL_BY_ID: Dict[str, Level] = {l.id: l for l in LEVELS}

# --- option vocabulary -------------------------------------------------------

ACCESS_SEPARATE = "separate"   # one key per overworld (9)
ACCESS_BEARS = "bears"         # the game's own bear chain (5)

VICTORY = "Victory"

# --- names -------------------------------------------------------------------


def clear_loc(level: Level) -> str:
    return f"{level.display} - Clear"


def silver_loc(level: Level) -> str:
    return f"{level.display} - Silver"


def gold_loc(level: Level) -> str:
    return f"{level.display} - Gold"


def complete_loc(ow: Overworld) -> str:
    return f"{ow.display} - Complete"


def access_item(ow_key: str) -> str:
    """Per-overworld key (ACCESS_SEPARATE granularity)."""
    return f"{OVERWORLD_BY_KEY[ow_key].display} Access"


def bear_item(ow_key: str) -> str:
    """The bear an overworld awards -- the game's own key (ACCESS_BEARS)."""
    return f"{OVERWORLD_BY_KEY[ow_key].display} Bear"


def is_start(ow: Overworld) -> bool:
    return not ow.requires


# Overworlds that award a bear another overworld actually needs. Only these
# become items under ACCESS_BEARS -- a bear nothing is gated on would be a
# progression item that unlocks nothing.
def bear_awarding_overworlds() -> List[str]:
    needed = {r for o in OVERWORLDS for r in o.requires}
    return sorted(needed)


def access_item_names() -> List[str]:
    return [access_item(o.key) for o in sorted(OVERWORLDS, key=lambda x: x.key)
            if not is_start(o)]


def bear_item_names() -> List[str]:
    return [bear_item(k) for k in bear_awarding_overworlds()]


FILLER_ITEMS: Tuple[str, ...] = (
    "Honk",
    "Spare Wheel",
    "Traffic Cone",
    "Air Freshener",
    "Loose Screw",
    "Parking Ticket",
)

# --- the id universe ---------------------------------------------------------
# Ids are positional indices into an APPEND-ONLY list covering every item and
# location that ANY option combination can produce. A seed only *creates* its
# subset, so ids stay stable as options change. New content appends to the END --
# inserting would shift every later id and invalidate existing seeds.


def all_item_names() -> List[str]:
    return list(access_item_names()) + list(bear_item_names()) + list(FILLER_ITEMS)


def all_location_names() -> List[str]:
    names: List[str] = []
    ordered = sorted(LEVELS, key=lambda l: (l.overworld, l.display))
    names += [clear_loc(l) for l in ordered]
    names += [silver_loc(l) for l in ordered]
    names += [gold_loc(l) for l in ordered]
    names += [complete_loc(o) for o in sorted(OVERWORLDS, key=lambda x: x.key)]
    return names


_all_items = all_item_names()
_all_locs = all_location_names()

# A duplicate name would silently collapse two entries and shift every later id,
# corrupting seeds in a way that is very hard to trace. Fail at import instead.
assert len(_all_items) == len(set(_all_items)), \
    f"duplicate item name(s): {sorted(n for n in _all_items if _all_items.count(n) > 1)}"
assert len(_all_locs) == len(set(_all_locs)), \
    f"duplicate location name(s): {sorted(n for n in _all_locs if _all_locs.count(n) > 1)}"

item_name_to_id: Dict[str, int] = {n: BASE_ID + i for i, n in enumerate(_all_items)}
location_name_to_id: Dict[str, int] = {n: LOC_BASE + i for i, n in enumerate(_all_locs)}


# --- region helpers ----------------------------------------------------------


def gates(mode: str) -> List[Tuple[str, Optional[str], List[Level]]]:
    """(region name, access item or None, levels) for the chosen granularity.

    Both modes use one region per overworld; they differ only in which item opens
    it. Under ACCESS_BEARS several overworlds share a bear, so one item opens all
    of them -- see the looseness note in Rules.
    """
    out = []
    for ow in sorted(OVERWORLDS, key=lambda x: x.key):
        levels = [LEVEL_BY_ID[i] for i in ow.levels if i in LEVEL_BY_ID]
        if is_start(ow):
            out.append((ow.key, None, levels))
        elif mode == ACCESS_BEARS:
            # Gated on whichever overworld awards this one's gate id.
            out.append((ow.key, bear_item(ow.requires[0]), levels))
        else:
            out.append((ow.key, access_item(ow.key), levels))
    return out


def final_overworld() -> Overworld:
    """The campaign's end: the deepest overworld of the main chain.

    AMONGCAR/GOAT/SNEAKY are also leaves, but they branch straight off JUMPING's
    bear, so BEACH -- at the end of JOB -> SOCCER -> LONG -> WHEELS -- is the real
    finale. Picked by chain depth rather than hardcoded.
    """
    depth: Dict[str, int] = {}

    def walk(key: str) -> int:
        if key in depth:
            return depth[key]
        ow = OVERWORLD_BY_KEY[key]
        depth[key] = 0 if is_start(ow) else 1 + max(walk(r) for r in ow.requires)
        return depth[key]

    for o in OVERWORLDS:
        walk(o.key)
    return OVERWORLD_BY_KEY[max(depth, key=lambda k: (depth[k], k))]
