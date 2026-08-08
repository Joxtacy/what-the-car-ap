from dataclasses import dataclass

from Options import Choice, DeathLink, PerGameCommonOptions, Toggle


class Goal(Choice):
    """What finishing the randomizer means.

    campaign: complete the final overworld of the main chain (Beach, at the end of
    Jumping -> Jobs -> Soccer -> Long -> Wheels). The side overworlds -- Among CAR,
    Goat Simulator and Sneaky Sasquatch -- branch off early and are optional, so
    this is the shortest of the three goals.

    all_overworlds: complete all ten overworlds, side branches included. Forces
    every access key into logic rather than just the main chain's.

    all_bears: collect every bear. In-game a bear is awarded for completing an
    overworld, so this is close to all_overworlds, but it is checked against the
    items you hold rather than the overworlds you have cleared.
    """
    display_name = "Goal"
    option_campaign = 0
    option_all_overworlds = 1
    option_all_bears = 2
    default = 0


class OverworldAccess(Choice):
    """How finely overworld access is randomised.

    separate: every overworld gets its own key -- nine progression items. More
    items in the pool and a more interesting seed.

    bears: use the game's own progression, where completing an overworld awards a
    bear that unlocks the next -- five progression items. Fewer, heavier keys and
    a more faithful, more linear run.

    A caveat for `separate`: the game gates Jobs, Among CAR, Goat Simulator and
    Sneaky Sasquatch behind a SINGLE shared key, so unlocking any one of the four
    physically opens all four. Logic still expects the right key, so this only
    means you may be able to reach a check earlier than logic requires -- never
    that a seed becomes unwinnable. Choose `bears` if that bothers you.
    """
    display_name = "Overworld Access"
    option_separate = 0
    option_bears = 1
    default = 0


class Medals(Choice):
    """Which per-level medals become checks.

    Every level is timed and awards Bronze, Silver or Gold. `clear_only` gives one
    check per level (183 total). `clear_and_gold` adds a second, demanding check
    per level. `all_medals` adds Silver as well, for three checks per level and 529
    locations -- a much longer game.

    Medals are cumulative, so earning Gold also awards Silver and Clear.
    """
    display_name = "Medals"
    option_clear_only = 0
    option_clear_and_gold = 1
    option_all_medals = 2
    default = 0


class OverworldCompletions(Toggle):
    """Add a check for completing each overworld (ten extra locations).

    These sit where the game hands you that overworld's bear.
    """
    display_name = "Overworld Completion Checks"
    default = 1


@dataclass
class WTCOptions(PerGameCommonOptions):
    goal: Goal
    overworld_access: OverworldAccess
    medals: Medals
    overworld_completions: OverworldCompletions
    death_link: DeathLink
