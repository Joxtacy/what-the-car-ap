from BaseClasses import ItemClassification, Region

from . import data
from .Items import WTCItem
from .Locations import WTCLocation
from .data import (
    ACCESS_BEARS,
    VICTORY,
    clear_loc,
    complete_loc,
    gates,
    gold_loc,
    location_name_to_id,
    silver_loc,
)


def _add(region: Region, name: str) -> None:
    region.locations.append(
        WTCLocation(region.player, name, location_name_to_id[name], region))


def create_regions(world) -> None:
    """One region per overworld, each hung directly off Menu.

    The overworlds form a chain in-game (Jumping -> Jobs -> Soccer -> ...), but the
    region graph is deliberately FLAT: every region connects straight to Menu and
    is gated only by its own key. Chaining the regions as well would double-count
    the requirement -- an entrance rule already expresses "you need this key", and
    the key itself is what the fill algorithm places. Golf's world is laid out the
    same way for the same reason.
    """
    multiworld = world.multiworld
    player = world.player
    options = world.options

    menu = Region("Menu", player, multiworld)
    multiworld.regions.append(menu)

    mode = (ACCESS_BEARS if options.overworld_access == options.overworld_access.option_bears
            else data.ACCESS_SEPARATE)
    medals = options.medals
    want_silver = medals == medals.option_all_medals
    want_gold = medals in (medals.option_all_medals, medals.option_clear_and_gold)

    for name, access, levels in gates(mode):
        region = Region(name, player, multiworld)
        multiworld.regions.append(region)

        for level in levels:
            _add(region, clear_loc(level))
            if want_silver:
                _add(region, silver_loc(level))
            if want_gold:
                _add(region, gold_loc(level))

        if options.overworld_completions:
            _add(region, complete_loc(data.OVERWORLD_BY_KEY[name]))

        menu.connect(region, f"To {name}")

    # Victory is an event: no address, holding a locked item the completion
    # condition tests for. Placed in the finale region so reaching it implies
    # everything the finale needs.
    final = data.final_overworld()
    final_region = multiworld.get_region(final.key, player)
    victory = WTCLocation(player, "Campaign Complete", None, final_region)
    victory.place_locked_item(
        WTCItem(VICTORY, ItemClassification.progression, None, player))
    final_region.locations.append(victory)
