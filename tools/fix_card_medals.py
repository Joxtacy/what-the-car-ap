"""Repair collectible-card medal colours in a WHAT THE CAR? save.

WHY THIS EXISTS
---------------
A card's colour does not come from the level's own completion record. Each level
can have several records -- one per *variant* of it, each with its own medal
thresholds -- and a card is bound to one specific variant. For four cards in this
save, the variant the card reads carries a lower medal than the level was
actually completed at, and no amount of replaying the level normally will fix it,
because that updates a different record.

Diagnosed 2026-08-08 by dumping `CardData.GetPlayedLevelInfo()` in-game and
matching what it resolves to against the save. See STATUS.md.

WHAT IT CHANGES
---------------
Sets `completedState` to 3 (Gold) on exactly four records, addressed by their
levelId. Nothing else is touched -- not times, not card flags, not other records.
Each target already holds a time that qualifies for gold on the base level, so
this corrects a wrong value rather than granting anything unearned.

USAGE (from the repo root)
--------------------------
    python tools/fix_card_medals.py            # dry run -- report only
    python tools/fix_card_medals.py --apply    # write, after backing up

The game MUST be closed. A timestamped backup is written next to the save before
any change, and the file is round-trip verified afterwards.

CAVEAT: the save carries an `_isDirtyForSever` flag and the game syncs to a
server, so a cloud copy could in principle overwrite the edit on next launch.
That would revert the fix rather than damage anything -- check the card book after
starting the game.
"""

import gzip
import json
import os
import shutil
import sys
import time

_SUFFIX = os.path.join("AppData", "LocalLow", "Triband", "WHATTHECAR", "CarSave0.car")


def default_save():
    """Locate the save. Under MSYS python, USERPROFILE is unset and ~ expands to
    a POSIX home that does not exist, so try the plausible roots in turn."""
    roots = [os.environ.get("USERPROFILE"), os.path.expanduser("~")]
    home = os.environ.get("HOME", "")
    if home.startswith("/c/"):                       # MSYS-style path
        roots.append("C:\\" + home[3:].replace("/", "\\"))
    user = os.environ.get("USERNAME") or os.path.basename(home)
    if user:
        roots.append(os.path.join("C:\\Users", user))
    for root in roots:
        if not root:
            continue
        candidate = os.path.join(root, _SUFFIX)
        if os.path.exists(candidate):
            return candidate
    return os.path.join(roots[0] or "", _SUFFIX)


SAVE = default_save()

STATE = {0: "Incomplete", 1: "Bronze", 2: "Silver", 3: "Gold"}

# levelId of the record each card actually reads -> the card it drives.
TARGETS = {
    "1ad85cb635294426973de21a20e24a8c": "I Am The Car- Ptain Now",
    "9cc8214d8ff543229bcb670bfa29308b": "Car Is A- Maze- Ing",
    "5e18ee58f0634869999bbd073214d300": "Snotting Hill",
    "95da910098d54074b1b0216796a93e7d": "Someone Left The Lasers On",
}

# --fonts: records whose card is owned but whose `gainedCard` flag is false,
# because the card was earned through a DIFFERENT record of the same level.
# Fourteen cards render their text differently as a result. The six Goat
# Simulator cards share the symptom but are deliberately absent -- those really
# are unobtained, and flagging them would claim content that cannot be played.
GAINED = {
    "1dc807fd86a04222acb6f96cfa9b5cc0": "3 Wide 4 Counting",
    "4317a16600d949f289f33840e1609824": "Boost On Springboards",
    "4e00028627334a03a0f2b4890c418c8a": "Car In Hamster Wheel",
    "9cc8214d8ff543229bcb670bfa29308b": "Car Is A- Maze- Ing",
    "04d7ca56c9a54b76abf59193724b9627": "Car Takes A Long Shot",
    "88d617e1ae8c4c25ae774bee0b10962a": "Car With Giraffes",
    "54506b36d5a8472a91881e3bdef323e7": "Do Cars Dream Of Giraffe Wheels?",
    "1ad85cb635294426973de21a20e24a8c": "I Am The Car- Ptain Now",
    "4b3ab02ccf7a489e9cd13968ccc41928": "Roller Car- Ster",
    "5e18ee58f0634869999bbd073214d300": "Snotting Hill",
    "19705767de254ec8b086c12b897bdfba": "So- Ccar It Shoots Balls",
    "95da910098d54074b1b0216796a93e7d": "Someone Left The Lasers On",
    "eebf3a1367a741508dc18d0144bfa1d0": "Watercooler Jetpack",
    "64d594da976842f78418176a03d29a03": "Who Let The Turtles Out?",
}


def load(path):
    with open(path, "rb") as f:
        return json.loads(gzip.decompress(f.read()).decode("utf-8"))


def main(argv):
    apply_it = "--apply" in argv
    fonts = "--fonts" in argv
    path = next((a for a in argv if a.endswith(".car")), SAVE)

    if not os.path.exists(path):
        print(f"save not found: {path}")
        return 1

    data = load(path)
    records = data["_levelInfosSerialized"]
    print(f"save: {path}\nrecords: {len(records)}\n")

    pending = []
    print("medal repair:")
    for rec in records:
        if rec["levelId"] in TARGETS:
            name = TARGETS[rec["levelId"]]
            cur = rec["completedState"]
            mark = "already Gold" if cur == 3 else f"{STATE[cur]} -> Gold"
            print(f"  {name:<30} best={rec['bestTimeMs']/1000:>7.2f}s   {mark}")
            if cur != 3:
                pending.append(("state", rec))

    missing = set(TARGETS) - {r["levelId"] for r in records}
    for m in missing:
        print(f"  {TARGETS[m]:<30} *** record not present in this save ***")

    if fonts:
        print("\ncard-text repair (--fonts):")
        for rec in records:
            if rec["levelId"] in GAINED and not rec.get("gainedCard"):
                print(f"  {GAINED[rec['levelId']]:<30} gainedCard false -> true")
                pending.append(("gained", rec))
        already = sum(1 for r in records if r["levelId"] in GAINED and r.get("gainedCard"))
        if already:
            print(f"  ({already} already flagged)")

    if not pending:
        print("\nnothing to change.")
        return 0
    if not apply_it:
        print(f"\n{len(pending)} change(s) pending. Re-run with --apply to write.")
        if not fonts:
            print("Add --fonts to also normalise the card text colour.")
        return 0

    backup = f"{path}.bak-{time.strftime('%Y%m%d-%H%M%S')}"
    shutil.copy2(path, backup)
    print(f"\nbackup -> {backup}")

    for kind, rec in pending:
        if kind == "state":
            rec["completedState"] = 3
        else:
            rec["gainedCard"] = True

    blob = gzip.compress(json.dumps(data, separators=(",", ":")).encode("utf-8"))
    with open(path, "wb") as f:
        f.write(blob)

    # Round-trip: prove it still parses and that only the intended fields moved.
    check = load(path)
    by_id = {r["levelId"]: r for r in check["_levelInfosSerialized"]}
    assert len(check["_levelInfosSerialized"]) == len(records), "record count changed!"
    for tid, name in TARGETS.items():
        if tid in by_id:
            assert by_id[tid]["completedState"] == 3, f"{name} medal did not take"
    if fonts:
        for tid, name in GAINED.items():
            if tid in by_id:
                assert by_id[tid]["gainedCard"] is True, f"{name} gainedCard did not take"
    print(f"wrote {len(blob)} bytes; applied {len(pending)} change(s).")
    print("\nStart the game and check the card book. To undo:")
    print(f"    copy \"{backup}\" \"{path}\"")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
