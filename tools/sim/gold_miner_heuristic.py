"""
A differentiable heuristic for the value difference (pig money - miner money), with both sides on
fixed "no enemy" policies, a balancer built on it, and a level generator.

Policies (each plans as if it were alone):
  pig    plans a route over the gold graph from its start: the visiting order that maximises
         sum(value * gamma^arrival time), walking at pig_speed and digesting digest_time +
         digest_per_value * value after each gold (every order is tried for up to 8 gold, beyond
         that a greedy route). In the game it follows the route, skips gold that is gone, and eats
         any gold it walks over while not digesting.
  hooks  collect as efficiently as possible (EfficientMiner): each free hook aims at the remaining
         gold with the best value / cycle time from where it stands (Smith's rule), never gold
         another hook is going for; the shot grabs the first gold on its line.

The heuristic replays both plans as discrete event simulations (no gradient), then times every
race smoothly along them:
  A_k   pig eats gold k: walk / pig_speed along its actual eating order, waiting out the digest
        of the gold it got before (an absolute "ready" time, carried over gold it loses)
  T_k   first hook to grab gold k: its earlier cycles (empty trips for gold the pig took while it
        was on the way) + telegraph + hit distance / hook_extend
  P_k   P(pig gets gold k) = sigmoid((T_k - A_k) / w_k), w_k the spread of the random telegraph
        times before that grab (the expected race, not an arbitrary smoothing)
  margin = sum_k value_k * (P_k - P(hook gets k))
The two sides are coupled by a few damped fixed-point passes; the hook plan is redone each pass
from the pig's times. Gold no hook goes for gets the gradient of a counterfactual race (the
earliest a hook could have grabbed it) without changing the value. On 64 random designs
(6 gold, 1-4 hooks) the heuristic tracks the game with correlation ~0.98.

Balancing: Adam over the gold positions, digest_time, digest_per_value, hook_extend and
hook_retract (pig speed fixed, 5 by default), with penalties that keep gold apart, in the
placement area, off the pig's start and off the hooks. The target is a margin (--target) or a pig
win rate (--target-win; P(win) ~ Phi((mean + offset) / sd) from the per-gold odds). Every
--verify-every steps the design is played in the GPU simulator with the same policies and the
offset is reset so the heuristic matches the game there. If a stage ends with the pig still too
strong, a hook is added at a random place (up to --max-hooks).

Levels: one more gold per level, one more hook every --hook-every levels (or whenever needed),
target pig win rate from --win-from to --win-to; each level is balanced from --tries random maps
and the closest is checked on --final-matches fresh matches.

Usage (needs torch):
  python gold_miner_heuristic.py check   --n-gold 6 --designs 64
  python gold_miner_heuristic.py balance --map-seed 7924 --n-gold 6 --hooks 1 --target-win 0.5 --out balanced.json
  python gold_miner_heuristic.py levels  --levels 8 --gold-start 4 --tries 3 --out-dir levels
"""

import argparse
import itertools
import json
import math
import random
import time
from dataclasses import asdict
from typing import Dict, List, Tuple

import torch

import gold_miner_sim as sim
import gold_miner_torch as gt
from gold_miner_sim import GameMap, GameParams, ITEM_TYPES, PIG_RADIUS

TUNED = ["digest_time", "digest_per_value", "hook_extend", "hook_retract"]
SIGMA_W0 = 0.1   # seconds: smallest race width
HOOK_CLEARANCE = 0.8   # gold edge to hook origin
WIN_CLAMP = 0.02       # game win rates are clamped to [c, 1-c] before calibrating
SOFT_MAX = 0.1   # seconds: softness of "wait for the later of walking and digesting"


def smooth_max(a: torch.Tensor, b: torch.Tensor, s: float = SOFT_MAX) -> torch.Tensor:
    """max(a, b), rounded over about s, so a digest shorter than the walk still has a (small) gradient."""
    return torch.maximum(a, b) + s * torch.nn.functional.softplus(-(a - b).abs() / s)


# ---------------------------------------------------------------- the design as tensors

class Design:
    """Gold positions (N,2) and parameters as float64 tensors; gold and tuned params require grad."""

    def __init__(self, game_map: GameMap, params: GameParams, tuned: List[str], grad: bool = True):
        self.map, self.params, self.tuned = game_map, params, tuned
        f64 = dict(dtype=torch.float64)
        self.xy = torch.tensor([[i.x, i.y] for i in game_map.items], **f64).requires_grad_(grad)
        self.val = torch.tensor([i.value for i in game_map.items], **f64)
        self.rad = torch.tensor([i.radius for i in game_map.items], **f64)
        self.mult = torch.tensor([i.retract_mult for i in game_map.items], **f64)
        self.hooks = torch.tensor([list(h) for h in game_map.hooks], **f64)
        self.pig = torch.tensor(game_map.pig_start, **f64)
        self.p = {n: torch.tensor(float(getattr(params, n)), **f64).requires_grad_(grad and n in tuned)
                  for n in sim.GameParams.__dataclass_fields__ if n != "hook_kills"}

    def variables(self) -> List[torch.Tensor]:
        return [self.xy] + [self.p[n] for n in self.tuned]

    def to_game(self) -> Tuple[GameMap, GameParams]:
        m = self.map.copy()
        for it, (x, y) in zip(m.items, self.xy.tolist()):
            it.x, it.y = x, y
        return m, self.params.replace(**{n: self.p[n].item() for n in self.tuned})


# ---------------------------------------------------------------- plans (no gradient)

def route_times(d: Design, order: torch.Tensor, got: torch.Tensor = None) -> torch.Tensor:
    """Pig arrival times along routes. order (R, N) gold indices; got (N,) P(pig got gold), used to
    charge digest only for gold it got (None = all). Returns (R, N) arrival time per position."""
    p = d.p
    xy = d.xy[order]                                                       # (R,N,2)
    prev = torch.cat([d.pig.expand(order.shape[0], 1, 2), xy[:, :-1]], dim=1)
    walk = torch.relu((xy - prev).norm(dim=-1) - (PIG_RADIUS + d.rad[order]))
    digest = p["digest_time"] + p["digest_per_value"] * d.val[order]
    if got is not None:
        digest = digest * got[order]
    t = torch.zeros(order.shape[0], dtype=xy.dtype)
    out = []
    for i in range(order.shape[1]):
        ready = t + (digest[:, i - 1] if i > 0 else 0.0)
        t = torch.maximum(t + walk[:, i] / p["pig_speed"], ready)
        out.append(t)
    return torch.stack(out, dim=1)


def plan_pig(d: Design, gamma: float = 0.85, exact_up_to: int = 8) -> List[int]:
    """Best route on the gold graph if the pig were alone."""
    n = len(d.map.items)
    with torch.no_grad():
        if n <= exact_up_to:
            order = torch.tensor(list(itertools.permutations(range(n))), dtype=torch.long)
            A = route_times(d, order)
            score = (d.val[order] * gamma ** A).sum(1)
            return order[score.argmax()].tolist()
        left, route = list(range(n)), []   # greedy for big maps: best value * gamma^arrival next
        while left:
            best = max(left, key=lambda j: (d.val[j] * gamma ** route_times(d, torch.tensor([route + [j]]))[0, -1]).item())
            route.append(best)
            left.remove(best)
        return route


def hook_cycle(d: Design, h: int, k) -> torch.Tensor:
    """EfficientMiner's ranking cycle for aiming hook h at gold k (centre distance, as in the game)."""
    p = d.p
    dist = (d.xy[k] - d.hooks[h]).norm(dim=-1)
    tel = 0.5 * (p["telegraph_min"] + p["telegraph_max"])
    return tel + dist / p["hook_extend"] + dist / (p["hook_retract"] * d.mult[k]) + p["cooldown"]


def shot_hit(d: Design, h: int, k: int, j: int):
    """Distance along hook h's shot, aimed at gold k, at which it touches gold j (the game's
    circle cast), or None if j is not on the line. Differentiable in the positions."""
    o = d.hooks[h]
    to_k = d.xy[k] - o
    reach = to_k.norm() + d.p["overshoot"]
    u = to_k / to_k.norm()
    v = d.xy[j] - o
    r = sim.HOOK_HIT_RADIUS + d.rad[j]
    if (v * v).sum().item() <= (r * r).item():   # the hook starts inside it
        return torch.zeros((), dtype=torch.float64)
    s_ = (v * u).sum()
    disc = s_ * s_ - ((v * v).sum() - r * r)
    if disc.item() < 0 or s_.item() < 0:
        return None
    t = s_ - disc.clamp_min(1e-12).sqrt()
    return t if 0 <= t.item() <= reach.item() else None


def empty_trip(d: Design, h: int, k: int) -> torch.Tensor:
    """A shot at k that comes back with nothing: full extension + overshoot and back."""
    p = d.p
    tel = 0.5 * (p["telegraph_min"] + p["telegraph_max"])
    reach = (d.xy[k] - d.hooks[h]).norm() + p["overshoot"]
    return tel + reach * (1 / p["hook_extend"] + 1 / p["hook_retract"]) + p["cooldown"]


def plan_hooks(d: Design, A=None) -> List[Tuple[int, int, int]]:
    """EfficientMiner as an event simulation: (hook, target, gold it grabs) per shot that grabs
    something. A free hook aims at the best value / cycle among gold still there (the pig has eaten
    gold k by time A[k]; None = no pig) and not claimed by a hook still out; when the shot gets
    there it grabs the first gold on its line still there, or comes back empty. Events are taken in
    time order, so a shot only grabs what another hook hasn't already. Discrete (no gradient), like
    the pig's route."""
    H, n = d.hooks.shape[0], len(d.map.items)
    A = [math.inf] * n if A is None else [float(a) for a in A]
    with torch.no_grad():
        p = {k: v.item() for k, v in d.p.items()}
        tel = 0.5 * (p["telegraph_min"] + p["telegraph_max"])
        ext = p["hook_extend"]
        free = [p["start_delay"]] * H
        pending = [None] * H                  # (target, choose time) of a shot in the air
        hooked, claim, seq = set(), {}, []    # claim: target -> until the hook is home

        def there(j, t):
            return j not in hooked and not A[j] < t

        def grab_of(h):
            """First gold the pending shot of h reaches that is still there then: (time, gold, hit)."""
            k, t0 = pending[h]
            line = sorted((hit.item(), j) for j in range(n) for hit in [shot_hit(d, h, k, j)] if hit is not None)
            for hit, j in line:
                T = t0 + tel + hit / ext
                if there(j, T):
                    return T, j, hit
            reach = (d.xy[k] - d.hooks[h]).norm().item() + p["overshoot"]
            return t0 + tel + reach / ext, None, None   # empty: back at full extension

        while True:
            events = []
            for h in range(H):
                if pending[h] is not None:
                    events.append((grab_of(h)[0], 1, h))
                elif free[h] < p["round_time"]:
                    events.append((free[h], 0, h))
            if not events:
                return seq
            t, kind, h = min(events)
            if kind == 0:      # hook h chooses a target
                alive = [k for k in range(n) if there(k, t)]
                if not alive:
                    free[h] = math.inf
                    continue
                cand = [k for k in alive if not claim.get(k, -math.inf) > t] or alive
                k = max(cand, key=lambda j: (d.val[j] / hook_cycle(d, h, j)).item())
                pending[h] = (k, t)
                claim[k] = math.inf
            else:              # the shot of hook h gets there
                T, j, hit = grab_of(h)
                k, t0 = pending[h]
                pending[h] = None
                if j is None:
                    home = t0 + empty_trip(d, h, k).item() - p["cooldown"]
                else:
                    hooked.add(j)
                    seq.append((h, k, j))
                    home = T + hit / (p["hook_retract"] * d.mult[j].item())
                claim[k] = home
                free[h] = home + p["cooldown"]


def eating_order(d: Design, route: List[int], T) -> List[int]:
    """The order the pig actually eats in: it follows `route`, but eats any gold it walks over
    while not digesting (as in the game), unless a hook took it first (T = hook grab times).
    Hard simulation, no gradient; a gold a hook takes first stays in the order (the soft race
    decides it)."""
    with torch.no_grad():
        p = {k: v.item() for k, v in d.p.items()}
        xy, rad, val = d.xy.detach().tolist(), d.rad.tolist(), d.val.tolist()
        T = [float(x) for x in T]
        v = p["pig_speed"]
        pos, t, ready = list(d.pig.tolist()), 0.0, 0.0
        done, out, queue = set(), [], list(route)
        while queue:
            k = queue[0]
            if k in done:
                queue.pop(0)
                continue
            dx, dy = xy[k][0] - pos[0], xy[k][1] - pos[1]
            dist = math.hypot(dx, dy)
            L = max(dist - (PIG_RADIUS + rad[k]), 0.0)
            ux, uy = (dx / dist, dy / dist) if dist > 1e-9 else (0.0, 0.0)
            first = None   # (eat time, gold, distance along the leg)
            for j in range(len(xy)):
                if j == k or j in done:
                    continue
                wx, wy = xy[j][0] - pos[0], xy[j][1] - pos[1]
                s_ = wx * ux + wy * uy
                perp2 = wx * wx + wy * wy - s_ * s_
                R = PIG_RADIUS + rad[j]
                if perp2 > R * R:
                    continue
                h_ = math.sqrt(R * R - perp2)
                s_in, s_out = max(s_ - h_, 0.0), min(s_ + h_, L)
                if s_in > s_out:
                    continue
                tau = max(t + s_in / v, ready)
                if tau <= t + s_out / v and T[j] > tau and (first is None or tau < first[0]):
                    first = (tau, j, (tau - t) * v)
            if first is not None:
                tau, j, along = first
                out.append(j); done.add(j)
                pos = [pos[0] + ux * along, pos[1] + uy * along]
                t, ready = tau, tau + p["digest_time"] + p["digest_per_value"] * val[j]
                continue
            arrive = max(t + L / v, ready)
            out.append(k); done.add(k); queue.pop(0)
            if T[k] > arrive:      # the pig gets it
                pos, t, ready = list(xy[k]), arrive, arrive + p["digest_time"] + p["digest_per_value"] * val[k]
            else:                  # a hook took it: the pig turns from where it got to
                along = min(max(T[k] - t, 0.0) * v, L)
                pos, t = [pos[0] + ux * along, pos[1] + uy * along], max(T[k], t)
        return out


# ---------------------------------------------------------------- the heuristic

def heuristic(d: Design, route: List[int], seq: List[Tuple[int, int, int]] = None, passes: int = 8) -> Dict:
    """Expected value difference with the plans fixed; differentiable in the design. The hooks'
    plan is redone every pass from the pig's current arrival times unless `seq` is given."""
    p = d.p
    n = len(route)
    tel = 0.5 * (p["telegraph_min"] + p["telegraph_max"])
    tel_sd = (p["telegraph_max"] - p["telegraph_min"]) / math.sqrt(12)
    P = torch.zeros(n, dtype=torch.float64)
    A = torch.full((n,), 1e4, dtype=torch.float64)   # first pass: the hooks as if alone
    fixed = seq is not None
    for it in range(passes):
        if not fixed:
            seq = plan_hooks(d, None if it == 0 else A.detach())
        # Hooks: work through their shots. A shot at gold the pig took before the hook chose it
        # costs nothing; gold it took while the hook was on its way costs an empty trip
        free = [p["start_delay"]] * d.hooks.shape[0]
        shots = [0] * d.hooks.shape[0]
        T_try = [[] for _ in range(n)]    # every shot that reaches gold j: (time, width)
        # Gold no hook goes for: the earliest a hook could have grabbed it, aiming there at one of
        # its choices instead (keeps a gradient where the plan never contests a gold)
        reach = (d.xy[None] - d.hooks[:, None]).norm(dim=-1) - (sim.HOOK_HIT_RADIUS + d.rad[None])   # (H,N)
        T_cf = [[] for _ in range(n)]

        def could(h, width):
            tt = free[h] + tel + reach[h].clamp_min(0) / p["hook_extend"]
            for g in range(n):
                T_cf[g].append((tt[g], width))

        for h in range(d.hooks.shape[0]):
            if not any(e[0] == h for e in seq):
                could(h, tel_sd + SIGMA_W0)
        for h, k, j in seq:
            could(h, math.sqrt(shots[h] + 1) * tel_sd + SIGMA_W0)
            hit = shot_hit(d, h, k, j)
            if hit is None:   # the hook sits on the gold
                hit = torch.zeros((), dtype=torch.float64)
            aim = free[h] + tel
            Tj = aim + hit / p["hook_extend"]
            shots[h] += 1
            Wj = math.sqrt(shots[h]) * tel_sd + SIGMA_W0
            T_try[j].append((Tj, Wj))
            before = torch.sigmoid((free[h] - A[j]) / Wj)
            took = torch.sigmoid((Tj - A[j]) / Wj)
            cycle = tel + hit / p["hook_extend"] + hit / (p["hook_retract"] * d.mult[j]) + p["cooldown"]
            free[h] = free[h] + (1 - took) * cycle + (took - before).clamp_min(0) * empty_trip(d, h, k)
        for h in range(d.hooks.shape[0]):
            if any(e[0] == h for e in seq):
                could(h, math.sqrt(shots[h] + 1) * tel_sd + SIGMA_W0)   # its next choice
        # The race for j is against the first hook to reach it (smooth min); gold no hook goes for
        # is never raced (T = 1e4) but gets the counterfactual race's gradient (straight through)
        def soft_min(tr):
            return -SIGMA_W0 * torch.logsumexp(torch.stack([t for t, _ in tr]) / -SIGMA_W0, 0)

        def width(tr):
            return min(tr, key=lambda x: x[0].item())[1]

        never = torch.tensor([not tr for tr in T_try])
        T = torch.stack([soft_min(tr) if tr else torch.tensor(1e4, dtype=torch.float64) for tr in T_try])
        W = torch.tensor([width(tr) if tr else 1.0 for tr in T_try], dtype=torch.float64)
        Tc = torch.stack([soft_min(cf) for cf in T_cf])
        Wc = torch.tensor([width(cf) for cf in T_cf], dtype=torch.float64)
        # Pig: walk the route. If a hook takes the next gold first, the pig was heading for it
        # until then and turns from where it got to
        A_new = [None] * n
        order = eating_order(d, route, T.detach())
        # ready: absolute time the pig can eat again; a digest carries over gold it fails to get
        t, pos, ready = torch.zeros((), dtype=torch.float64), d.pig, torch.zeros((), dtype=torch.float64)
        for k in order:
            walk = torch.relu((d.xy[k] - pos).norm() - (PIG_RADIUS + d.rad[k]))
            A_new[k] = smooth_max(t + walk / p["pig_speed"], ready)
            got = torch.sigmoid((T[k] - A_new[k]) / W[k])
            frac = ((T[k] - t) / (A_new[k] - t + 1e-6)).clamp(0, 1)
            t = got * A_new[k] + (1 - got) * (t + frac * (A_new[k] - t))
            pos = got * d.xy[k] + (1 - got) * (pos + frac * (d.xy[k] - pos))
            ready = got * (A_new[k] + p["digest_time"] + p["digest_per_value"] * d.val[k]) + (1 - got) * ready
        A_new = torch.stack(A_new)
        A = A_new if it == 0 else 0.5 * (A + A_new)    # damped fixed point
        in_time_pig = torch.sigmoid((p["round_time"] - A) / SIGMA_W0)
        cf = torch.sigmoid((Tc - A) / Wc)
        P = (torch.sigmoid((T - A) / W) + torch.where(never, cf - cf.detach(), torch.zeros_like(cf))) * in_time_pig
    P_hook = torch.sigmoid((p["round_time"] - T) / SIGMA_W0) * (1 - P)
    margin = (d.val * (P - P_hook)).sum()
    return dict(margin=margin, A=A, T=T, P=P, P_hook=P_hook, val=d.val, seq=seq, order=order)


def win_model(out: Dict) -> Tuple[torch.Tensor, torch.Tensor]:
    """Mean and spread of the value difference from the per-gold odds: gold k is +v (pig), -v
    (hook) or 0 (nobody), independently. P(pig wins) ~ Phi((mean + offset) / sd)."""
    v, P, Ph = out["val"], out["P"], out["P_hook"]
    mean = (v * (P - Ph)).sum()
    var = (v * v * (P + Ph) - (v * (P - Ph)) ** 2).sum()
    return mean, var.clamp_min(1.0).sqrt()


def phi(z: torch.Tensor) -> torch.Tensor:
    return 0.5 * (1 + torch.erf(z / math.sqrt(2)))


def phi_inv(p: float) -> float:
    return math.sqrt(2) * torch.erfinv(torch.tensor(2 * p - 1, dtype=torch.float64)).item()


def penalty(d: Design, spacing: float = 0.3) -> torch.Tensor:
    """Keep gold apart, inside the placement area, off the pig's start and off the hooks (squared
    distance violations)."""
    xy = d.xy
    dd = (xy[:, None] - xy[None]).norm(dim=-1) + torch.eye(len(xy), dtype=xy.dtype) * 1e3
    need = d.rad[:, None] + d.rad[None] + spacing
    over = torch.relu(need - dd).pow(2).sum() / 2
    box = (torch.relu(sim.PLACE_X[0] - xy[:, 0]) + torch.relu(xy[:, 0] - sim.PLACE_X[1])
           + torch.relu(sim.PLACE_Y[0] - xy[:, 1]) + torch.relu(xy[:, 1] - sim.PLACE_Y[1])).pow(2).sum()
    start = torch.relu(1.6 - (xy - d.pig).norm(dim=-1)).pow(2).sum()
    hooks = torch.relu(d.rad[:, None] + HOOK_CLEARANCE - (xy[:, None] - d.hooks[None]).norm(dim=-1)).pow(2).sum()
    return over + box + start + hooks


# ---------------------------------------------------------------- the game with the same policies

def play_fixed(game_map: GameMap, params: GameParams, route: List[int], seeds: List[int], device):
    """Mean margin, standard error and pig win rate in the GPU simulator: pig follows `route`,
    hooks play EfficientMiner."""
    n = len(game_map.items)
    rank = torch.full((len(seeds), n), float("inf"), device=device)
    for place, k in enumerate(route):
        rank[:, k] = float(place)
    m, w, _ = gt.play_many([game_map], [0] * len(seeds), [params], [0] * len(seeds), seeds, "order",
                           "efficient", device, order_ranks=rank)
    return m.mean().item(), m.std().item() / math.sqrt(len(seeds)), w.float().mean().item()


def play_fixed_many(jobs: List[Tuple[GameMap, GameParams, List[int]]], seeds: List[int], device) -> List[Tuple[float, float, float]]:
    """play_fixed for many (map, params, route) in one GPU batch (the simulator's cost is per time
    step, not per row, so this costs about as much as the slowest single one)."""
    if not jobs:
        return []
    E, N = len(seeds), max(len(m.items) for m, _, _ in jobs)
    rank = torch.full((len(jobs) * E, N), float("inf"), device=device)
    for i, (_, _, route) in enumerate(jobs):
        for place, k in enumerate(route):
            rank[i * E:(i + 1) * E, k] = float(place)
    idx = [i for i in range(len(jobs)) for _ in range(E)]
    m, w, _ = gt.play_many([j[0] for j in jobs], idx, [j[1] for j in jobs], idx, seeds * len(jobs),
                           "order", "efficient", device, order_ranks=rank)
    m, w = m.view(len(jobs), E).double(), w.view(len(jobs), E).double()
    return [(m[i].mean().item(), m[i].std().item() / math.sqrt(E), w[i].mean().item()) for i in range(len(jobs))]


def play_owners(game_map: GameMap, params: GameParams, route: List[int], seeds: List[int], device):
    """Per gold: how often the pig ate it, how often a hook took it, and the mean time each side
    took it (nan if never); same matches as play_fixed."""
    n = len(game_map.items)
    S = gt.new_batch([game_map], [0] * len(seeds), [params], [0] * len(seeds), seeds, device)
    S.order_rank = torch.full((len(seeds), n), float("inf"), device=device)
    for place, k in enumerate(route):
        S.order_rank[:, k] = float(place)
    gt.run(S, gt.make_policy("order", 2, 0.85, "efficient"), gt.make_miner("efficient"))
    hooked = ~S.alive & ~S.pig_ate
    pig, hook = S.pig_ate.double().mean(0).cpu(), hooked.double().mean(0).cpu()
    t = S.taken_at.double()
    nan = torch.full_like(t, float("nan"))
    t_pig = torch.where(S.pig_ate, t, nan).nanmean(0).cpu()
    t_hook = torch.where(hooked, t, nan).nanmean(0).cpu()
    return pig, hook, t_pig, t_hook


# ---------------------------------------------------------------- commands

def random_design(rng: random.Random, n_gold: int, base: GameParams) -> Tuple[GameMap, GameParams]:
    m = sim.gold_map(rng, n_gold)
    m.hooks = sim.random_hooks(rng, rng.randint(1, 4), m.pig_start)
    p = base.replace(digest_time=rng.uniform(0, 1.5), digest_per_value=rng.uniform(0, 0.01),
                     hook_extend=rng.uniform(6, 24), hook_retract=rng.uniform(8, 30))
    return m, p


def cmd_check(args, base: GameParams, device):
    """Heuristic vs the GPU simulator (same policies) on random designs."""
    rng = random.Random(args.map_seed)
    seeds = list(range(args.episodes))
    rows = []
    t0 = time.time()
    for _ in range(args.designs):
        m, p = random_design(rng, args.n_gold, base)
        d = Design(m, p, [], grad=False)
        route = plan_pig(d)
        with torch.no_grad():
            out = heuristic(d, route)
            h = out["margin"].item()
        g, se, w = play_fixed(m, p, route, seeds, device)
        pig, hook, _, _ = play_owners(m, p, route, seeds, device)
        err = (out["P"] - pig).abs()
        rows.append((len(m.hooks), h, g, se, w, err.mean().item(), (err * d.val).sum().item() / d.val.sum().item()))
    H = torch.tensor([r[1] for r in rows]); G = torch.tensor([r[2] for r in rows])
    corr = torch.corrcoef(torch.stack([H, G]))[0, 1].item()
    rmse = (H - G).pow(2).mean().sqrt().item()
    print(f"{len(rows)} random designs ({args.n_gold} gold, 1-4 hooks, random digest and hook speeds), "
          f"{len(seeds)} matches each, {time.time() - t0:.0f}s")
    print(f"correlation {corr:.3f}, rms error {rmse:.1f}, mean heuristic {H.mean().item():+.1f} vs game {G.mean().item():+.1f}")
    E = torch.tensor([r[5] for r in rows]); EV = torch.tensor([r[6] for r in rows])
    print(f"per-gold |P(pig) heuristic - game|: mean {E.mean().item():.3f}, value-weighted {EV.mean().item():.3f}")
    print(f"{'hooks':>5s} {'heuristic':>10s} {'game':>14s} {'pig win':>8s} {'gold err':>9s}")
    for r in rows[:args.show]:
        print(f"{r[0]:5d} {r[1]:+10.1f} {r[2]:+8.1f}+/-{r[3]:4.1f} {r[4]:8.1%} {r[5]:9.3f}")


def balance(game_map: GameMap, params: GameParams, args, device, rng: random.Random,
            target_win: float = None, log=print, verify=None) -> Dict:
    """Adam on the gold positions, digest and hook speeds (pig speed fixed) towards a target margin
    (target_win None) or a target pig win rate, with the heuristic corrected by the game every
    --verify-every steps. If a stage ends with the pig still too strong, a hook is added (up to
    --max-hooks). Returns the final map, params and game numbers."""
    seeds = list(range(100_000, 100_000 + args.verify))
    if verify is None:   # the game check: (map, params, route) -> (margin, standard error, pig win rate)
        verify = lambda m, p, route: play_fixed(m, p, route, seeds, device)
    lo = {n: sim.TUNABLE_PARAMS[n][0] for n in TUNED}
    hi = {n: sim.TUNABLE_PARAMS[n][1] for n in TUNED}
    by_win = target_win is not None

    def done(g_m, se, win):
        if by_win:
            return abs(win - target_win) <= args.win_tolerance
        return abs(g_m - args.target) < max(2 * se, args.tolerance)

    def pig_too_strong(g_m, win):
        return win > target_win + args.win_tolerance if by_win else g_m > args.target

    while True:
        d = Design(game_map, params, TUNED)
        # Adam steps are about lr per iteration, so give each parameter lr x its range
        opt = torch.optim.Adam([{"params": [d.xy], "lr": args.lr_pos}]
                               + [{"params": [d.p[n]], "lr": args.lr_param * (hi[n] - lo[n])} for n in TUNED])
        log(f"=== {len(game_map.hooks)} hook(s) at {', '.join(f'({x:+.1f},{y:+.1f})' for x, y in game_map.hooks)}")
        offset, best = 0.0, None
        for it in range(1, args.iters + 1):
            out = heuristic(d, plan_pig(d))
            if by_win:
                mean, sd = win_model(out)
                loss = ((phi((mean + offset) / sd) - target_win) / 0.05) ** 2
            else:
                loss = ((out["margin"] + offset - args.target) / 100.0) ** 2
            loss = loss + args.penalty * penalty(d)
            opt.zero_grad()
            loss.backward()
            opt.step()
            with torch.no_grad():
                for n in TUNED:
                    d.p[n].clamp_(lo[n], hi[n])
            if it % args.verify_every == 0 or it == args.iters:
                m_now, p_now = d.to_game()
                g_m, se, win = verify(m_now, p_now, plan_pig(d))
                with torch.no_grad():
                    h = heuristic(d, plan_pig(d))
                    if by_win:   # shift so the model's win rate here equals the game's
                        mean, sd = win_model(h)
                        w = min(max(win, WIN_CLAMP), 1 - WIN_CLAMP)
                        offset = phi_inv(w) * sd.item() - mean.item()
                    else:
                        offset = g_m - h["margin"].item()
                # ties (e.g. a win rate stuck at 100%) go to the margin nearer to even
                score = (abs(win - target_win), abs(g_m)) if by_win else (abs(g_m - args.target), 0)
                if best is None or score < best[0]:
                    best = (score, m_now, p_now, g_m, se, win)
                log(f"iter {it:4d}  heuristic {h['margin'].item():+8.1f}  game {g_m:+8.1f} (+/-{se:.1f}) "
                    f"pig win {win:5.1%}  " + "  ".join(f"{n}={d.p[n].item():.4g}" for n in TUNED))
                if done(g_m, se, win):
                    break
        _, game_map, params, g_m, se, win = best
        if done(g_m, se, win) or not pig_too_strong(g_m, win):
            break
        if len(game_map.hooks) >= args.max_hooks:
            log(f"pig still too strong with {len(game_map.hooks)} hooks (the --max-hooks limit)")
            break
        extra = sim.random_hooks(rng, len(game_map.hooks) + 1, game_map.pig_start)
        game_map.hooks = list(game_map.hooks) + [extra[-1]]
        log(f"pig still too strong (win {win:.1%}, margin {g_m:+.1f}): adding a hook at "
            f"({extra[-1][0]:+.1f},{extra[-1][1]:+.1f})")
    return dict(map=game_map, params=params, margin=g_m, se=se, win=win)


def save_design(path: str, game_map: GameMap, params: GameParams, extra: Dict = None):
    with open(path, "w", encoding="utf-8") as f:
        json.dump({**game_map.to_json(), "params": asdict(params), **(extra or {})}, f, indent=2)


def cmd_balance(args, game_map: GameMap, params: GameParams, device):
    r = balance(game_map, params, args, device, random.Random(args.map_seed * 13 + 1), args.target_win)
    game_map, params = r["map"], r["params"]
    print(f"\nfinal: {len(game_map.hooks)} hooks, game margin {r['margin']:+.1f}, pig win {r['win']:.1%}")
    print("params: " + ", ".join(f"{n}={getattr(params, n):.4g}" for n in TUNED) + f", pig_speed={params.pig_speed}")
    for it in game_map.items:
        print(f"  {it.type:12s} ({it.x:+5.2f},{it.y:+5.2f})")
    if args.out:
        save_design(args.out, game_map, params)
        print(f"wrote {args.out}")


def level_plan(args) -> List[Dict]:
    """Every level's gold count, starting hooks and target win rate."""
    out = []
    for lv in range(args.levels):
        hooks = args.hooks + (lv // args.hook_every if args.hook_every > 0 else 0)
        out.append(dict(level=lv + 1, gold=args.gold_start + lv * args.gold_step, hooks=min(hooks, args.max_hooks),
                        target=args.win_from + (args.win_to - args.win_from) * lv / max(args.levels - 1, 1)))
    return out


def level_worker(job: Dict, args_dict: Dict, base: Dict, req_q, resp_q):
    """One (level, try): balance a random map on the CPU; game checks go to the main process."""
    torch.set_num_threads(1)
    args = argparse.Namespace(**args_dict)
    rng = random.Random(args.map_seed * 1000 + job["level"] * 37 + job["try"])
    m = sim.gold_map(rng, job["gold"])
    m.hooks = sim.random_hooks(rng, job["hooks"], m.pig_start)

    def verify(m_, p_, route):
        req_q.put(("verify", job["id"], m_.to_json(), asdict(p_), route))
        return resp_q.get()

    def log(line):
        if args.verbose:
            req_q.put(("log", job["id"], line))

    t0 = time.time()
    r = balance(m, sim.GameParams(**base), args, None, rng, job["target"], log=log, verify=verify)
    req_q.put(("done", job["id"], {**r["map"].to_json(), "params": asdict(r["params"]),
                                   "result": dict(margin=r["margin"], se=r["se"], win=r["win"],
                                                  seconds=round(time.time() - t0))}))


def cmd_levels(args, base: GameParams, device):
    """A sequence of levels: one more gold per level, one more hook every --hook-every levels (and
    more whenever the pig can't be brought down to the target), target pig win rate going from
    --win-from to --win-to. Every (level, try) is balanced at once, each in its own CPU process;
    their game checks are batched into one GPU call. Each level keeps its closest try, checked on
    --final-matches fresh matches. Finished tries are saved, so a rerun resumes."""
    import os, queue
    import multiprocessing as mp
    os.makedirs(args.out_dir, exist_ok=True)
    plan = level_plan(args)
    try_path = lambda lv, tr: os.path.join(args.out_dir, f"level_{lv:02d}_try{tr}.json")
    jobs = {}
    for L in plan:
        for tr in range(1, args.tries + 1):
            if not os.path.exists(try_path(L["level"], tr)):
                jid = len(jobs)
                jobs[jid] = dict(L, id=jid, **{"try": tr})
    print(f"{len(plan)} levels x {args.tries} tries: {len(jobs)} to balance "
          f"({len(plan) * args.tries - len(jobs)} done earlier)", flush=True)
    for L in plan:
        print(f"  level {L['level']}: {L['gold']} gold, {L['hooks']}+ hooks, target pig win {L['target']:.0%}")

    ctx = mp.get_context("spawn")
    req_q = ctx.Queue()
    resp_q = {j: ctx.Queue() for j in jobs}
    seeds = list(range(100_000, 100_000 + args.verify))
    args_dict, base_dict = vars(args).copy(), asdict(base)
    procs, restarts, waiting, finished = {}, {j: 0 for j in jobs}, {}, set()

    def start(j):
        procs[j] = ctx.Process(target=level_worker, args=(jobs[j], args_dict, base_dict, req_q, resp_q[j]), daemon=True)
        procs[j].start()

    for j in jobs:
        start(j)
    t0, first_wait = time.time(), None
    while len(finished) < len(jobs):
        try:
            msg = req_q.get(timeout=0.2)
        except queue.Empty:
            msg = None
        if msg is not None:
            kind, j = msg[0], msg[1]
            job = jobs[j]
            if kind == "verify":
                waiting[j] = (GameMap.from_json(msg[2]), sim.GameParams(**msg[3]), msg[4])
                first_wait = first_wait or time.time()
            elif kind == "log":
                print(f"    [L{job['level']} t{job['try']}] {msg[2]}", flush=True)
            else:
                data = msg[2]
                save_design(try_path(job["level"], job["try"]), GameMap.from_json(data),
                            sim.GameParams(**data["params"]), {"result": data["result"]})
                finished.add(j)
                r = data["result"]
                print(f"  level {job['level']} try {job['try']}: {len(data['hooks'])} hooks, pig win {r['win']:5.1%} "
                      f"(target {job['target']:.0%}), margin {r['margin']:+7.1f}  [{r['seconds']}s; "
                      f"{len(finished)}/{len(jobs)} done, {time.time() - t0:.0f}s]", flush=True)
            continue
        # A worker that died without finishing is restarted from scratch
        for j, p in procs.items():
            if j not in finished and not p.is_alive() and j not in waiting:
                restarts[j] += 1
                print(f"  level {jobs[j]['level']} try {jobs[j]['try']}: worker died (exit {p.exitcode}), "
                      f"restart {restarts[j]}", flush=True)
                start(j)
        # Play every waiting check in one batch once all live workers wait (or 2 s after the first)
        busy = [j for j in jobs if j not in finished and j not in waiting]
        if waiting and (not busy or time.time() - first_wait > 2.0):
            ids = list(waiting)
            for j, res in zip(ids, play_fixed_many([waiting[j] for j in ids], seeds, device)):
                resp_q[j].put(res)
            waiting, first_wait = {}, None

    # Each level: the closest try, checked on fresh matches (one batch)
    best = []
    for L in plan:
        tries = []
        for tr in range(1, args.tries + 1):
            with open(try_path(L["level"], tr), encoding="utf-8") as f:
                data = json.load(f)
            tries.append((abs(data["result"]["win"] - L["target"]), len(data["hooks"]), tr, data))
        _, _, tr, data = min(tries, key=lambda x: x[:3])
        m, p = GameMap.from_json(data), sim.GameParams(**data["params"])
        best.append((L, tr, m, p, plan_pig(Design(m, p, [], grad=False))))
    fresh = list(range(900_000, 900_000 + args.final_matches))
    finals = play_fixed_many([(m, p, route) for _, _, m, p, route in best], fresh, device)
    summary = []
    for (L, tr, m, p, _), (g_m, se, win) in zip(best, finals):
        row = dict(level=L["level"], gold=L["gold"], value=m.total_value(), hooks=len(m.hooks), try_=tr,
                   target_win=round(L["target"], 4), pig_win=round(win, 4), margin=round(g_m, 1),
                   margin_se=round(se, 1), **{n: round(getattr(p, n), 4) for n in TUNED})
        summary.append(row)
        save_design(os.path.join(args.out_dir, f"level_{L['level']:02d}.json"), m, p, {"level": row})
    with open(os.path.join(args.out_dir, "levels.json"), "w", encoding="utf-8") as f:
        json.dump(summary, f, indent=2)
    print(f"\nall done in {time.time() - t0:.0f}s; on {len(fresh)} fresh matches:")
    print(f"{'lvl':>3s} {'gold':>4s} {'$':>5s} {'hooks':>5s} {'target':>7s} {'pig win':>8s} {'margin':>8s} "
          + " ".join(f"{n:>16s}" for n in TUNED))
    for r in summary:
        print(f"{r['level']:3d} {r['gold']:4d} {r['value']:5d} {r['hooks']:5d} {r['target_win']:7.0%} {r['pig_win']:8.1%} "
              f"{r['margin']:+8.1f} " + " ".join(f"{r[n]:16.4g}" for n in TUNED))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", choices=["check", "balance", "levels"])
    sim.add_arguments(ap)
    ap.add_argument("--designs", type=int, default=64, help="check: random designs")
    ap.add_argument("--show", type=int, default=12, help="check: rows to print")
    ap.add_argument("--max-hooks", type=int, default=5)
    ap.add_argument("--target", type=float, default=0.0, help="balance: target margin")
    ap.add_argument("--target-win", type=float, default=None, help="balance: target pig win rate instead")
    ap.add_argument("--win-tolerance", type=float, default=0.03)
    ap.add_argument("--levels", type=int, default=6)
    ap.add_argument("--gold-start", type=int, default=4)
    ap.add_argument("--gold-step", type=int, default=1)
    ap.add_argument("--hook-every", type=int, default=2, help="levels: one more hook every k levels (0 = only when needed)")
    ap.add_argument("--win-from", type=float, default=0.60)
    ap.add_argument("--win-to", type=float, default=0.40)
    ap.add_argument("--tries", type=int, default=3, help="levels: random maps balanced per level")
    ap.add_argument("--final-matches", type=int, default=4096)
    ap.add_argument("--out-dir", default="levels")
    ap.add_argument("--verbose", action="store_true", help="levels: print every balancing check")
    ap.add_argument("--tolerance", type=float, default=25.0, help="$: close enough to the target")
    ap.add_argument("--lr-pos", type=float, default=0.05)
    ap.add_argument("--lr-param", type=float, default=0.01)
    ap.add_argument("--penalty", type=float, default=10.0)
    ap.add_argument("--verify-every", type=int, default=10)
    ap.add_argument("--verify", type=int, default=1024, help="matches per game check")
    ap.add_argument("--device", default="cuda" if torch.cuda.is_available() else "cpu")
    ap.set_defaults(map="gold", episodes=256, iters=200, no_kill=True)
    args = ap.parse_args()
    device = torch.device(args.device)
    params = sim.resolve_params(args, None).replace(hook_kills=False)
    if args.pig_speed is None:
        params = params.replace(pig_speed=5.0)
    if args.command == "check":
        cmd_check(args, params, device)
        return
    if args.command == "levels":
        cmd_levels(args, params, device)
        return
    game_map, stored = sim.load_map(args)
    if stored:
        params = stored.replace(hook_kills=False)
    print(f"map: {len(game_map.items)} gold, ${game_map.total_value()}; pig speed {params.pig_speed}")
    cmd_balance(args, game_map, params, device)


if __name__ == "__main__":
    main()
