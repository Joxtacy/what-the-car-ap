"""Compile the in-game dumps into the apworld's levels.json.

Inputs (captured by mod/src/Mapping/Dumpers.cs, committed under mod/):
    mod/wtc_overworlds.json   Speed.OverworldData -- THE authoritative structure
    mod/wtc_levels.json       Speed.NormalLevelDef -- per-level metadata
    mod/wtc_islands.json      live Island MonoBehaviours (cross-check only)
    mod/wtc_accesspoints.json live BaseAccessPoint (cross-check only)

Output:
    what_the_car/levels.json

Only `wtc_overworlds.json` and `wtc_levels.json` are load-bearing. The other two
come from live scene objects that populate only once the player physically drives
up to them, so they are incomplete by nature and are used to report discrepancies,
never to define the world.

The campaign is exactly the levels reachable from an overworld whose
`pack == MainGame`. That is a better filter than gameplay subtype, which lets
daily levels through -- the dailies are NormalLevelDefs attached to no overworld.

stdlib only, so it runs without an Archipelago checkout. Run from the repo root
(the `python` on PATH here is MSYS and cannot take Windows absolute paths):

    python tools/build_levels.py            # report only
    python tools/build_levels.py --write    # write what_the_car/levels.json
"""

import json
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MOD = os.path.join(REPO, "mod")
OUT = os.path.join(REPO, "what_the_car", "levels.json")

# Overworld asset name -> (key, display). Keys are stable and become part of AP
# item/location names, so they must never change once a seed has been generated.
OVERWORLDS = {
    "Overworld - JUMPING": ("JUMPING", "Jumping"),
    "Overworld - JOB": ("JOB", "Jobs"),
    "Overworld - SOCCER": ("SOCCER", "Soccer"),
    "Overworld - LONG": ("LONG", "Long"),
    "Overworld - STORM": ("STORM", "Storm"),
    "Overworld - WHEELS": ("WHEELS", "Wheels"),
    "Overworld - BEACH": ("BEACH", "Beach"),
    "OverworldDungeon-AmongCAR": ("AMONGCAR", "Among CAR"),
    "OverworldDungeon-GoatSimulatorCollab": ("GOAT", "Goat Simulator"),
    "OverworldDungeon-SneakySasquatch": ("SNEAKY", "Sneaky Sasquatch"),
}


def read(name):
    with open(os.path.join(MOD, name), encoding="utf-8") as f:
        return json.load(f)


def slug(text):
    """Stable key from an island's asset name: 'Just Jumping Island' -> 'just_jumping'."""
    text = re.sub(r"\bIsland\b", "", text or "").strip()
    text = re.sub(r"[^A-Za-z0-9]+", "_", text).strip("_").lower()
    return text or "unnamed"


def build():
    overworlds_raw = read("wtc_overworlds.json")
    levels_raw = read("wtc_levels.json")

    main = {k: v for k, v in overworlds_raw.items() if v.get("pack") == "MainGame"}
    unknown = sorted({v["assetName"] for v in main.values()} - set(OVERWORLDS))
    if unknown:
        raise SystemExit(
            "Unrecognised MainGame overworld(s): " + ", ".join(unknown)
            + "\nAdd them to OVERWORLDS (a new one shifts nothing, but it must be named)."
        )

    # --- the gate graph -----------------------------------------------------
    # Every overworld has a requiredIdToAccess (its gate) and a completionId that
    # equals its givesBear: finishing an overworld awards a bear, and that bear is
    # the key to whatever it gates. So `requires` is just "who awards my gate id".
    awarder = {}
    for v in main.values():
        completion = v.get("completionId")
        if completion:
            awarder.setdefault(completion, []).append(OVERWORLDS[v["assetName"]][0])

    # A few levels are referenced by more than one overworld, so ownership has to
    # be decided somewhere -- and it must be decided DETERMINISTICALLY. Iterating
    # the dump's own key order would hand ownership to whichever overworld the
    # capture happened to see first, which changes each run and would silently
    # shift every downstream location id. Sort by overworld key instead.
    overworlds, level_owner, shared = [], {}, {}
    for v in sorted(main.values(), key=lambda o: OVERWORLDS[o["assetName"]][0]):
        key, display = OVERWORLDS[v["assetName"]]
        gate = v.get("requiredIdToAccess")

        islands, ordered_levels = [], []
        for isl in v["islands"]:
            ikey = slug(isl.get("name"))
            for content in isl["levels"]:
                if content in level_owner:
                    shared.setdefault(content, [level_owner[content][0]]).append(key)
                    continue
                level_owner[content] = (key, ikey)
                ordered_levels.append(content)
            islands.append({
                "key": ikey,
                "display": (isl.get("name") or "Unnamed").strip(),
                "levels": list(isl["levels"]),
            })

        overworlds.append({
            "key": key,
            "display": display,
            "asset": v["assetName"],
            "gate_id": gate,
            "completion_id": v.get("completionId"),
            "bear_id": v.get("givesBear"),
            # Overworld keys whose bear opens this one. Empty == the start.
            "requires": sorted(awarder.get(gate, [])),
            "achievement": v.get("progressAchievement"),
            "gold_achievement": v.get("goldAchievement"),
            "islands": islands,
            "levels": ordered_levels,
        })

    overworlds.sort(key=lambda o: o["key"])

    # --- levels -------------------------------------------------------------
    # AP location names must be globally unique. Level names are human-written and
    # DO collide across overworlds, so a colliding name gets its overworld as a
    # prefix -- the same trick golf uses for episode holes.
    counts = {}
    for content in level_owner:
        name = (levels_raw[content].get("levelName") or "").strip()
        counts[name] = counts.get(name, 0) + 1

    levels, seen = [], set()
    for content, (ow_key, island_key) in level_owner.items():
        meta = levels_raw[content]
        name = (meta.get("levelName") or "").strip() or meta.get("debugTitle") or content
        display = f"{ow_key}: {name}" if counts.get(name, 0) > 1 else name
        if display in seen:      # pathological: same name twice in one overworld
            display = f"{display} ({content[:6]})"
        seen.add(display)

        levels.append({
            "id": content,
            "display": display,
            "name": name,
            "debug": meta.get("debugTitle"),
            "overworld": ow_key,
            "island": island_key,
            "subtype": meta.get("gameplaySubType"),
            "silver": meta.get("silverTime"),
            "gold": meta.get("goldTime"),
        })
    levels.sort(key=lambda l: (l["overworld"], l["display"]))

    start = [o["key"] for o in overworlds if not o["requires"]]
    gated = {r for o in overworlds for r in o["requires"]}
    final = [o["key"] for o in overworlds if o["key"] not in gated]

    return {
        "start_overworlds": sorted(start),
        "final_overworlds": sorted(final),
        "overworlds": overworlds,
        "levels": levels,
        # Levels reachable from more than one overworld. Ownership went to the
        # alphabetically-first key; recorded so the apworld can widen the access
        # rule later rather than silently under-gating.
        "shared_levels": {k: sorted(set(v)) for k, v in sorted(shared.items())},
    }


def report(world):
    print(f"overworlds: {len(world['overworlds'])}   levels: {len(world['levels'])}")
    print(f"start: {world['start_overworlds']}   final: {world['final_overworlds']}")
    print()
    for o in world["overworlds"]:
        req = ", ".join(o["requires"]) or "-- START --"
        print(f"  {o['key']:<10} {len(o['levels']):>3} levels  {len(o['islands']):>2} islands"
              f"   requires: {req}")

    if world["shared_levels"]:
        print(f"\n{len(world['shared_levels'])} level(s) appear in more than one overworld"
              " (owned by the alphabetically-first, recorded in shared_levels):")
        for content, keys in list(world["shared_levels"].items())[:10]:
            print(f"  {content[:10]}...  {' + '.join(keys)}")

    # Cross-check against the live-scene dumps. These are expected to be a subset;
    # anything they know that OverworldData does not would mean the authoritative
    # source is incomplete, which is worth shouting about.
    try:
        live = {l for v in read("wtc_islands.json").values() for l in v.get("levels", [])}
        live |= {l for v in read("wtc_accesspoints.json").values() for l in v.get("levels", [])}
    except FileNotFoundError:
        return
    known = {l["id"] for l in world["levels"]}
    extra = live - known
    print(f"\nlive-scene dumps knew {len(live)} levels; {len(live & known)} are in the campaign.")
    if extra:
        print(f"  NOTE {len(extra)} level(s) seen live but not under any MainGame overworld")
        print("  (expected: dailies, UGC and seasonal content live in HomeDungeon overworlds).")


def main(argv):
    world = build()
    report(world)
    if "--write" in argv:
        os.makedirs(os.path.dirname(OUT), exist_ok=True)
        with open(OUT, "w", encoding="utf-8") as f:
            json.dump(world, f, indent=2, ensure_ascii=False)
            f.write("\n")
        print(f"\nwrote {os.path.relpath(OUT, REPO)}")
    else:
        print("\n(report only -- pass --write to emit levels.json)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
