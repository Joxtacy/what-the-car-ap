from typing import Any, Dict

from BaseClasses import Tutorial
from worlds.AutoWorld import WebWorld, World

from . import Regions, Rules, data
from .Items import WTCItem, create_item, item_classification
from .Locations import WTCLocation
from .Options import WTCOptions
from .data import (
    ACCESS_BEARS,
    FILLER_ITEMS,
    access_item_names,
    bear_item_names,
    item_name_to_id,
    location_name_to_id,
)


class WTCWeb(WebWorld):
    theme = "partyTime"
    tutorials = [Tutorial(
        "Multiworld Setup Guide",
        "A guide to playing WHAT THE CAR? in Archipelago.",
        "English",
        "setup_en.md",
        "setup/en",
        ["Joxtacy"],
    )]


class WTCWorld(World):
    """WHAT THE CAR? is a game about a car. It has legs. Drive through ten
    overworlds of absurd, short, physics-driven levels, chasing medals and
    rescuing bears."""

    game = "WHAT THE CAR?"
    web = WTCWeb()
    options_dataclass = WTCOptions
    options: WTCOptions
    topology_present = True

    # Universal Tracker: without this UT tries to match the connected slot against
    # YAMLs in its Players folder and errors unless the seed's yaml is physically
    # there. With it, UT regenerates from slot data alone.
    ut_can_gen_without_yaml = True

    item_name_to_id = item_name_to_id
    location_name_to_id = location_name_to_id

    item_name_groups = {
        "Access": set(access_item_names()),
        "Bears": set(bear_item_names()),
    }

    # --- generation ---------------------------------------------------------

    def create_regions(self) -> None:
        Regions.create_regions(self)

    def set_rules(self) -> None:
        Rules.set_rules(self)

    def create_item(self, name: str) -> WTCItem:
        return create_item(self.player, name)

    def create_items(self) -> None:
        pool = []

        mode = (ACCESS_BEARS
                if self.options.overworld_access == self.options.overworld_access.option_bears
                else data.ACCESS_SEPARATE)
        names = bear_item_names() if mode == ACCESS_BEARS else access_item_names()
        pool.extend(self.create_item(n) for n in names)

        # Pad to exactly the number of real (addressed) locations. Event locations
        # carry their own locked item and must not be counted.
        total = sum(1 for loc in self.multiworld.get_locations(self.player)
                    if loc.address is not None)
        # Cycle deterministically and use self.random (not the module-level random)
        # so Universal Tracker's regeneration reproduces an identical pool.
        for i in range(total - len(pool)):
            pool.append(self.create_item(FILLER_ITEMS[i % len(FILLER_ITEMS)]))

        self.multiworld.itempool += pool

    def get_filler_item_name(self) -> str:
        return self.random.choice(FILLER_ITEMS)

    # --- slot data / Universal Tracker --------------------------------------

    def fill_slot_data(self) -> Dict[str, Any]:
        o = self.options
        return {
            "goal": o.goal.value,
            "overworld_access": (ACCESS_BEARS
                                 if o.overworld_access == o.overworld_access.option_bears
                                 else data.ACCESS_SEPARATE),
            "medals": o.medals.value,
            "overworld_completions": bool(o.overworld_completions),
            "death_link": bool(o.death_link),
        }

    def generate_early(self) -> None:
        # Present ONLY under a Universal Tracker regeneration. UT rebuilds the world
        # from an empty yaml and carries the real options through here, so applying
        # slot data in interpret_slot_data alone is not enough -- that runs against
        # a throwaway world which the authoritative regen then discards.
        passthrough = getattr(self.multiworld, "re_gen_passthrough", None)
        if passthrough and self.game in passthrough:
            self._apply_slot_data(passthrough[self.game])

    def interpret_slot_data(self, slot_data: Dict[str, Any]) -> Dict[str, Any]:
        self._apply_slot_data(slot_data)
        return slot_data    # truthy -> tells UT to regenerate with re_gen_passthrough

    def _apply_slot_data(self, slot_data: Dict[str, Any]) -> None:
        o = self.options
        if "goal" in slot_data:
            o.goal.value = int(slot_data["goal"])
        if "overworld_access" in slot_data:
            o.overworld_access.value = (
                o.overworld_access.option_bears
                if slot_data["overworld_access"] == ACCESS_BEARS
                else o.overworld_access.option_separate)
        if "medals" in slot_data:
            o.medals.value = int(slot_data["medals"])
        if "overworld_completions" in slot_data:
            o.overworld_completions.value = int(bool(slot_data["overworld_completions"]))
        if "death_link" in slot_data:
            o.death_link.value = int(bool(slot_data["death_link"]))
