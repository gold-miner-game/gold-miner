"""
Headless simulator for the gold-miner game, for experiment-driven map tuning.

The rules and numbers mirror the Unity scripts in Assets/Scripts:
  Hook.cs        extend 12, retract 16 x weight factor, circle-cast radius 0.2,
                 stops at the first treasure / rock / pig, an empty hook still reacts on the way back
  MinerAI.cs     start delay 1.5 s, telegraph 1-2 s, cooldown 0.8 s, overshoot 0.6,
                 2-3 treasure shots then one shot at the pig
  GameManager.cs 90 s round, 0 starting lives, 1.5 s invincibility,
                 ends on time-out / all treasure gone / pig killed; player wins if alive and richer
  PigController  speed 5, collects treasure and Life pickups by touching them, blocked by rocks

Differences from Unity (keep in mind when reading results):
  - The pig is clamped to the camera view; Game.unity currently has no walls.
  - Physics is simplified to circles; time step defaults to 0.02 s (Unity's fixed step).

Usage (Python 3.8+, no extra packages):
  python gold_miner_sim.py eval  --map current --episodes 200
  python gold_miner_sim.py eval  --map random --map-seed 7 --episodes 200
  python gold_miner_sim.py tune  --map-seed 7 --episodes 100 --iters 40 --out tuned_map.json
  python gold_miner_sim.py eval  --map tuned_map.json --miner smart

Plug in trained agents by subclassing PigAgent / MinerAgent (see the baseline agents below).
"""

import argparse
import json
import math
import random
from dataclasses import dataclass, field, asdict
from typing import List, Optional, Tuple

Vec = Tuple[float, float]

# ---------------------------------------------------------------- game constants

ARENA_X = 8.89          # camera half-width (ortho size 5 at 16:9)
ARENA_Y = 5.0           # camera half-height
HOOK_ORIGIN = (0.0, 3.4)
PIG_START = (0.0, -1.0)
PIG_RADIUS = 0.4        # circle 0.5 x prefab scale 0.8
PIG_SPEED = 5.0

HOOK_EXTEND = 12.0
HOOK_RETRACT = 16.0
HOOK_HIT_RADIUS = 0.2
OVERSHOOT = 0.6

ROUND_TIME = 90.0
STARTING_LIVES = 0
INVINCIBLE_TIME = 1.5

START_DELAY = 1.5
TELEGRAPH_MIN, TELEGRAPH_MAX = 1.0, 2.0
COOLDOWN = 0.8
MIN_TREASURE_SHOTS, MAX_TREASURE_SHOTS = 2, 3

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

@dataclass
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

    def copy(self) -> "GameMap":
        return GameMap([Item(i.type, i.x, i.y) for i in self.items], self.pig_start)

    def total_value(self) -> int:
        return sum(i.value for i in self.items if i.is_treasure)

    def to_json(self) -> dict:
        return {"pig_start": list(self.pig_start), "items": [asdict(i) for i in self.items]}

    @staticmethod
    def from_json(data: dict) -> "GameMap":
        return GameMap([Item(**i) for i in data["items"]], tuple(data.get("pig_start", PIG_START)))


def current_game_map() -> GameMap:
    """The hand-placed layout in Assets/Scenes/Game.unity."""
    return GameMap([Item("Gold_Large", 5, -3), Item("Gold_Small", -4, -2), Item("Gold_Medium", 3, 0)])


def position_ok(p: Vec, item_radius: float, others: List[Item], pig_start: Vec, spacing: float) -> bool:
    if not (PLACE_X[0] <= p[0] <= PLACE_X[1] and PLACE_Y[0] <= p[1] <= PLACE_Y[1]):
        return False
    if dist(p, pig_start) < 1.6 or dist(p, HOOK_ORIGIN) < 1.5:
        return False
    return all(dist(p, o.pos) >= spacing + item_radius + o.radius for o in others)


def random_map(rng: random.Random, budget: int = 1500, rocks: int = 3, lives: int = 1,
               spacing: float = 0.6) -> GameMap:
    """Heuristic starting point: spend a value budget on treasure, scatter everything with a
    minimum spacing (Poisson-disk style rejection sampling)."""
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

    gm = GameMap()
    for t in types:
        for _ in range(500):
            p = (rng.uniform(*PLACE_X), rng.uniform(*PLACE_Y))
            if position_ok(p, ITEM_TYPES[t][1], gm.items, gm.pig_start, spacing):
                gm.items.append(Item(t, *p))
                break
    return gm


# ---------------------------------------------------------------- game state seen by agents

@dataclass
class Hook:
    state: str = "idle"          # idle / extending / retracting
    pos: Vec = HOOK_ORIGIN
    direction: Vec = (0.0, -1.0)
    max_length: float = 0.0
    traveled: float = 0.0
    carried: Optional[Item] = None
    touched: set = field(default_factory=set)   # ids of things this shot already reacted to
    shot_end: Vec = HOOK_ORIGIN                 # far end of the locked trajectory


@dataclass
class Observation:
    """Everything an agent may look at. Agents must not modify it."""
    time_left: float
    pig_pos: Vec
    pig_vel: Vec
    lives: int
    invincible: bool
    player_money: int
    miner_money: int
    items: List[Item]                    # treasure, rocks and lives still on the map
    hook: Hook
    telegraph: Optional[Tuple[Vec, Vec, bool]]  # (start, end, aimed_at_player) while shown


# ---------------------------------------------------------------- agents

class PigAgent:
    def reset(self, rng: random.Random): self.rng = rng
    def act(self, obs: Observation) -> Vec:
        """Return the desired move direction (length <= 1)."""
        raise NotImplementedError


class MinerAgent:
    def reset(self, rng: random.Random): self.rng = rng
    def choose_target(self, obs: Observation) -> Tuple[Vec, bool]:
        """Called when the hook is idle and the cooldown is over.
        Return (aim point, aimed_at_player). The trajectory is locked when this is called."""
        raise NotImplementedError


class ScriptedMiner(MinerAgent):
    """Replica of MinerAI.cs: value-weighted random treasure, every 2-3 shots aim at the pig."""

    def reset(self, rng):
        super().reset(rng)
        self.treasure_shots_left = rng.randint(MIN_TREASURE_SHOTS, MAX_TREASURE_SHOTS)

    def choose_target(self, obs):
        treasures = [i for i in obs.items if i.is_treasure]
        if self.treasure_shots_left > 0 and treasures:
            self.treasure_shots_left -= 1
            pick = self.rng.choices(treasures, weights=[max(1, t.value) for t in treasures])[0]
            return pick.pos, False
        self.treasure_shots_left = self.rng.randint(MIN_TREASURE_SHOTS, MAX_TREASURE_SHOTS)
        return obs.pig_pos, True


class SmartMiner(ScriptedMiner):
    """Stronger stand-in: best value per second, steals what the pig is heading for,
    and leads the pig partially when shooting at it."""

    lead = 0.5   # fraction of full prediction used when aiming at the pig

    def choose_target(self, obs):
        treasures = [i for i in obs.items if i.is_treasure]
        if self.treasure_shots_left > 0 and treasures:
            self.treasure_shots_left -= 1
            return max(treasures, key=lambda t: self._utility(t, obs)).pos, False
        self.treasure_shots_left = self.rng.randint(MIN_TREASURE_SHOTS, MAX_TREASURE_SHOTS)
        flight = 0.5 * (TELEGRAPH_MIN + TELEGRAPH_MAX) + dist(HOOK_ORIGIN, obs.pig_pos) / HOOK_EXTEND
        return add(obs.pig_pos, mul(obs.pig_vel, flight * self.lead)), True

    @staticmethod
    def _utility(t: Item, obs: Observation) -> float:
        d = dist(HOOK_ORIGIN, t.pos)
        hook_time = 0.5 * (TELEGRAPH_MIN + TELEGRAPH_MAX) + d / HOOK_EXTEND + d / (HOOK_RETRACT * t.retract_mult)
        pig_time = dist(obs.pig_pos, t.pos) / PIG_SPEED
        steal_bonus = 1.5 if pig_time < hook_time + 1.0 else 1.0   # contested: grab it before the pig
        lost = pig_time < hook_time - 1.5                           # pig will clearly win it
        return 0.0 if lost else steal_bonus * t.value / (hook_time + COOLDOWN)


class GreedyPig(PigAgent):
    """Baseline player: chase the best value-per-distance item, dodge telegraphs and the hook."""

    danger_margin = 0.5

    def act(self, obs):
        # 1. Dodge: step out of any locked trajectory
        for a, b in self._danger_lines(obs):
            d = point_segment_distance(obs.pig_pos, a, b)
            if d < PIG_RADIUS + HOOK_HIT_RADIUS + self.danger_margin:
                line = normalize(sub(b, a))
                side = (-line[1], line[0])
                if dot(sub(obs.pig_pos, a), side) < 0:
                    side = mul(side, -1)
                return side

        # 2. Go for the best target that is not on a danger line
        best, best_score = None, 0.0
        for item in obs.items:
            if item.type == "Rock":
                continue
            worth = item.value if item.is_treasure else (150 if obs.lives == 0 else 40)
            if any(point_segment_distance(item.pos, a, b) < item.radius + HOOK_HIT_RADIUS + 0.3
                   for a, b in self._danger_lines(obs)):
                continue
            score = worth / (dist(obs.pig_pos, item.pos) + 0.5)
            if score > best_score:
                best, best_score = item, score
        return normalize(sub(best.pos, obs.pig_pos)) if best else (0.0, 0.0)

    @staticmethod
    def _danger_lines(obs):
        lines = []
        if obs.telegraph:
            lines.append((obs.telegraph[0], obs.telegraph[1]))
        if obs.hook.state != "idle" and obs.hook.carried is None:
            lines.append((HOOK_ORIGIN, obs.hook.shot_end))
        return lines


# ---------------------------------------------------------------- simulation

@dataclass
class MatchResult:
    player_won: bool
    killed: bool
    player_money: int
    miner_money: int
    duration: float
    end_reason: str


class Game:
    def __init__(self, game_map: GameMap, pig: PigAgent, miner: MinerAgent, seed: int, dt: float = 0.02):
        self.rng = random.Random(seed)
        self.items = [Item(i.type, i.x, i.y) for i in game_map.items]
        self.pig, self.miner = pig, miner
        pig.reset(random.Random(self.rng.random()))
        miner.reset(random.Random(self.rng.random()))
        self.dt = dt

        self.time_left = ROUND_TIME
        self.pig_pos = game_map.pig_start
        self.pig_vel = (0.0, 0.0)
        self.lives = STARTING_LIVES
        self.invincible_until = -1.0
        self.player_money = 0
        self.miner_money = 0
        self.hook = Hook()
        self.telegraph = None                 # (start, end, aimed_at_player)
        self.telegraph_left = 0.0
        self.miner_wait = START_DELAY         # start delay, then cooldowns
        self.had_treasure = any(i.is_treasure for i in self.items)
        self.playing = True
        self.killed = False
        self.end_reason = ""

    @property
    def now(self) -> float:
        return ROUND_TIME - self.time_left

    def observe(self) -> Observation:
        return Observation(self.time_left, self.pig_pos, self.pig_vel, self.lives,
                           self.now < self.invincible_until, self.player_money, self.miner_money,
                           self.items, self.hook, self.telegraph)

    def run(self) -> MatchResult:
        while self.playing:
            self.step()
        won = not self.killed and self.player_money > self.miner_money
        return MatchResult(won, self.killed, self.player_money, self.miner_money, self.now, self.end_reason)

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
        self.pig_vel = mul(move, PIG_SPEED)
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

        # Touching treasure / Life pickups collects them
        for item in list(self.items):
            if item.type != "Rock" and dist(p, item.pos) < PIG_RADIUS + item.radius:
                self.items.remove(item)
                if item.is_treasure:
                    self.player_money += item.value
                else:
                    self.lives += 1

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
        end = add(HOOK_ORIGIN, mul(direction, dist(aim, HOOK_ORIGIN) + OVERSHOOT))
        self.telegraph = (HOOK_ORIGIN, end, at_player)
        self.telegraph_left = self.rng.uniform(TELEGRAPH_MIN, TELEGRAPH_MAX)

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
            step = min(HOOK_EXTEND * dt, h.max_length - h.traveled)
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
            step = min(HOOK_RETRACT * mult * dt, remaining)
            if self.playing and h.carried is None and remaining > 1e-4:
                back = normalize(sub(HOOK_ORIGIN, h.pos))
                for _, thing in self.cast(h.pos, back, step):
                    self.handle_hit(thing)
                    if h.carried:
                        break
            h.pos = add(h.pos, mul(normalize(sub(HOOK_ORIGIN, h.pos)), step))
            if dist(h.pos, HOOK_ORIGIN) < 1e-3:
                h.pos, h.state, h.carried = HOOK_ORIGIN, "idle", None
                self.miner_wait = COOLDOWN

    def cast(self, start: Vec, direction: Vec, distance: float):
        """Everything the hook would touch this step, nearest first (CircleCastAll)."""
        hits = []
        for item in self.items:
            d = circle_cast(start, direction, distance, HOOK_HIT_RADIUS, item.pos, item.radius)
            if d is not None:
                hits.append((d, item))
        d = circle_cast(start, direction, distance, HOOK_HIT_RADIUS, self.pig_pos, PIG_RADIUS)
        if d is not None:
            hits.append((d, "pig"))
        hits.sort(key=lambda x: x[0])
        return hits

    def handle_hit(self, thing) -> bool:
        """Mirror of Hook.HandleHit: True if the hook should stop here."""
        h = self.hook
        key = "pig" if thing == "pig" else id(thing)
        if key in h.touched:
            return False
        h.touched.add(key)
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
            self.invincible_until = self.now + INVINCIBLE_TIME
        else:
            self.end("Killed by the hook!", killed=True)


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


def make_agents(miner_name: str):
    miner = SmartMiner() if miner_name == "smart" else ScriptedMiner()
    return GreedyPig(), miner


def evaluate(game_map: GameMap, episodes: int, miner_name: str = "scripted", base_seed: int = 0,
             dt: float = 0.02) -> Evaluation:
    """Play `episodes` matches. The same base_seed gives the same match seeds (common random numbers),
    so two maps evaluated with the same seed are compared fairly."""
    wins = kills = 0
    margin = 0.0
    for e in range(episodes):
        pig, miner = make_agents(miner_name)
        r = Game(game_map, pig, miner, seed=base_seed * 100003 + e, dt=dt).run()
        wins += r.player_won
        kills += r.killed
        margin += r.player_money - r.miner_money
    return Evaluation(wins / episodes, kills / episodes, margin / episodes, episodes)


def race_margin(p: Vec, item_type: str, pig_start: Vec) -> float:
    """Seconds the pig is ahead of the hook for an item at p (positive = pig gets there first).
    Smooth, so its gradient tells us which way to move an item to help one side."""
    d = dist(HOOK_ORIGIN, p)
    hook_time = 0.5 * (TELEGRAPH_MIN + TELEGRAPH_MAX) + d / HOOK_EXTEND
    pig_time = dist(pig_start, p) / PIG_SPEED
    return hook_time - pig_time


def propose(game_map: GameMap, help_player: bool, rng: random.Random, step: float = 0.7) -> GameMap:
    """One change that should shift the balance towards the losing side."""
    new = game_map.copy()
    treasures = [i for i in new.items if i.is_treasure]
    sign = 1.0 if help_player else -1.0
    move = rng.random()

    if move < 0.6 and treasures:
        # Move a treasure along the gradient of the race margin (higher value = moved more often)
        t = rng.choices(treasures, weights=[x.value for x in treasures])[0]
        eps = 0.05
        gx = (race_margin((t.x + eps, t.y), t.type, new.pig_start) - race_margin((t.x - eps, t.y), t.type, new.pig_start)) / (2 * eps)
        gy = (race_margin((t.x, t.y + eps), t.type, new.pig_start) - race_margin((t.x, t.y - eps), t.type, new.pig_start)) / (2 * eps)
        g = normalize((gx, gy))
        jitter = (rng.gauss(0, 0.3), rng.gauss(0, 0.3))
        target = add((t.x, t.y), add(mul(g, sign * step), jitter))
        others = [o for o in new.items if o is not t]
        if position_ok(target, t.radius, others, new.pig_start, 0.4):
            t.x, t.y = target
    elif move < 0.8 and len(treasures) >= 2:
        # Swap the types of two treasures: put the valuable one where the losing side has the edge
        a, b = rng.sample(treasures, 2)
        ma, mb = race_margin(a.pos, a.type, new.pig_start), race_margin(b.pos, b.type, new.pig_start)
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
    return new


def tune(start: GameMap, episodes: int, iters: int, miner_name: str, seed: int,
         tolerance: float = 0.05, log=print) -> Tuple[GameMap, Evaluation]:
    """Experiment-driven search: play the map, then keep changes that move the win rate towards 50%."""
    rng = random.Random(seed)
    best = start
    best_eval = evaluate(best, episodes, miner_name, base_seed=seed)
    log(f"start   {best_eval}")
    for it in range(1, iters + 1):
        if best_eval.imbalance <= tolerance:
            log(f"balanced within +/-{tolerance:.0%}, stopping")
            break
        help_player = best_eval.win_rate < 0.5
        candidate = propose(best, help_player, rng)
        cand_eval = evaluate(candidate, episodes, miner_name, base_seed=seed)
        better = (cand_eval.imbalance < best_eval.imbalance or
                  (cand_eval.imbalance == best_eval.imbalance and abs(cand_eval.mean_margin) < abs(best_eval.mean_margin)))
        if better:
            best, best_eval = candidate, cand_eval
        log(f"iter {it:3d} {'help player' if help_player else 'help miner '} "
            f"{'KEEP' if better else 'skip'}  {cand_eval}")
    log(f"final   {best_eval}")
    return best, best_eval


# ---------------------------------------------------------------- command line

def load_map(spec: str, map_seed: int) -> GameMap:
    if spec == "current":
        return current_game_map()
    if spec == "random":
        return random_map(random.Random(map_seed))
    with open(spec, encoding="utf-8") as f:
        return GameMap.from_json(json.load(f))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", choices=["eval", "tune"])
    ap.add_argument("--map", default="random", help="'current', 'random', or a map .json file")
    ap.add_argument("--map-seed", type=int, default=1, help="seed for --map random")
    ap.add_argument("--miner", choices=["scripted", "smart"], default="scripted")
    ap.add_argument("--episodes", type=int, default=100)
    ap.add_argument("--iters", type=int, default=30)
    ap.add_argument("--seed", type=int, default=0, help="seed for match randomness and the tuner")
    ap.add_argument("--out", default=None, help="write the (tuned) map as JSON")
    args = ap.parse_args()

    game_map = load_map(args.map, args.map_seed)
    print(f"map: {len(game_map.items)} items, ${game_map.total_value()} of treasure, miner={args.miner}")
    for i in game_map.items:
        print(f"  {i.type:12s} ({i.x:+5.2f}, {i.y:+5.2f})")

    if args.command == "eval":
        print(evaluate(game_map, args.episodes, args.miner, base_seed=args.seed))
    else:
        game_map, _ = tune(game_map, args.episodes, args.iters, args.miner, args.seed)
        for i in game_map.items:
            print(f"  {i.type:12s} ({i.x:+5.2f}, {i.y:+5.2f})")

    if args.out:
        with open(args.out, "w", encoding="utf-8") as f:
            json.dump(game_map.to_json(), f, indent=2)
        print(f"wrote {args.out}")


if __name__ == "__main__":
    main()
