from worlds.generic.Rules import set_rule

from . import data
from .data import ACCESS_BEARS, VICTORY, bear_item, complete_loc, gates


def set_rules(world) -> None:
    multiworld = world.multiworld
    player = world.player
    options = world.options

    mode = (ACCESS_BEARS if options.overworld_access == options.overworld_access.option_bears
            else data.ACCESS_SEPARATE)

    for name, access, _levels in gates(mode):
        if access is None:
            continue    # the starting overworld is always open
        entrance = multiworld.get_entrance(f"To {name}", player)
        # k= binds the loop variable; without it every lambda would close over the
        # last value and all regions would demand the same key.
        set_rule(entrance, lambda state, k=access: state.has(k, player))

    goal = options.goal
    if goal == goal.option_campaign:
        multiworld.completion_condition[player] = \
            lambda state: state.has(VICTORY, player)

    elif goal == goal.option_all_overworlds:
        # Reaching every overworld's region means holding every access key. Using
        # can_reach_region (rather than the completion locations, which the
        # overworld_completions option can switch off) keeps this goal valid under
        # every other option combination.
        keys = [o.key for o in data.OVERWORLDS]
        multiworld.completion_condition[player] = \
            lambda state, ks=keys: all(state.can_reach_region(k, player) for k in ks)

    else:   # all_bears
        # Bears are awarded for completing an overworld. Under `separate` access
        # they are not items at all, so fall back to the equivalent reachability
        # test rather than requiring items that this seed never generated.
        if mode == ACCESS_BEARS:
            bears = [bear_item(k) for k in data.bear_awarding_overworlds()]
            multiworld.completion_condition[player] = \
                lambda state, bs=bears: all(state.has(b, player) for b in bs)
        else:
            awarders = data.bear_awarding_overworlds()
            multiworld.completion_condition[player] = \
                lambda state, ks=awarders: all(state.can_reach_region(k, player) for k in ks)
