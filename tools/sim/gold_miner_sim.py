"""
Headless simulator for the gold-miner game, for experiment-driven map and parameter tuning.

The rules and default numbers mirror the Unity scripts in Assets/Scripts:
  Hook.cs        extend 12, retract 16 x weight factor, circle-cast radius 0.2,
                 stops at the first treasure / rock / pig, an empty hook still reacts on the way back
  MinerAI.cs     start delay 1.5 s, telegraph 1-2 s, cooldown 0.8 s, overshoot 0.6,
                 2-3 treasure shots then one shot at the pig
  GameManager.cs 90 s round, 0 starting lives, 1.5 s invincibility,
                 ends on time-out / all treasure gone / pig killed; player wins if alive and richer
  MrHydeController speed 5, collects treasure and Life pickups by touching them, blocked by rocks

Every gameplay number lives in GameParams, so the tuner can change agent speeds, waiting
periods and so on, not only the map. GameParams also adds two things Unity does not have yet:
  digest_time    after eating gold the pig cannot eat other gold for
                 digest_time + digest_per_value x value seconds
  hook_kills     False = pure collection race: the hook ignores the pig and the miner never aims at it

Differences from Unity (keep in mind when reading results):
  - The pig is clamped to the camera view; Game.unity currently has no walls.
  - Physics is simplified to circles; time step defaults to 0.02 s (Unity's fixed step).

Pig policies:
  greedy   value / distance, ignores the miner (the original baseline)
  race     gold the miner reaches before us is worth 0; take the most valuable of the rest
  margin   forecasts the miner's schedule, counts gold the miner would otherwise take twice
           (+v for us, -v for it) and searches a few steps ahead with time discounting
  oracle   (compare only) exhaustive search over collection orders with full knowledge of the
           match's random numbers: an upper bound for policies that walk from gold to gold

Usage (Python 3.8+, no extra packages):
  python gold_miner_sim.py eval    --map current --episodes 200
  python gold_miner_sim.py eval    --map gold --n-gold 8 --pig margin --digest 1.0 --no-kill
  python gold_miner_sim.py tune    --map random --map-seed 7 --iters 40 --tune-params all --out tuned.json
  python gold_miner_sim.py eval    --map tuned.json --miner smart
  python gold_miner_sim.py compare --n-gold 6 --maps 20 --seeds 5 --no-kill --csv results.csv

Plug in trained agents by subclassing PigAgent / MinerAgent (see the baseline agents below).
"""

import argparse
import copy
import csv
import dataclasses
import itertools
import json
import math
import random
from dataclasses import dataclass, field, asdict
from typing import Dict, List, Optional, Tuple

Vec = Tuple[float, float]

# ---------------------------------------------------------------- geometry of the scene

ARENA_X = 8.89          # camera half-width (ortho size 5 at 16:9)
ARENA_Y = 5.0           # camera half-height
HOOK_ORIGIN = (0.0, 3.4)
PIG_START = (0.0, -1.0)
PIG_RADIUS = 0.4        # circle 0.5 x prefab scale 0.8
HOOK_HIT_RADIUS = 0.2

# type -> (value, collider radius in world units, hook retract multiplier)
ITEM_TYPES = {
    "Gold_Small":  (50,  0.20,  1.0),
    "Gold_Medium": (100, 0.35,  0.7),
    "Gold_Large":  (250, 0.55,  0.45),
    "Diamond":     (600, 0.1875, 1.1),
    "Rock":        (0,   0.47,  1.0),
    "Life":        (0,   0.20,  1.0),
}
TREASURE_TYPES = ["Gold_Small", "Gold_Medium", "Gold_Large", "Diamond"]

# Where generated items may be placed (inside the view, below the miner)
PLACE_X = (-8.2, 8.2)
PLACE_Y = (-4.5, 2.4)


# ---------------------------------------------------------------- game parameters

@dataclass
class GameParams:
    """Every gameplay number the tuner may change. Defaults mirror the Unity scripts."""
    pig_speed: float = 5.0
    digest_time: float = 0.0
    digest_per_value: float = 0.0   # extra digest seconds per $ eaten
    hook_extend: float = 12.0
    hook_retract: float = 16.0
    overshoot: float = 0.6
    start_delay: float = 1.5
    telegraph_min: float = 1.0
    telegraph_max: float = 2.0
    cooldown: float = 0.8
    min_treasure_shots: int = 2
    max_treasure_shots: int = 3
    round_time: float = 90.0
    starting_lives: int = 0
    invincible_time: float = 1.5
    hook_kills: bool = True

    @property
    def telegraph_mean(self) -> float:
        return 0.5 * (self.telegraph_min + self.telegraph_max)

    def replace(self, **changes) -> "GameParams":
        return dataclasses.replace(self, **changes)


# name -> (low, high, +1 if a larger value helps the player / -1 if it helps the miner)
TUNABLE_PARAMS = {
    "pig_speed":      (1.0, 9.0, +1),
    "digest_time":    (0.0, 3.0, -1),
    "digest_per_value": (0.0, 0.03, -1),
    "hook_extend":    (6.0, 24.0, -1),
    "hook_retract":   (8.0, 30.0, -1),
    "telegraph_mean": (0.5, 3.5, +1),   # shifts telegraph_min and telegraph_max together
    "cooldown":       (0.2, 3.0, +1),
}


# ---------------------------------------------------------------- geometry helpers

def sub(a: Vec, b: Vec) -> Vec: return (a[0] - b[0], a[1] - b[1])
def add(a: Vec, b: Vec) -> Vec: return (a[0] + b[0], a[1] + b[1])
def mul(a: Vec, s: float) -> Vec: return (a[0] * s, a[1] * s)
def dot(a: Vec, b: Vec) -> float: return a[0] * b[0] + a[1] * b[1]
def length(a: Vec) -> float: return math.hypot(a[0], a[1])
def dist(a: Vec, b: Vec) -> float: return math.hypot(a[0] - b[0], a[1] - b[1])


def normalize(a: Vec) -> Vec:
    n = length(a)
    return (a[0] / n, a[1] / n) if n > 1e-9 else (0.0, 0.0)


def clamp(v: float, lo: float, hi: float) -> float:
    return max(lo, min(hi, v))


def circle_cast(start: Vec, direction: Vec, distance: float, radius: float,
                center: Vec, target_radius: float) -> Optional[float]:
    """Distance along `direction` at which a moving circle first touches a static one, like
    Physics2D.CircleCast. Returns 0 if they already overlap, None if no hit within `distance`."""
    r = radius + target_radius
    to_start = sub(start, center)
    c = dot(to_start, to_start) - r * r
    if c <= 0:
        return 0.0
    b = dot(to_start, direction)
    if b > 0:
        return None  # moving away
    disc = b * b - c
    if disc < 0:
        return None
    t = -b - math.sqrt(disc)
    return t if 0 <= t <= distance else None


def point_segment_distance(p: Vec, a: Vec, b: Vec) -> float:
    ab = sub(b, a)
    denom = dot(ab, ab)
    t = 0.0 if denom < 1e-9 else clamp(dot(sub(p, a), ab) / denom, 0.0, 1.0)
    return dist(p, add(a, mul(ab, t)))


# ---------------------------------------------------------------- map

@dataclass(eq=False)   # items compare (and hash) by identity, so equal-looking items stay distinct
class Item:
    type: str
    x: float
    y: float

    @property
    def pos(self) -> Vec: return (self.x, self.y)
    @property
    def value(self) -> int: return ITEM_TYPES[self.type][0]
    @property
    def radius(self) -> float: return ITEM_TYPES[self.type][1]
    @property
    def retract_mult(self) -> float: return ITEM_TYPES[self.type][2]
    @property
    def is_treasure(self) -> bool: return self.type in TREASURE_TYPES


@dataclass
class GameMap:
    items: List[Item] = field(default_factory=list)
    pig_start: Vec = PIG_START
    hooks: List[Vec] = field(default_factory=lambda: [HOOK_ORIGIN])   # more than one: GPU simulator only

    def copy(self) -> "GameMap":
        return GameMap([Item(i.type, i.x, i.y) for i in self.items], self.pig_start, list(self.hooks))

    def total_value(self) -> int:
        return sum(i.value for i in self.items if i.is_treasure)

    def to_json(self) -> dict:
        return {"pig_start": list(self.pig_start), "hooks": [list(h) for h in self.hooks],
                "items": [asdict(i) for i in self.items]}

    @staticmethod
    def from_json(data: dict) -> "GameMap":
        return GameMap([Item(**i) for i in data["items"]], tuple(data.get("pig_start", PIG_START)),
                       [tuple(h) for h in data.get("hooks", [HOOK_ORIGIN])])


def current_game_map() -> GameMap:
    """The hand-placed layout in Assets/Scenes/Game.unity."""
    return GameMap([Item("Gold_Large", 5, -3), Item("Gold_Small", -4, -2), Item("Gold_Medium", 3, 0)])


def position_ok(p: Vec, item_radius: float, others: List[Item], pig_start: Vec, spacing: float) -> bool:
    if not (PLACE_X[0] <= p[0] <= PLACE_X[1] and PLACE_Y[0] <= p[1] <= PLACE_Y[1]):
        return False
    if dist(p, pig_start) < 1.6 or dist(p, HOOK_ORIGIN) < 1.5:
        return False
    return all(dist(p, o.pos) >= spacing + item_radius + o.radius for o in others)


def scatter(rng: random.Random, types: List[str], spacing: float) -> GameMap:
    """Place items with a minimum spacing (Poisson-disk style rejection sampling)."""
    gm = GameMap()
    for t in types:
        for _ in range(500):
            p = (rng.uniform(*PLACE_X), rng.uniform(*PLACE_Y))
            if position_ok(p, ITEM_TYPES[t][1], gm.items, gm.pig_start, spacing):
                gm.items.append(Item(t, *p))
                break
    return gm


def random_map(rng: random.Random, budget: int = 1500, rocks: int = 3, lives: int = 1,
               spacing: float = 0.6) -> GameMap:
    """Heuristic starting point: spend a value budget on treasure, then scatter everything."""
    # Spend the budget: at most one diamond, a couple of large, fill with medium/small
    types: List[str] = []
    left = budget
    if left >= 900 and rng.random() < 0.6:
        types.append("Diamond"); left -= 600
    for _ in range(rng.randint(1, 2)):
        if left >= 400:
            types.append("Gold_Large"); left -= 250
    while left >= 50:
        t = "Gold_Medium" if left >= 100 and rng.random() < 0.5 else "Gold_Small"
        types.append(t); left -= ITEM_TYPES[t][0]
    types += ["Rock"] * rocks + ["Life"] * lives
    return scatter(rng, types, spacing)


HOOK_PLACE_Y = (PLACE_Y[0], HOOK_ORIGIN[1])   # where extra hooks may stand


def random_hooks(rng: random.Random, n: int, pig_start: Vec = PIG_START, spacing: float = 2.0) -> List[Vec]:
    """n hook origins at random places, apart from each other and from the pig's start."""
    hooks: List[Vec] = []
    for _ in range(n):
        for _ in range(1000):
            p = (rng.uniform(*PLACE_X), rng.uniform(*HOOK_PLACE_Y))
            if dist(p, pig_start) >= spacing and all(dist(p, h) >= spacing for h in hooks):
                hooks.append(p)
                break
    return hooks


def gold_map(rng: random.Random, n_gold: int, spacing: float = 0.6) -> GameMap:
    """Gold only: n treasures of random types, no rocks and no Life pickups."""
    return scatter(rng, [rng.choice(TREASURE_TYPES) for _ in range(n_gold)], spacing)


# ---------------------------------------------------------------- game state seen by agents

@dataclass
class Hook:
    state: str = "idle"          # idle / extending / retracting
    pos: Vec = HOOK_ORIGIN
    direction: Vec = (0.0, -1.0)
    max_length: float = 0.0
    traveled: float = 0.0
    carried: Optional[Item] = None
    touched: set = field(default_factory=set)   # things this shot already reacted to
    shot_end: Vec = HOOK_ORIGIN                 # far end of the locked trajectory


@dataclass
class Observation:
    """Everything an agent may look at. Agents must not modify it."""
    time_left: float
    now: float
    pig_pos: Vec
    pig_vel: Vec
    lives: int
    invincible: bool
    digest_left: float                   # seconds until the pig may eat gold again
    player_money: int
    miner_money: int
    items: List[Item]                    # treasure, rocks and lives still on the map
    hook: Hook
    telegraph: Optional[Tuple[Vec, Vec, bool]]  # (start, end, aimed_at_player) while shown
    params: GameParams


# ---------------------------------------------------------------- agents

class PigAgent:
    def reset(self, rng: random.Random, params: GameParams):
        self.rng, self.params = rng, params

    def act(self, obs: Observation) -> Vec:
        """Return the desired move direction (length <= 1)."""
        raise NotImplementedError


class MinerAgent:
    def reset(self, rng: random.Random, params: GameParams):
        self.rng, self.params = rng, params

    def choose_target(self, obs: Observation) -> Tuple[Vec, bool]:
        """Called when the hook is idle and the cooldown is over.
        Return (aim point, aimed_at_player). The trajectory is locked when this is called."""
        raise NotImplementedError


class ScriptedMiner(MinerAgent):
    """Replica of MinerAI.cs: value-weighted random treasure, every 2-3 shots aim at the pig."""

    def reset(self, rng, params):
        super().reset(rng, params)
        self.treasure_shots_left = self._shots()

    def _shots(self) -> int:
        return self.rng.randint(self.params.min_treasure_shots, self.params.max_treasure_shots)

    def _wants_pig_shot(self, treasures) -> bool:
        """True when this shot goes at the pig (and resets the treasure-shot counter)."""
        if not self.params.hook_kills:
            return False   # the hook can't hurt the pig, so aiming at it is pointless
        if self.treasure_shots_left > 0 and treasures:
            self.treasure_shots_left -= 1
            return False
        self.treasure_shots_left = self._shots()
        return True

    def choose_target(self, obs):
        treasures = [i for i in obs.items if i.is_treasure]
        if self._wants_pig_shot(treasures) or not treasures:
            return obs.pig_pos, True
        pick = self.rng.choices(treasures, weights=[max(1, t.value) for t in treasures])[0]
        return pick.pos, False


class SmartMiner(ScriptedMiner):
    """Stronger stand-in: best value per second, steals what the pig is heading for,
    and leads the pig partially when shooting at it."""

    lead = 0.5   # fraction of full prediction used when aiming at the pig

    def choose_target(self, obs):
        p = obs.params
        treasures = [i for i in obs.items if i.is_treasure]
        if self._wants_pig_shot(treasures) or not treasures:
            flight = p.telegraph_mean + dist(HOOK_ORIGIN, obs.pig_pos) / p.hook_extend
            return add(obs.pig_pos, mul(obs.pig_vel, flight * self.lead)), True
        return max(treasures, key=lambda t: self._utility(t, obs)).pos, False

    @staticmethod
    def _utility(t: Item, obs: Observation) -> float:
        p = obs.params
        d = dist(HOOK_ORIGIN, t.pos)
        hook_time = p.telegraph_mean + d / p.hook_extend + d / (p.hook_retract * t.retract_mult)
        pig_time = max(dist(obs.pig_pos, t.pos) / p.pig_speed, obs.digest_left)
        steal_bonus = 1.5 if pig_time < hook_time + 1.0 else 1.0   # contested: grab it before the pig
        lost = pig_time < hook_time - 1.5                           # pig will clearly win it
        return 0.0 if lost else steal_bonus * t.value / (hook_time + p.cooldown)


# -- pig helpers shared by the policies

def danger_lines(obs: Observation) -> List[Tuple[Vec, Vec]]:
    lines = []
    if obs.telegraph:
        lines.append((obs.telegraph[0], obs.telegraph[1]))
    if obs.hook.state != "idle" and obs.hook.carried is None:
        lines.append((HOOK_ORIGIN, obs.hook.shot_end))
    return lines


def dodge_move(obs: Observation, margin: float = 0.5) -> Optional[Vec]:
    """Step sideways out of a locked trajectory, or None if the pig is safe (or can't be hurt)."""
    if not obs.params.hook_kills:
        return None
    for a, b in danger_lines(obs):
        if point_segment_distance(obs.pig_pos, a, b) < PIG_RADIUS + HOOK_HIT_RADIUS + margin:
            line = normalize(sub(b, a))
            side = (-line[1], line[0])
            if dot(sub(obs.pig_pos, a), side) < 0:
                side = mul(side, -1)
            return side
    return None


def seek(obs: Observation, item: Item) -> Vec:
    to_item = sub(item.pos, obs.pig_pos)
    return (0.0, 0.0) if length(to_item) < 0.05 else normalize(to_item)


def eat_time(start: Vec, t0: float, ready_at: float, item: Item, speed: float) -> Tuple[float, Vec]:
    """Leaving `start` at time t0, when can the pig eat `item` (touch it and be done digesting),
    and where does it stand then."""
    d = dist(start, item.pos)
    walk = max(0.0, d - (PIG_RADIUS + item.radius))
    stop = add(start, mul(normalize(sub(item.pos, start)), walk))
    return max(t0 + walk / speed, ready_at), stop


def hook_cycle(p: GameParams, d: float, mult: float) -> float:
    """Aim, fly out, pull back and cool down, for a treasure at distance d."""
    return p.telegraph_mean + d / p.hook_extend + d / (p.hook_retract * mult) + p.cooldown


@dataclass
class HookForecast:
    free_at: float                       # seconds until the miner can start its next aim
    committed: Optional[Item] = None     # treasure the shown telegraph / flying hook will grab
    committed_at: float = math.inf       # seconds until it grabs it


def forecast_hook(obs: Observation, telegraph_left: float, idle_wait: float = 0.0) -> HookForecast:
    """What the miner is busy with right now, from what the player can see.
    `idle_wait`: remaining start delay or cooldown while the hook is idle."""
    p, h = obs.params, obs.hook

    def along(start: Vec, end: Vec, wait: float, out_so_far: float) -> HookForecast:
        """Hook about to fly (after `wait`) from start to end, already `out_so_far` from the origin."""
        direction, max_len = normalize(sub(end, start)), dist(start, end)
        hit, hit_d = None, math.inf
        for item in obs.items:
            if item.type == "Life":
                continue   # the hook passes through Life pickups
            d = circle_cast(start, direction, max_len, HOOK_HIT_RADIUS, item.pos, item.radius)
            if d is not None and d < hit_d:
                hit, hit_d = item, d
        if hit is None:
            out = out_so_far + max_len
            return HookForecast(wait + max_len / p.hook_extend + out / p.hook_retract + p.cooldown)
        grab = wait + hit_d / p.hook_extend
        out = out_so_far + hit_d
        mult = hit.retract_mult if hit.is_treasure else 1.0
        free = grab + out / (p.hook_retract * mult) + p.cooldown
        return HookForecast(free, hit, grab) if hit.is_treasure else HookForecast(free)

    if obs.telegraph:
        return along(obs.telegraph[0], obs.telegraph[1], telegraph_left, 0.0)
    if h.state == "extending":
        return along(h.pos, h.shot_end, 0.0, h.traveled)
    if h.state == "retracting":
        mult = h.carried.retract_mult if h.carried else 1.0
        return HookForecast(dist(h.pos, HOOK_ORIGIN) / (p.hook_retract * mult) + p.cooldown)
    return HookForecast(idle_wait)


def single_race_eta(obs: Observation, fc: HookForecast, item: Item) -> float:
    """When the hook would grab `item` if the miner went for it next (ignores its other targets)."""
    if item is fc.committed:
        return fc.committed_at
    return fc.free_at + obs.params.telegraph_mean + dist(HOOK_ORIGIN, item.pos) / obs.params.hook_extend


def schedule_threats(obs: Observation, fc: HookForecast) -> Dict[Item, float]:
    """Forecast of when the miner takes each treasure if the pig took nothing: it works through
    them one shot at a time, best value per second first. Missing = not taken before time runs out."""
    p = obs.params
    threats: Dict[Item, float] = {}
    if fc.committed:
        threats[fc.committed] = fc.committed_at
    t = fc.free_at
    left = [i for i in obs.items if i.is_treasure and i is not fc.committed]
    mean_shots = 0.5 * (p.min_treasure_shots + p.max_treasure_shots)
    pig_shot_debt = 0.0
    while left:
        nxt = max(left, key=lambda i: i.value / hook_cycle(p, dist(HOOK_ORIGIN, i.pos), i.retract_mult))
        d = dist(HOOK_ORIGIN, nxt.pos)
        grab = t + p.telegraph_mean + d / p.hook_extend
        if grab > obs.time_left:
            break
        threats[nxt] = grab
        left.remove(nxt)
        t += hook_cycle(p, d, nxt.retract_mult)
        if p.hook_kills:   # every few shots the miner spends one on the pig
            pig_shot_debt += 1.0 / mean_shots
            if pig_shot_debt >= 1.0:
                pig_shot_debt -= 1.0
                t += hook_cycle(p, dist(HOOK_ORIGIN, obs.pig_pos) + p.overshoot, 1.0)
    return threats


def survival_model(obs: Observation, fc: HookForecast, miner_model: str):
    """Returns survival(item, t): probability that the miner has not taken `item` t seconds from now
    (assuming the pig leaves it alone).
      'greedy'   the miner takes gold in order of value per second (like SmartMiner, deterministic)
      'weighted' each shot picks gold at random, weighted by value (like ScriptedMiner)"""
    p = obs.params
    if miner_model == "greedy":
        threats = schedule_threats(obs, fc)
        return lambda item, t: 1.0 if t < threats.get(item, math.inf) else 0.0

    left = [i for i in obs.items if i.is_treasure and i is not fc.committed]
    total = sum(i.value for i in left) or 1
    avg_cycle = sum(i.value / total * hook_cycle(p, dist(HOOK_ORIGIN, i.pos), i.retract_mult) for i in left)
    if p.hook_kills:   # share of shots spent on the pig
        mean_shots = 0.5 * (p.min_treasure_shots + p.max_treasure_shots)
        avg_cycle += hook_cycle(p, dist(HOOK_ORIGIN, obs.pig_pos) + p.overshoot, 1.0) / mean_shots
        aim_share = mean_shots / (mean_shots + 1)
    else:
        aim_share = 1.0

    def survival(item: Item, t: float) -> float:
        if item is fc.committed:
            return 1.0 if t < fc.committed_at else 0.0
        first_grab = fc.free_at + p.telegraph_mean + dist(HOOK_ORIGIN, item.pos) / p.hook_extend
        if t < first_grab or avg_cycle <= 0:
            return 1.0
        shots = int((t - first_grab) / avg_cycle) + 1
        return (1.0 - aim_share * item.value / total) ** shots

    return survival


class GreedyPig(PigAgent):
    """Original baseline: chase the best value-per-distance item, dodge telegraphs and the hook.
    Ignores what the miner is about to take."""

    danger_margin = 0.5

    def act(self, obs):
        move = dodge_move(obs, self.danger_margin)
        if move:
            return move
        best, best_score = None, 0.0
        for item in obs.items:
            if item.type == "Rock":
                continue
            worth = item.value if item.is_treasure else (150 if obs.lives == 0 else 40)
            if any(point_segment_distance(item.pos, a, b) < item.radius + HOOK_HIT_RADIUS + 0.3
                   for a, b in danger_lines(obs)):
                continue
            score = worth / (dist(obs.pig_pos, item.pos) + 0.5)
            if score > best_score:
                best, best_score = item, score
        return normalize(sub(best.pos, obs.pig_pos)) if best else (0.0, 0.0)


class PlannerPig(PigAgent):
    """Base for the forecast-driven pigs: tracks the telegraph timer, dodges, replans a target
    every `replan_every` seconds (or when the target disappears) and walks to it."""

    replan_every = 0.1
    danger_margin = 0.5

    def reset(self, rng, params):
        super().reset(rng, params)
        self.target: Optional[Item] = None
        self.next_plan = -1.0
        self.telegraph_seen_at: Optional[float] = None
        self.idle_since: Optional[float] = None

    def telegraph_left(self, obs: Observation) -> float:
        """Expected time until the shown telegraph fires. The player sees when it appeared,
        and that it lasts between telegraph_min and telegraph_max."""
        if obs.telegraph is None:
            self.telegraph_seen_at = None
            return 0.0
        if self.telegraph_seen_at is None:
            self.telegraph_seen_at = obs.now
        p = obs.params
        elapsed = obs.now - self.telegraph_seen_at
        if elapsed < p.telegraph_min:
            return p.telegraph_mean - elapsed
        return max(0.0, 0.5 * (p.telegraph_max - elapsed))

    def idle_wait(self, obs: Observation) -> float:
        """Remaining start delay or cooldown, from when the player saw the hook come back."""
        if obs.hook.state != "idle" or obs.telegraph:
            self.idle_since = None
            return 0.0
        if obs.now < obs.params.start_delay:
            return obs.params.start_delay - obs.now
        if self.idle_since is None:
            self.idle_since = obs.now
        return max(0.0, obs.params.cooldown - (obs.now - self.idle_since))

    def act(self, obs):
        fc = forecast_hook(obs, self.telegraph_left(obs), self.idle_wait(obs))
        move = dodge_move(obs, self.danger_margin)
        if move:
            return move
        if self.target not in obs.items or obs.now >= self.next_plan:
            self.target = self.choose(obs, fc)
            self.next_plan = obs.now + self.replan_every
        return seek(obs, self.target) if self.target else (0.0, 0.0)

    def choose(self, obs: Observation, fc: HookForecast) -> Optional[Item]:
        raise NotImplementedError

    @staticmethod
    def nearest(obs: Observation) -> Optional[Item]:
        treasures = [i for i in obs.items if i.is_treasure]
        return min(treasures, key=lambda i: dist(obs.pig_pos, i.pos)) if treasures else None


class RaceGreedyPig(PlannerPig):
    """Gold the miner can reach before us is worth 0; take the most valuable of what is left."""

    def choose(self, obs, fc):
        best, best_key = None, None
        for item in obs.items:
            if not item.is_treasure:
                continue
            eat, _ = eat_time(obs.pig_pos, 0.0, obs.digest_left, item, obs.params.pig_speed)
            if eat >= single_race_eta(obs, fc, item):
                continue
            key = (item.value, -eat)
            if best_key is None or key > best_key:
                best, best_key = item, key
        return best or self.nearest(obs)


class MarginGreedyPig(PlannerPig):
    """Plays for the money margin. Uses a model of the miner to estimate, for each gold, the
    chance it is still there when the pig arrives. Gold the miner would otherwise take counts
    twice (+v for us, -v for it), safe gold counts once. Searches `depth` steps of collection
    order on expected gain, discounted by `gamma` per second, which makes it take contested gold
    first and leave safe gold for later.
    `miner_model` is what the player has learned about the AI: 'weighted' or 'greedy'."""

    min_survival = 0.05   # don't plan around gold that is almost surely gone

    def __init__(self, depth: int = 2, gamma: float = 0.85, miner_model: str = "weighted"):
        self.depth, self.gamma, self.miner_model = depth, gamma, miner_model

    def choose(self, obs, fc):
        p = obs.params
        survival = survival_model(obs, fc, self.miner_model)
        treasures = [i for i in obs.items if i.is_treasure]
        contested = {i: 1.0 - survival(i, obs.time_left) for i in treasures}  # P(miner takes it in time)
        best_score, best_first = 0.0, None

        def search(pos: Vec, t: float, ready: float, left: List[Item], depth: int, score: float,
                   first: Optional[Item]):
            nonlocal best_score, best_first
            extended = False
            if depth < self.depth:
                for item in left:
                    eat, stop = eat_time(pos, t, ready, item, p.pig_speed)
                    alive = survival(item, eat)
                    if alive < self.min_survival or eat > obs.time_left:
                        continue   # the miner almost surely gets it first, or the round is over
                    gain = alive * item.value * (1.0 + contested[item])
                    extended = True
                    search(stop, eat, eat + p.digest_time + p.digest_per_value * item.value, [j for j in left if j is not item],
                           depth + 1, score + gain * self.gamma ** eat, first or item)
            if not extended and first is not None and score > best_score:
                best_score, best_first = score, first

        search(obs.pig_pos, 0.0, obs.digest_left, treasures, 0, 0.0, None)
        return best_first or self.nearest(obs)


class FollowPig(PigAgent):
    """Macro-action pig driven by the oracle search: walk to `target` and eat it."""

    target: Optional[Item] = None

    def act(self, obs):
        move = dodge_move(obs)
        if move:
            return move
        return seek(obs, self.target) if self.target in obs.items else (0.0, 0.0)


class OrderPig(PlannerPig):
    """Walks to gold in a fixed order, skipping gold that is already gone (open-loop plan)."""

    def __init__(self, order: List[int]):
        self.order = order
        self.slots: Optional[List[Item]] = None

    def choose(self, obs, fc):
        if self.slots is None:   # first call: the map is still complete, in map order
            self.slots = [obs.items[k] for k in self.order]
        return next((i for i in self.slots if i in obs.items), None)


# ---------------------------------------------------------------- simulation

@dataclass
class MatchResult:
    player_won: bool
    killed: bool
    player_money: int
    miner_money: int
    duration: float
    end_reason: str

    @property
    def margin(self) -> int:
        return self.player_money - self.miner_money


class Game:
    def __init__(self, game_map: GameMap, pig: PigAgent, miner: MinerAgent, seed: int,
                 params: Optional[GameParams] = None, dt: float = 0.02):
        self.p = params or GameParams()
        if list(game_map.hooks) != [HOOK_ORIGIN]:
            raise NotImplementedError("the CPU simulator has one hook at HOOK_ORIGIN; use gold_miner_torch.py")
        self.rng = random.Random(seed)
        self.items = [Item(i.type, i.x, i.y) for i in game_map.items]
        self.pig, self.miner = pig, miner
        pig.reset(random.Random(self.rng.random()), self.p)
        miner.reset(random.Random(self.rng.random()), self.p)
        self.dt = dt

        self.time_left = self.p.round_time
        self.pig_pos = game_map.pig_start
        self.pig_vel = (0.0, 0.0)
        self.lives = self.p.starting_lives
        self.invincible_until = -1.0
        self.digest_until = -1.0
        self.player_money = 0
        self.miner_money = 0
        self.hook = Hook()
        self.telegraph = None                 # (start, end, aimed_at_player)
        self.telegraph_left = 0.0
        self.miner_wait = self.p.start_delay  # start delay, then cooldowns
        self.had_treasure = any(i.is_treasure for i in self.items)
        self.playing = True
        self.killed = False
        self.end_reason = ""

    @property
    def now(self) -> float:
        return self.p.round_time - self.time_left

    def observe(self) -> Observation:
        return Observation(self.time_left, self.now, self.pig_pos, self.pig_vel, self.lives,
                           self.now < self.invincible_until, max(0.0, self.digest_until - self.now),
                           self.player_money, self.miner_money, self.items, self.hook, self.telegraph,
                           self.p)

    def result(self) -> MatchResult:
        won = not self.killed and self.player_money > self.miner_money
        return MatchResult(won, self.killed, self.player_money, self.miner_money, self.now, self.end_reason)

    def run(self) -> MatchResult:
        while self.playing:
            self.step()
        return self.result()

    # -- one fixed step: pig, miner brain, hook, round rules
    def step(self):
        dt = self.dt
        self.move_pig(dt)
        self.update_miner(dt)
        self.update_hook(dt)

        self.time_left -= dt
        if self.time_left <= 0:
            self.end("Time's up!")
        elif self.had_treasure and not any(i.is_treasure for i in self.items):
            self.end("All treasure collected!")

    def end(self, reason: str, killed: bool = False):
        if self.playing:
            self.playing, self.killed, self.end_reason = False, killed, reason

    def move_pig(self, dt):
        move = self.pig.act(self.observe())
        if length(move) > 1:
            move = normalize(move)
        self.pig_vel = mul(move, self.p.pig_speed)
        p = add(self.pig_pos, mul(self.pig_vel, dt))

        # Rocks are solid: push the pig back out
        for item in self.items:
            if item.type == "Rock":
                d = dist(p, item.pos)
                min_d = PIG_RADIUS + item.radius
                if d < min_d:
                    p = add(item.pos, mul(normalize(sub(p, item.pos)) if d > 1e-6 else (0, 1), min_d))
        p = (clamp(p[0], -ARENA_X + PIG_RADIUS, ARENA_X - PIG_RADIUS),
             clamp(p[1], -ARENA_Y + PIG_RADIUS, ARENA_Y - PIG_RADIUS))
        self.pig_pos = p

        # Touching treasure / Life pickups collects them; gold only when not digesting
        for item in list(self.items):
            if item.type == "Rock" or dist(p, item.pos) >= PIG_RADIUS + item.radius:
                continue
            if item.is_treasure:
                if self.now < self.digest_until:
                    continue
                self.player_money += item.value
                self.digest_until = self.now + self.p.digest_time + self.p.digest_per_value * item.value
            else:
                self.lives += 1
            self.items.remove(item)

    def update_miner(self, dt):
        if self.telegraph:
            self.telegraph_left -= dt
            if self.telegraph_left <= 0:
                start, end, _ = self.telegraph
                self.telegraph = None
                self.fire(start, end)
            return
        if self.hook.state != "idle":
            return
        self.miner_wait -= dt
        if self.miner_wait > 0:
            return
        aim, at_player = self.miner.choose_target(self.observe())
        direction = normalize(sub(aim, HOOK_ORIGIN))
        if direction == (0.0, 0.0):
            direction = (0.0, -1.0)
        end = add(HOOK_ORIGIN, mul(direction, dist(aim, HOOK_ORIGIN) + self.p.overshoot))
        self.telegraph = (HOOK_ORIGIN, end, at_player)
        self.telegraph_left = self.rng.uniform(self.p.telegraph_min, self.p.telegraph_max)

    def fire(self, start: Vec, end: Vec):
        h = self.hook
        h.state, h.pos = "extending", start
        h.direction = normalize(sub(end, start))
        h.max_length = dist(start, end)
        h.traveled, h.carried = 0.0, None
        h.touched = set()
        h.shot_end = end

    def update_hook(self, dt):
        h = self.hook
        if h.state == "extending":
            step = min(self.p.hook_extend * dt, h.max_length - h.traveled)
            for hit_dist, thing in self.cast(h.pos, h.direction, step):
                if self.handle_hit(thing):
                    h.pos = add(h.pos, mul(h.direction, hit_dist))
                    h.state = "retracting"
                    return
            h.pos = add(h.pos, mul(h.direction, step))
            h.traveled += step
            if h.traveled >= h.max_length - 0.001:
                h.state = "retracting"
        elif h.state == "retracting":
            remaining = dist(h.pos, HOOK_ORIGIN)
            mult = h.carried.retract_mult if h.carried else 1.0
            step = min(self.p.hook_retract * mult * dt, remaining)
            if self.playing and h.carried is None and remaining > 1e-4:
                back = normalize(sub(HOOK_ORIGIN, h.pos))
                for _, thing in self.cast(h.pos, back, step):
                    self.handle_hit(thing)
                    if h.carried:
                        break
            h.pos = add(h.pos, mul(normalize(sub(HOOK_ORIGIN, h.pos)), step))
            if dist(h.pos, HOOK_ORIGIN) < 1e-3:
                h.pos, h.state, h.carried = HOOK_ORIGIN, "idle", None
                self.miner_wait = self.p.cooldown

    def cast(self, start: Vec, direction: Vec, distance: float):
        """Everything the hook would touch this step, nearest first (CircleCastAll)."""
        hits = []
        for item in self.items:
            d = circle_cast(start, direction, distance, HOOK_HIT_RADIUS, item.pos, item.radius)
            if d is not None:
                hits.append((d, item))
        if self.p.hook_kills:
            d = circle_cast(start, direction, distance, HOOK_HIT_RADIUS, self.pig_pos, PIG_RADIUS)
            if d is not None:
                hits.append((d, "pig"))
        hits.sort(key=lambda x: x[0])
        return hits

    def handle_hit(self, thing) -> bool:
        """Mirror of Hook.HandleHit: True if the hook should stop here."""
        h = self.hook
        if thing in h.touched:
            return False
        h.touched.add(thing)
        if thing == "pig":
            self.player_hit()
            return True
        if thing.is_treasure:
            self.items.remove(thing)
            h.carried = thing
            if self.playing:
                self.miner_money += thing.value
            return True
        if thing.type == "Rock":
            self.items.remove(thing)   # shatters
            return True
        return False                   # Life pickups: the hook passes through

    def player_hit(self):
        if not self.playing or self.now < self.invincible_until:
            return
        if self.lives > 0:
            self.lives -= 1
            self.invincible_until = self.now + self.p.invincible_time
        else:
            self.end("Killed by the hook!", killed=True)


# ---------------------------------------------------------------- oracle

@dataclass
class OracleResult:
    margin: int
    nodes: int
    complete: bool    # False if the node budget ran out (then margin is only a lower bound)


def oracle(game_map: GameMap, params: GameParams, miner_name: str, seed: int,
           node_budget: int = 20000, dt: float = 0.02) -> OracleResult:
    """Best money margin over every order of walking to gold, for this match's random numbers.
    Branches by cloning the game each time the pig's target is eaten or taken by the miner.
    It knows the future (telegraph durations, the miner's random picks), so it is an upper bound
    for any policy that walks straight from gold to gold."""
    root = Game(game_map, FollowPig(), make_miner(miner_name), seed, params, dt)
    best = -math.inf
    nodes = 0
    complete = True

    def search(g: Game):
        nonlocal best, nodes, complete
        treasures = [i for i in g.items if i.is_treasure]
        if not g.playing or not treasures:
            while g.playing:
                g.step()
            best = max(best, g.result().margin)
            return
        if g.player_money + sum(i.value for i in treasures) - g.miner_money <= best:
            return   # can't beat the best found even if the pig got everything left
        # Promising branches first, so the bound prunes more
        treasures.sort(key=lambda i: -i.value / (dist(g.pig_pos, i.pos) + 0.5))
        for item in treasures:
            if nodes >= node_budget:
                complete = False
                return
            nodes += 1
            child = copy.deepcopy(g)
            target = child.items[g.items.index(item)]
            child.pig.target = target
            while child.playing and target in child.items:
                child.step()
            search(child)

    search(root)
    return OracleResult(int(best), nodes, complete)


# ---------------------------------------------------------------- experiments

@dataclass
class Evaluation:
    win_rate: float
    kill_rate: float
    mean_margin: float      # player money - miner money
    episodes: int

    @property
    def imbalance(self) -> float:
        return abs(self.win_rate - 0.5)

    def __str__(self):
        se = math.sqrt(max(self.win_rate * (1 - self.win_rate), 1e-9) / self.episodes)
        return (f"player win {self.win_rate:5.1%} (+/-{se:.1%})  killed {self.kill_rate:5.1%}  "
                f"avg margin {self.mean_margin:+7.1f}  [{self.episodes} games]")


PIG_POLICIES = ["greedy", "race", "margin"]


MINER_MODELS = {"scripted": "weighted", "smart": "greedy", "efficient": "greedy"}   # what a player learns about each AI


def make_pig(name: str, depth: int = 2, gamma: float = 0.85, miner_name: str = "scripted") -> PigAgent:
    if name == "race":
        return RaceGreedyPig()
    if name == "margin":
        return MarginGreedyPig(depth, gamma, MINER_MODELS[miner_name])
    return GreedyPig()


def make_miner(name: str) -> MinerAgent:
    if name == "efficient":
        raise NotImplementedError("the efficient miner is in gold_miner_torch.py only")
    return SmartMiner() if name == "smart" else ScriptedMiner()


def play(game_map: GameMap, params: GameParams, pig_name: str, miner_name: str, seed: int,
         depth: int = 2, gamma: float = 0.85, dt: float = 0.02) -> MatchResult:
    return Game(game_map, make_pig(pig_name, depth, gamma, miner_name), make_miner(miner_name), seed, params, dt).run()


def evaluate(game_map: GameMap, params: GameParams, episodes: int, pig_name: str = "greedy",
             miner_name: str = "scripted", base_seed: int = 0, depth: int = 2, gamma: float = 0.85,
             dt: float = 0.02) -> Evaluation:
    """Play `episodes` matches. The same base_seed gives the same match seeds (common random numbers),
    so two maps evaluated with the same seed are compared fairly."""
    wins = kills = 0
    margin = 0.0
    for e in range(episodes):
        r = play(game_map, params, pig_name, miner_name, base_seed * 100003 + e, depth, gamma, dt)
        wins += r.player_won
        kills += r.killed
        margin += r.margin
    return Evaluation(wins / episodes, kills / episodes, margin / episodes, episodes)


def race_margin(p: Vec, pig_start: Vec, params: GameParams) -> float:
    """Seconds the pig is ahead of the hook for an item at p (positive = pig gets there first).
    Smooth, so its gradient tells us which way to move an item to help one side."""
    hook_time = params.telegraph_mean + dist(HOOK_ORIGIN, p) / params.hook_extend
    pig_time = dist(pig_start, p) / params.pig_speed
    return hook_time - pig_time


def tweak_param(params: GameParams, help_player: bool, rng: random.Random, names: List[str],
                frac: float = 0.1) -> GameParams:
    """Nudge one tunable parameter towards the losing side."""
    name = rng.choice(names)
    lo, hi, sign = TUNABLE_PARAMS[name]
    delta = sign * (1 if help_player else -1) * frac * (hi - lo) * rng.uniform(0.5, 1.5)
    if name == "telegraph_mean":
        half = 0.5 * (params.telegraph_max - params.telegraph_min)
        mean = clamp(params.telegraph_mean + delta, lo, hi)
        return params.replace(telegraph_min=max(0.0, mean - half), telegraph_max=mean + half)
    return params.replace(**{name: clamp(getattr(params, name) + delta, lo, hi)})


def propose(game_map: GameMap, params: GameParams, help_player: bool, rng: random.Random,
            tune_params: List[str], allow_rocks: bool, step: float = 0.7) -> Tuple[GameMap, GameParams]:
    """One change that should shift the balance towards the losing side."""
    if tune_params and rng.random() < 0.3:
        return game_map, tweak_param(params, help_player, rng, tune_params)

    new = game_map.copy()
    treasures = [i for i in new.items if i.is_treasure]
    sign = 1.0 if help_player else -1.0
    move = rng.random()
    if not allow_rocks:
        move *= 0.8   # never reach the rock branch

    if move < 0.6 and treasures:
        # Move a treasure along the gradient of the race margin (higher value = moved more often)
        t = rng.choices(treasures, weights=[x.value for x in treasures])[0]
        eps = 0.05
        gx = (race_margin((t.x + eps, t.y), new.pig_start, params) - race_margin((t.x - eps, t.y), new.pig_start, params)) / (2 * eps)
        gy = (race_margin((t.x, t.y + eps), new.pig_start, params) - race_margin((t.x, t.y - eps), new.pig_start, params)) / (2 * eps)
        g = normalize((gx, gy))
        jitter = (rng.gauss(0, 0.3), rng.gauss(0, 0.3))
        target = add((t.x, t.y), add(mul(g, sign * step), jitter))
        others = [o for o in new.items if o is not t]
        if position_ok(target, t.radius, others, new.pig_start, 0.4):
            t.x, t.y = target
    elif move < 0.8 and len(treasures) >= 2:
        # Swap the types of two treasures: put the valuable one where the losing side has the edge
        a, b = rng.sample(treasures, 2)
        ma, mb = race_margin(a.pos, new.pig_start, params), race_margin(b.pos, new.pig_start, params)
        favoured, other = (a, b) if (ma > mb) == help_player else (b, a)
        if favoured.value < other.value:
            favoured.type, other.type = other.type, favoured.type
    else:
        # Rocks: cover between the hook and a treasure helps the player; removing cover helps the miner
        rocks = [i for i in new.items if i.type == "Rock"]
        if help_player and treasures:
            t = rng.choice(treasures)
            along = rng.uniform(0.35, 0.7)
            p = add(HOOK_ORIGIN, mul(sub(t.pos, HOOK_ORIGIN), along))
            if position_ok(p, ITEM_TYPES["Rock"][1], new.items, new.pig_start, 0.3):
                if rocks:
                    rock = rng.choice(rocks)
                    rock.x, rock.y = p                    # move an existing rock into cover
                else:
                    new.items.append(Item("Rock", *p))
        elif not help_player and rocks:
            new.items.remove(rng.choice(rocks))
    return new, params


def tune(start: GameMap, params: GameParams, episodes: int, iters: int, pig_name: str, miner_name: str,
         seed: int, tune_params: List[str], allow_rocks: bool, tolerance: float = 0.05,
         depth: int = 2, gamma: float = 0.85, log=print) -> Tuple[GameMap, GameParams, Evaluation]:
    """Experiment-driven search: play the map, then keep changes that move the win rate towards 50%."""
    rng = random.Random(seed)
    best, best_params = start, params
    best_eval = evaluate(best, best_params, episodes, pig_name, miner_name, seed, depth, gamma)
    log(f"start   {best_eval}")
    for it in range(1, iters + 1):
        if best_eval.imbalance <= tolerance:
            log(f"balanced within +/-{tolerance:.0%}, stopping")
            break
        help_player = best_eval.win_rate < 0.5
        cand, cand_params = propose(best, best_params, help_player, rng, tune_params, allow_rocks)
        cand_eval = evaluate(cand, cand_params, episodes, pig_name, miner_name, seed, depth, gamma)
        better = (cand_eval.imbalance < best_eval.imbalance or
                  (cand_eval.imbalance == best_eval.imbalance and abs(cand_eval.mean_margin) < abs(best_eval.mean_margin)))
        if better:
            best, best_params, best_eval = cand, cand_params, cand_eval
        log(f"iter {it:3d} {'help player' if help_player else 'help miner '} "
            f"{'KEEP' if better else 'skip'}  {cand_eval}")
    log(f"final   {best_eval}")
    return best, best_params, best_eval


def best_fixed_order(game_map: GameMap, params: GameParams, miner_name: str, seeds: List[int]) -> float:
    """Mean margin of the best single collection order for this map, averaged over `seeds`.
    Unlike the oracle it can't adapt to how a match unfolds, so it is a baseline that does
    not know the future (slightly optimistic: the order is picked on the same seeds)."""
    gold = [k for k, i in enumerate(game_map.items) if i.is_treasure]
    best = -math.inf
    for order in itertools.permutations(gold):
        total = sum(Game(game_map, OrderPig(list(order)), make_miner(miner_name), seed, params).run().margin
                    for seed in seeds)
        best = max(best, total / len(seeds))
    return best


def compare(args, params: GameParams):
    """Play every pig policy on the same gold-only maps and match seeds, and measure each one's
    gap to the oracle (best collection order with hindsight)."""
    pigs = [p.strip() for p in args.pigs.split(",")]
    use_oracle = args.n_gold <= args.oracle_max_gold
    rows = []
    for m in range(args.maps):
        gm = gold_map(random.Random(args.map_seed * 7919 + m), args.n_gold)
        seeds = [args.seed * 100003 + s for s in range(args.seeds)]
        fixed = best_fixed_order(gm, params, args.miner, seeds) if args.best_order else None
        for s in range(args.seeds):
            seed = args.seed * 100003 + s
            row = {"map": m, "seed": seed, "gold": gm.total_value()}
            if fixed is not None:
                row["best_order"] = fixed   # same value for every seed of the map
            if use_oracle:
                o = oracle(gm, params, args.miner, seed, args.node_budget)
                row.update(oracle=o.margin, oracle_complete=o.complete, oracle_nodes=o.nodes)
            for pig in pigs:
                r = play(gm, params, pig, args.miner, seed, args.lookahead, args.gamma)
                row[pig] = r.margin
                row[pig + "_won"] = r.player_won
            rows.append(row)
        print(f"map {m + 1}/{args.maps} done", flush=True)

    print(f"\n{args.maps} maps x {args.seeds} seeds, {args.n_gold} gold, miner={args.miner}, {params}")
    header = f"{'policy':8s} {'margin':>8s} {'win':>6s}"
    if use_oracle:
        header += f" {'gap':>8s} {'gap/gold':>9s} {'=oracle':>8s}"
    print(header)
    for pig in pigs:
        margins = [r[pig] for r in rows]
        line = f"{pig:8s} {sum(margins) / len(rows):+8.1f} {sum(r[pig + '_won'] for r in rows) / len(rows):6.1%}"
        if use_oracle:
            gaps = [r["oracle"] - r[pig] for r in rows]
            line += (f" {sum(gaps) / len(rows):8.1f} {sum(g / r['gold'] for g, r in zip(gaps, rows)) / len(rows):9.1%}"
                     f" {sum(g <= 0 for g in gaps) / len(rows):8.1%}")
        print(line)
    if use_oracle:
        o = [r["oracle"] for r in rows]
        print(f"{'oracle':8s} {sum(o) / len(rows):+8.1f} {sum(x > 0 for x in o) / len(rows):6.1%}"
              f"   (knows the future; search finished in {sum(r['oracle_complete'] for r in rows)}/{len(rows)} games)")
    if args.best_order:
        print(f"{'best order':10s} {sum(r['best_order'] for r in rows) / len(rows):+6.1f}"
              f"   (best fixed order per map, averaged over its seeds)")
    if args.csv:
        with open(args.csv, "w", newline="", encoding="utf-8") as f:
            w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
            w.writeheader()
            w.writerows(rows)
        print(f"wrote {args.csv}")


# ---------------------------------------------------------------- command line

PARAM_FLAGS = {   # command-line flag -> GameParams field
    "pig_speed": "pig_speed", "digest": "digest_time", "digest_per_value": "digest_per_value", "hook_extend": "hook_extend",
    "hook_retract": "hook_retract", "telegraph_min": "telegraph_min", "telegraph_max": "telegraph_max",
    "cooldown": "cooldown", "start_delay": "start_delay", "round_time": "round_time",
}


def load_map(args) -> Tuple[GameMap, Optional[GameParams]]:
    """The map, plus the parameters stored with it (map files written by tune).
    --hooks n > 1 replaces the hooks with n at random places."""
    stored = None
    if args.map == "current":
        game_map = current_game_map()
    elif args.map == "random":
        game_map = random_map(random.Random(args.map_seed), args.budget, args.rocks, args.lives)
    elif args.map == "gold":
        game_map = gold_map(random.Random(args.map_seed), args.n_gold)
    else:
        with open(args.map, encoding="utf-8") as f:
            data = json.load(f)
        game_map = GameMap.from_json(data)
        stored = GameParams(**data["params"]) if "params" in data else None
    if args.hooks > 1:
        game_map.hooks = random_hooks(random.Random(args.map_seed * 31 + 7), args.hooks, game_map.pig_start)
    return game_map, stored


def resolve_params(args, stored: Optional[GameParams]) -> GameParams:
    params = stored or GameParams()
    changes = {fld: getattr(args, flag) for flag, fld in PARAM_FLAGS.items() if getattr(args, flag) is not None}
    if args.no_kill:
        changes["hook_kills"] = False
    return params.replace(**changes)


def add_arguments(ap: argparse.ArgumentParser):
    """Options shared with the GPU version (gold_miner_torch.py)."""
    ap.add_argument("--map", default="random", help="'current', 'random', 'gold', or a map .json file")
    ap.add_argument("--map-seed", type=int, default=1, help="seed for generated maps")
    ap.add_argument("--n-gold", type=int, default=6, help="treasure count for --map gold and compare")
    ap.add_argument("--budget", type=int, default=1500, help="treasure value for --map random")
    ap.add_argument("--rocks", type=int, default=3, help="rocks for --map random")
    ap.add_argument("--lives", type=int, default=1, help="Life pickups for --map random")
    ap.add_argument("--hooks", type=int, default=1, help="more than 1: that many hooks at random places (GPU only)")
    ap.add_argument("--pig", choices=PIG_POLICIES, default="greedy")
    ap.add_argument("--miner", choices=["scripted", "smart", "efficient"], default="scripted")
    ap.add_argument("--lookahead", type=int, default=2, help="search depth of the margin pig")
    ap.add_argument("--gamma", type=float, default=0.85, help="per-second discount of the margin pig")
    ap.add_argument("--episodes", type=int, default=100)
    ap.add_argument("--iters", type=int, default=30)
    ap.add_argument("--seed", type=int, default=0, help="seed for match randomness and the tuner")
    ap.add_argument("--out", default=None, help="write the (tuned) map and parameters as JSON")

    g = ap.add_argument_group("game parameters (default: Unity values, or those stored in the map file)")
    g.add_argument("--pig-speed", type=float)
    g.add_argument("--digest", type=float, help="seconds after eating gold before the pig can eat more")
    g.add_argument("--digest-per-value", type=float, help="extra digest seconds per $ of gold eaten")
    g.add_argument("--hook-extend", type=float)
    g.add_argument("--hook-retract", type=float)
    g.add_argument("--telegraph-min", type=float)
    g.add_argument("--telegraph-max", type=float)
    g.add_argument("--cooldown", type=float)
    g.add_argument("--start-delay", type=float)
    g.add_argument("--round-time", type=float)
    g.add_argument("--no-kill", action="store_true", help="pure race: the hook can't hurt the pig")

    t = ap.add_argument_group("tune")
    t.add_argument("--tune-params", default="",
                   help=f"comma list of parameters the tuner may change, or 'all': {', '.join(TUNABLE_PARAMS)}")

    c = ap.add_argument_group("compare")
    c.add_argument("--pigs", default=",".join(PIG_POLICIES), help="comma list of pig policies")
    c.add_argument("--maps", type=int, default=10)
    c.add_argument("--seeds", type=int, default=5, help="matches per map")
    c.add_argument("--oracle-max-gold", type=int, default=8, help="skip the oracle above this many gold")
    c.add_argument("--node-budget", type=int, default=20000, help="oracle search nodes per match")
    c.add_argument("--best-order", action="store_true",
                   help="also find the best fixed collection order per map (tries every order: keep --n-gold small)")
    c.add_argument("--csv", default=None, help="write one row per match")
    return t, c


def parse_tune_params(ap: argparse.ArgumentParser, spec: str) -> List[str]:
    names = list(TUNABLE_PARAMS) if spec == "all" else [p.strip() for p in spec.split(",") if p.strip()]
    unknown = [p for p in names if p not in TUNABLE_PARAMS]
    if unknown:
        ap.error(f"unknown --tune-params {unknown}; choose from {list(TUNABLE_PARAMS)}")
    return names


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", choices=["eval", "tune", "compare"])
    add_arguments(ap)
    args = ap.parse_args()

    if args.command == "compare":
        compare(args, resolve_params(args, None))
        return

    game_map, stored = load_map(args)
    params = resolve_params(args, stored)
    print(f"map: {len(game_map.items)} items, ${game_map.total_value()} of treasure, "
          f"pig={args.pig}, miner={args.miner}")
    print(f"params: {params}")
    for i in game_map.items:
        print(f"  {i.type:12s} ({i.x:+5.2f}, {i.y:+5.2f})")

    if args.command == "eval":
        print(evaluate(game_map, params, args.episodes, args.pig, args.miner, args.seed, args.lookahead, args.gamma))
    else:
        tune_params = parse_tune_params(ap, args.tune_params)
        allow_rocks = any(i.type == "Rock" for i in game_map.items)
        game_map, params, _ = tune(game_map, params, args.episodes, args.iters, args.pig, args.miner,
                                   args.seed, tune_params, allow_rocks, depth=args.lookahead, gamma=args.gamma)
        print(f"params: {params}")
        for i in game_map.items:
            print(f"  {i.type:12s} ({i.x:+5.2f}, {i.y:+5.2f})")

    if args.out:
        with open(args.out, "w", encoding="utf-8") as f:
            json.dump({**game_map.to_json(), "params": asdict(params)}, f, indent=2)
        print(f"wrote {args.out}")


if __name__ == "__main__":
    main()
