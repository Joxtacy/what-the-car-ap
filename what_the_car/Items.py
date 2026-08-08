from BaseClasses import Item, ItemClassification as IC

from .data import FILLER_ITEMS, access_item_names, bear_item_names, item_name_to_id


class WTCItem(Item):
    game = "WHAT THE CAR?"


_PROGRESSION = frozenset(access_item_names()) | frozenset(bear_item_names())


def item_classification(name: str) -> IC:
    """Access keys and bears open overworlds; everything else is filler."""
    if name in _PROGRESSION:
        return IC.progression
    return IC.filler


def create_item(player: int, name: str) -> WTCItem:
    return WTCItem(name, item_classification(name), item_name_to_id[name], player)


__all__ = ["WTCItem", "item_classification", "create_item", "FILLER_ITEMS"]
