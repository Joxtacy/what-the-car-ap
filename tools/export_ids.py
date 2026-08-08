"""Export the apworld's id tables and name maps to mod/ids.json.

This file IS the apworld<->mod contract. The mod hardcodes no game knowledge; it
deserialises this and does table lookups, so the two halves can never disagree
about what a location is called or which id it carries.

data.py is framework-free, so it loads here without an Archipelago checkout.

Run from the repo root (the `python` on PATH is MSYS and cannot take Windows
absolute paths):

    python tools/export_ids.py
"""

import importlib.util
import json
import os
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA_PY = os.path.join(REPO, "what_the_car", "data.py")
OUT = os.path.join(REPO, "mod", "ids.json")


def load_data():
    spec = importlib.util.spec_from_file_location("wtc_data", DATA_PY)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main() -> int:
    data = load_data()

    # contentId -> display name. The game reports a raw contentId; the mod
    # translates it here before appending " - Clear" / " - Silver" / " - Gold".
    name_by_content = {l.id: l.display for l in data.LEVELS}
    island_by_content = {l.id: l.island for l in data.LEVELS}
    overworld_by_content = {l.id: l.overworld for l in data.LEVELS}

    # Access item -> the game's gate id, so the mod knows which native key to grant.
    # Under `separate` several overworlds share one gate id; granting it opens all
    # of them. That looseness is documented in Options.OverworldAccess.
    gate_by_item = {}
    for ow in data.OVERWORLDS:
        if data.is_start(ow):
            continue
        gate_by_item[data.access_item(ow.key)] = ow.gate_id
    bear_by_item = {data.bear_item(k): data.OVERWORLD_BY_KEY[k].bear_id
                    for k in data.bear_awarding_overworlds()}

    payload = {
        "game": "WHAT THE CAR?",
        "items": data.item_name_to_id,
        "locations": data.location_name_to_id,
        "name_by_content": name_by_content,
        "island_by_content": island_by_content,
        "overworld_by_content": overworld_by_content,
        "gate_by_item": gate_by_item,
        "bear_by_item": bear_by_item,
        "overworlds": [
            {
                "key": o.key,
                "display": o.display,
                "gate_id": o.gate_id,
                "bear_id": o.bear_id,
                "requires": list(o.requires),
                "completion_location": data.complete_loc(o),
            }
            for o in data.OVERWORLDS
        ],
        "final_overworld": data.final_overworld().key,
    }

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(payload, f, indent=2, ensure_ascii=False)
        f.write("\n")

    print(f"wrote {os.path.relpath(OUT, REPO)}: "
          f"{len(payload['items'])} items, {len(payload['locations'])} locations, "
          f"{len(name_by_content)} levels, {len(payload['overworlds'])} overworlds")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
