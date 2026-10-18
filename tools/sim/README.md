# Gold Miner match simulator

`gold_miner_sim.py` plays the game without Unity, hundreds of matches per second, so we can
measure how fair a map is and search for maps where both sides win about half the time.

- **Standalone:** plain Python 3.8+, standard library only. No Unity, no packages to install.
- **A copy of the rules, not a link to them:** the numbers below were copied by hand from the C#
  scripts. If you change the game in Unity, change the matching constant here too
  (see [Keeping it in sync](#keeping-it-in-sync-with-unity)).

```
python tools/sim/gold_miner_sim.py eval --map current --episodes 200
```

---

## Why it exists

We want generated maps instead of hand-placed treasure, and we want those maps to be
**competitive**: neither the player nor the miner should win most of the time.

"Competitive" is hard to judge by eye, so we measure it: put two agents on a map, play many
matches, and count who wins. A map where the player wins about 50% of matches is a good map.
If one side wins too often, change the map in that side's disfavour and measure again. That
loop is what the `tune` command does.

In research terms this is **simulation-based procedural content generation** used for
**automated game balancing**. See [Background reading](#background-reading).

---

## The game, as the simulator models it

Two sides compete for the same treasure in a 90-second round.

| | Player (the pig) | Miner (AI) |
|---|---|---|
| Where | Starts at (0, −1), moves freely | Fixed at the top; hook fires from (0, 3.4) |
| How it scores | Walks into a treasure: gets its value | Hooks a treasure: gets its value as soon as it is grabbed |
| How it attacks | Can't attack | Can shoot the hook at the pig |
| Loses when | Hit by the hook with 0 lives | Has less money than the player at the end |

### Coordinates

The arena is the camera view: x from −8.89 to +8.89, y from −5 to +5 (orthographic size 5,
16:9). Up is +y. The miner sits at the top centre, so the hook always shoots downward-ish.

```
 y=+5  ┌───────────────────────────────────────┐
       │                 MINER                  │
 y=3.4 │                   ●  hook origin       │
       │                  /                      │
       │                 /  hook line            │
       │     $          /         $$$            │
 y=−1  │              🐷 pig start               │
       │        $$          $                    │
 y=−5  └───────────────────────────────────────┘
      x=−8.89                                x=+8.89
```

### Objects

| Type | Value | Radius | Hook pull-back speed | What happens on contact |
|---|---|---|---|---|
| Gold_Small | $50 | 0.20 | ×1.0 | Pig: collects. Hook: grabs, carries back |
| Gold_Medium | $100 | 0.35 | ×0.7 | same |
| Gold_Large | $250 | 0.55 | ×0.45 (heavy, slow) | same |
| Diamond | $600 | 0.19 | ×1.1 (light, fast) | same |
| Rock | – | 0.47 | – | Pig: blocked (solid). Hook: stops, rock shatters |
| Life | – | 0.20 | – | Pig: +1 life. Hook: passes through |

### The pig, Mr. Hyde (`MrHydeController.cs`)

- Moves at **5 units/s** in any direction (diagonals are not faster). Radius 0.4.
- Collects treasure and Life pickups by touching them.
- Rocks block it.
- Starts with **0 lives**. After losing a life it is invincible for **1.5 s**.

### The hook (`Hook.cs`)

- Shoots out at **12 units/s** in a straight line, up to the aim point + 0.6.
- Comes back at **16 units/s × the carried item's pull-back factor** (empty = ×1).
- It is a circle of radius 0.2. It stops at the **first** thing it touches:
  - treasure: grabbed, miner is paid immediately, carried back;
  - rock: rock shatters, hook returns empty;
  - pig: the pig is hit (loses a life, or the round ends).
- Life pickups don't stop it.
- An **empty** hook still reacts to things it touches on the way back. A loaded hook ignores
  everything.

### The miner's rhythm (`MinerAI.cs`)

```
wait 1.5 s ──► pick target ──► telegraph line shown 1–2 s ──► fire ──► hook returns ──► wait 0.8 s ──┐
                    ▲                                                                                │
                    └────────────────────────────────────────────────────────────────────────────────┘
```

- The aim is **locked when the telegraph line appears**. The player can see it and dodge.
- It shoots at treasure 2–3 times, then once at the pig, then repeats.
- Treasure choice: random, weighted by value (a $600 diamond is 12× as likely as a $50 gold).
- When aiming at the pig it aims where the pig is *now* (no leading).

### Round rules (`GameManager.cs`)

The round ends when any of these happens:

1. 90 seconds pass;
2. all treasure is gone (only if there was treasure at the start);
3. the pig is hit with 0 lives — the player **loses**.

Otherwise the player **wins if they have more money** than the miner. A tie is a loss.

---

## How the simulation runs

Each match is a loop of fixed time steps (0.02 s, Unity's physics step). Each step:

1. **Pig:** ask the pig agent for a direction, move, get pushed out of rocks, stay in the arena,
   collect anything touched.
2. **Miner:** count down the wait or the telegraph; when the hook is free, ask the miner agent
   for a target and start a telegraph; when the telegraph ends, fire.
3. **Hook:** move out or back. Hits are found with the same circle-cast test Unity uses,
   nearest first.
4. **Rules:** tick the clock, check the end conditions.

Randomness (telegraph length, the miner's choices) comes from a seed, so a match with the same
map, agents and seed always plays out identically.

---

## Agents

Agents only see an `Observation` (time, positions, money, items, hook state, telegraph line) and
return a decision. They are the part to replace with trained ones.

| Class | Side | Behaviour |
|---|---|---|
| `ScriptedMiner` | Miner | Exact copy of today's `MinerAI.cs` |
| `SmartMiner` | Miner | Picks the best value per second, steals treasure the pig is racing for, aims 50% of the way ahead of the moving pig |
| `GreedyPig` | Player | Goes for the best value per distance, skips items on a danger line, steps sideways out of telegraph lines and the hook's path |

To add your own (for example a trained ML-Agents policy exported to Python):

```python
class MyPig(PigAgent):
    def act(self, obs: Observation) -> Vec:
        return (dx, dy)            # direction, length <= 1

class MyMiner(MinerAgent):
    def choose_target(self, obs: Observation) -> Tuple[Vec, bool]:
        return (x, y), aimed_at_player
```

Then return them from `make_agents()`.

---

## Maps

A map is a pig start position plus a list of items. It is saved as JSON:

```json
{
  "pig_start": [0.0, -1.0],
  "items": [
    {"type": "Gold_Large", "x": 5.0, "y": -3.0},
    {"type": "Rock",       "x": 1.2, "y": 0.4}
  ]
}
```

- `--map current`: the hand-placed layout from `Assets/Scenes/Game.unity` (3 golds).
- `--map random --map-seed N`: a generated map. It spends a value budget ($1,500 by default) on
  treasure (at most one diamond, 1–2 large, the rest medium/small), adds 3 rocks and 1 Life, and
  scatters them with a minimum spacing. Items stay below the miner (y ≤ 2.4) and away from the
  pig start and the hook origin.
- `--map file.json`: load a saved map.

---

## Commands

Run from `tools/sim/` (or prefix the script with `tools/sim/` from the repo root):

```
python gold_miner_sim.py eval --map current --episodes 200
python gold_miner_sim.py eval --map random --map-seed 7 --miner smart --episodes 200
python gold_miner_sim.py tune --map random --map-seed 7 --episodes 100 --iters 40 --out tuned.json
python gold_miner_sim.py eval --map tuned.json --episodes 400
```

| Option | Meaning | Default |
|---|---|---|
| `--map` | `current`, `random`, or a `.json` file | `random` |
| `--map-seed` | which random map | 1 |
| `--miner` | `scripted` (today's AI) or `smart` | `scripted` |
| `--episodes` | matches per evaluation | 100 |
| `--iters` | tuning steps | 30 |
| `--seed` | match randomness; same seed = same matches | 0 |
| `--out` | save the map as JSON | – |

### Reading the output

```
player win 48.0% (+/-5.0%)  killed 12.0%  avg margin  +35.5  [100 games]
```

- **player win:** share of matches the player won. The goal is about 50%.
- **(+/-5.0%):** statistical uncertainty. It shrinks with more episodes: 100 games ≈ ±5%,
  400 games ≈ ±2.5%.
- **killed:** share of matches where the pig was hooked with no lives left.
- **avg margin:** average (player money − miner money). Positive = player ahead.

---

## How `tune` works

```
evaluate map (N matches) ──► who is losing? ──► propose one change that helps them
        ▲                                                │
        │                                                ▼
        └──── keep it if the win rate got closer to 50%, otherwise discard ◄── evaluate new map
```

Each proposal is one of:

1. **Move a treasure** towards the losing side. The direction comes from the *race margin*:
   for a spot on the map, how many seconds sooner the pig gets there than the hook. This is a
   smooth function, so its gradient says which way to move a treasure to help either side.
2. **Swap two treasures' values**, putting the valuable one where the losing side has the edge.
3. **Rocks:** move one between the hook and a treasure (cover helps the player), or remove one
   (helps the miner).

The old and new map are played on the **same match seeds**, so a difference in results comes
from the map, not from luck. Tuning stops when the win rate is within ±5% of 50%.

---

## What we have found so far

| Map | Player wins | Avg margin | Match length |
|---|---|---|---|
| Current `Game.unity` | 100% | +$300 | 3.5 s |
| Random map, $1,500 | 100% | +$1,139 | 9.2 s |
| Same map after 60 tuning steps | 100% | +$1,043 | – |

With today's rules **the player always wins, whatever the map**. The pig clears all the treasure
in seconds, before the miner's first shot (1.5 s wait + 1–2 s telegraph), and the round ends
when the treasure runs out. Moving treasure around cannot fix that, so the next step is to let
the search also change rule settings (pig speed, telegraph time, start delay, when the round
ends) and to score by money margin as well as win rate.

---

## Limitations

- **The pig is kept inside the camera view.** `Game.unity` has no walls, so in Unity the pig can
  leave the screen.
- **Circles only.** Every collider is treated as a circle.
- **Agents are scripted stand-ins.** A map tuned against `GreedyPig` is only balanced for players
  who play like `GreedyPig`. Real balance needs strong (trained) agents on both sides.
- **Balanced is not the same as fun.** Consider also scoring match length or close calls.

---

## Keeping it in sync with Unity

All game numbers are constants at the top of `gold_miner_sim.py`:

| Constant(s) | Unity source |
|---|---|
| `HOOK_EXTEND`, `HOOK_RETRACT`, `HOOK_HIT_RADIUS`, pull-back factors in `ITEM_TYPES` | `Assets/Scripts/Hook.cs` |
| `START_DELAY`, `TELEGRAPH_MIN/MAX`, `COOLDOWN`, `OVERSHOOT`, `MIN/MAX_TREASURE_SHOTS` | `Assets/Scripts/MinerAI.cs` (and its values in `Game.unity`) |
| `ROUND_TIME`, `STARTING_LIVES`, `INVINCIBLE_TIME` | `Assets/Scripts/GameManager.cs` |
| `PIG_SPEED`, `PIG_RADIUS`, `PIG_START` | `Assets/Scripts/MrHydeController.cs`, `Assets/Prefabs/MrHyde.prefab`, `Game.unity` |
| values and radii in `ITEM_TYPES` | `Assets/Prefabs/Gold_*.prefab`, `Diamond.prefab`, `Rock.prefab`, `Life.prefab` |
| `HOOK_ORIGIN`, `ARENA_X/Y` | Miner position and camera in `Game.unity` |

Inspector values in a scene override script defaults, so check the scene too.

---

## Background reading

- Rupp et al., *Simulation-Driven Balancing of Competitive Game Levels with RL*, 2025:
  https://arxiv.org/abs/2503.18748 (the closest match to this approach)
- *Level the Level: Balancing Game Levels for Asymmetric Player Archetypes with RL*, 2025:
  https://arxiv.org/abs/2503.24099 (uneven sides, like miner vs pig)
- *It might be balanced, but is it actually good?*, 2024: https://arxiv.org/abs/2407.11396
- *The Quest for Content: A Survey of Search-Based PCG*, 2023: https://arxiv.org/abs/2311.04710
- Samvelyan et al., *MAESTRO*, ICLR 2023: https://arxiv.org/abs/2303.03376 (choosing maps and
  opponents together when training two-player agents)
- Unity ML-Agents self-play:
  https://github.com/Unity-Technologies/ml-agents/blob/develop/docs/Training-Self-Play.md
