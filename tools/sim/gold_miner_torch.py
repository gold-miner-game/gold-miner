"""
GPU-batched version of gold_miner_sim.py (PyTorch). Same rules and agents, written as tensor
operations, so thousands of matches (maps x parameters x seeds x collection orders) step together.

What runs on the GPU:
  eval      many matches at once
  compare   every pig policy, the best fixed order (all permutations x seeds in one batch) and the
            oracle (its search tree is expanded level by level, all branches stepping in parallel)
  tune      evaluates a whole population of candidate maps / parameters per iteration
  check     plays the same maps on the CPU and the GPU simulator and compares the results

Several hooks: a map may list more than one hook origin. Every per-hook tensor is stored as
H_<field> with shape (B, H, ...); load_hook(S, h) puts hook h into the plain attributes that the
single-hook code works on, store_hook(S, h) writes them back. The hooks act one after another in a
step, all of their earnings count for the miner side, and the pig's forecasts combine all hooks.

Randomness comes from a hash of (match seed, hook, shot number), so a match's random numbers don't depend
on the batch it runs in: every branch of the oracle and every collection order of the same match
faces the same miner luck. Results are not bit-identical to the CPU simulator (different random
streams, float32), only statistically equivalent; `check` measures that.

Usage (needs torch; uses CUDA when available):
  python gold_miner_torch.py eval    --map gold --n-gold 8 --pig margin --episodes 4096 --no-kill
  python gold_miner_torch.py compare --n-gold 7 --maps 50 --seeds 20 --no-kill --best-order
  python gold_miner_torch.py tune    --map random --tune-params all --population 64 --episodes 256
  python gold_miner_torch.py check   --n-gold 6 --maps 10 --seeds 20
"""

import argparse
import dataclasses
import itertools
import json
import math
import random
import time
from dataclasses import asdict
from typing import Callable, List, Optional, Sequence, Tuple

import torch

import gold_miner_sim as sim
from gold_miner_sim import (ARENA_X, ARENA_Y, HOOK_HIT_RADIUS, HOOK_ORIGIN, ITEM_TYPES, PIG_RADIUS,
                            Evaluation, GameMap, GameParams)

Tensor = torch.Tensor
INF = float("inf")
TYPE_NAMES = list(ITEM_TYPES)
PARAM_FIELDS = [f.name for f in dataclasses.fields(GameParams)]


# ---------------------------------------------------------------- helpers

def normalize(v: Tensor) -> Tensor:
    n = v.norm(dim=-1, keepdim=True)
    return torch.where(n > 1e-9, v / n.clamp_min(1e-9), torch.zeros_like(v))


def seg_dist(p: Tensor, a: Tensor, b: Tensor) -> Tensor:
    """Distance from points p to segments a-b (all broadcast, last dim 2)."""
    ab = b - a
    denom = (ab * ab).sum(-1)
    t = ((p - a) * ab).sum(-1) / denom.clamp_min(1e-9)
    t = torch.where(denom < 1e-9, torch.zeros_like(t), t.clamp(0.0, 1.0))
    return (p - (a + ab * t[..., None])).norm(dim=-1)


def circle_cast(start: Tensor, direction: Tensor, distance: Tensor, centers: Tensor, radii: Tensor) -> Tensor:
    """Batched sim.circle_cast for a hook-sized circle. start/direction (B,2), distance (B,),
    centers (B,M,2), radii (B,M) -> (B,M) hit distance, inf where no hit."""
    r = HOOK_HIT_RADIUS + radii
    to_start = start[:, None, :] - centers
    c = (to_start * to_start).sum(-1) - r * r
    b = (to_start * direction[:, None, :]).sum(-1)
    disc = b * b - c
    t = -b - disc.clamp_min(0.0).sqrt()
    hit = (b <= 0) & (disc >= 0) & (t >= 0) & (t <= distance[:, None])
    return torch.where(c <= 0, torch.zeros_like(t), torch.where(hit, t, torch.full_like(t, INF)))


def gather_n(t: Tensor, idx: Tensor) -> Tensor:
    """t (B,N) or (B,N,2) at per-row index idx (B,); rows with idx < 0 read index 0."""
    i = idx.clamp_min(0)
    if t.dim() == 2:
        return t.gather(1, i[:, None])[:, 0]
    return t.gather(1, i[:, None, None].expand(-1, 1, t.shape[2]))[:, 0]


def uniform(seed: Tensor, counter: Tensor, stream: int) -> Tensor:
    """Counter-based random numbers in [0, 1): the same (seed, counter, stream) always gives
    the same value, whatever batch the match is in."""
    x = (seed * 0x9E3779B1 + counter * 0x85EBCA77 + stream * 0xC2B2AE3D) & 0xFFFFFFFF
    for _ in range(2):   # lowbias32 integer hash, twice
        x = x ^ (x >> 16)
        x = (x * 0x7FEB352D) & 0xFFFFFFFF
        x = x ^ (x >> 15)
        x = (x * 0x846CA68B) & 0xFFFFFFFF
        x = x ^ (x >> 16)
    return (x >> 8).float() / 16777216.0


# ---------------------------------------------------------------- batched game state

class Batch:
    """B matches, one row each. Every tensor attribute has the batch as its first dimension."""

    def take(self, idx: Tensor) -> "Batch":
        """Copy of the selected rows (also used to clone states for the oracle)."""
        new = Batch.__new__(Batch)
        for k, v in self.__dict__.items():
            new.__dict__[k] = v[idx] if isinstance(v, Tensor) and k not in Batch.SHARED else v
        return new

    @staticmethod
    def cat(parts: List["Batch"]) -> "Batch":
        new = Batch.__new__(Batch)
        for k, v in parts[0].__dict__.items():
            new.__dict__[k] = torch.cat([p.__dict__[k] for p in parts]) \
                if isinstance(v, Tensor) and k not in Batch.SHARED else v
        return new

    SHARED = {"arange_n"}

    @property
    def hook_count(self) -> int:
        return self.H_hook_state.shape[1]

    @property
    def size(self) -> int:
        return self.now.shape[0]


PAD_HOOK = (0.0, 1000.0)   # where padding hooks stand


def new_batch(maps: Sequence[GameMap], map_idx: Sequence[int], params: Sequence[GameParams],
              param_idx: Sequence[int], seeds: Sequence[int], device, dt: float = 0.02) -> Batch:
    """Row b plays maps[map_idx[b]] with params[param_idx[b]] and match seed seeds[b]."""
    S = Batch()
    S.device, S.dt = device, dt
    f32 = dict(dtype=torch.float32, device=device)
    mi = torch.as_tensor(list(map_idx), dtype=torch.long, device=device)
    pi = torch.as_tensor(list(param_idx), dtype=torch.long, device=device)
    B = mi.shape[0]
    N = max(1, max(len(m.items) for m in maps))

    # Maps: build each distinct map once, then index
    pos = torch.zeros(len(maps), N, 2, **f32)
    typ = torch.zeros(len(maps), N, dtype=torch.long, device=device)
    present = torch.zeros(len(maps), N, dtype=torch.bool, device=device)
    pig_start = torch.zeros(len(maps), 2, **f32)
    for k, m in enumerate(maps):
        for j, it in enumerate(m.items):
            pos[k, j] = torch.tensor([it.x, it.y])
            typ[k, j] = TYPE_NAMES.index(it.type)
            present[k, j] = True
        pig_start[k] = torch.tensor(m.pig_start)
    S.items_pos, S.typ, S.alive = pos[mi], typ[mi], present[mi]
    table = lambda col: torch.tensor([ITEM_TYPES[t][col] for t in TYPE_NAMES], **f32)[S.typ]
    S.val, S.rad, S.mult = table(0), table(1), table(2)
    S.is_tre = torch.tensor([t in sim.TREASURE_TYPES for t in TYPE_NAMES], device=device)[S.typ]
    S.is_rock = S.typ == TYPE_NAMES.index("Rock")
    S.is_life = S.typ == TYPE_NAMES.index("Life")
    S.has_rocks = bool((S.alive & S.is_rock).any())

    # Parameters: one tensor per GameParams field
    for name in PARAM_FIELDS:
        col = torch.tensor([float(getattr(p, name)) for p in params], **f32)[pi]
        setattr(S, "p_" + name, col > 0.5 if name == "hook_kills" else col)
    S.p_telegraph_mean = 0.5 * (S.p_telegraph_min + S.p_telegraph_max)

    S.arange_n = torch.arange(N, device=device)
    S.match_seed = torch.as_tensor(list(seeds), dtype=torch.long, device=device)

    zeros = lambda: torch.zeros(B, **f32)
    S.now = zeros()
    S.playing = torch.ones(B, dtype=torch.bool, device=device)
    S.killed = torch.zeros(B, dtype=torch.bool, device=device)
    S.had_treasure = (S.alive & S.is_tre).any(1)
    S.pig_pos = pig_start[mi].clone()
    S.pig_vel = torch.zeros(B, 2, **f32)
    S.lives = S.p_starting_lives.long()
    S.invincible_until = zeros() - 1
    S.digest_until = zeros() - 1
    S.pmoney, S.mmoney = zeros(), zeros()
    S.pig_ate = torch.zeros_like(S.alive)   # gold the pig ate (gone and not eaten = hooked)
    S.taken_at = torch.full(S.alive.shape, float("nan"), device=device)   # when each item was eaten / grabbed

    # Hooks: per-hook state is (B, H, ...); see load_hook / store_hook
    # Maps with fewer hooks are padded with idle hooks far off the map that never shoot (only the
    # "order" pig is exact with them: the planning pigs would still forecast them)
    H = max(len(m.hooks) for m in maps)
    origins = torch.tensor([list(m.hooks) + [PAD_HOOK] * (H - len(m.hooks)) for m in maps], **f32)[mi]
    real = torch.tensor([[h < len(m.hooks) for h in range(H)] for m in maps], device=device)[mi]
    hz = lambda dtype=torch.float32: torch.zeros(B, H, dtype=dtype, device=device)
    S.H_origin = origins
    S.H_seed = S.match_seed[:, None] * 7 + torch.arange(H, device=device) * 1_000_003
    S.H_shot_no = hz(torch.long)
    S.H_hook_state = hz(torch.long)   # 0 idle, 1 extending, 2 retracting
    S.H_hook_pos = origins.clone()
    S.H_hook_dir = torch.tensor([0.0, -1.0], **f32).expand(B, H, 2).clone()
    S.H_hook_max, S.H_hook_trav = hz(), hz()
    S.H_carried = hz(torch.long) - 1
    S.H_touched = torch.zeros(B, H, N, dtype=torch.bool, device=device)
    S.H_touched_pig = hz(torch.bool)
    S.H_shot_end = origins.clone()
    S.H_tel_active = hz(torch.bool)
    S.H_tel_end = origins.clone()
    S.H_tel_left = hz()
    S.H_miner_wait = torch.where(real, S.p_start_delay[:, None].expand(B, H), torch.full((B, H), INF, **f32))
    S.H_shots_left = hz(torch.long)
    S.H_claim = hz(torch.long) - 1           # gold an EfficientMiner hook is going for, until it is home
    S.H_tel_seen = hz() + float("nan")      # pig's memory of each hook's telegraph / idle time
    S.H_idle_since = hz() + float("nan")
    for h in range(H):
        load_hook(S, h)
        S.shots_left = randint_shots(S, torch.full((B,), 1 << 20, dtype=torch.long, device=device))
        store_hook(S, h)

    # Pig policy memory
    S.target = torch.full((B,), -1, dtype=torch.long, device=device)
    S.next_plan = zeros() - 1
    S.order_rank = torch.zeros(B, N, **f32)
    return S


HOOK_FIELDS = ["origin", "seed", "shot_no", "hook_state", "hook_pos", "hook_dir", "hook_max", "hook_trav",
               "carried", "touched", "touched_pig", "shot_end", "tel_active", "tel_end", "tel_left",
               "miner_wait", "shots_left", "claim", "tel_seen", "idle_since"]


def load_hook(S: Batch, h: int):
    """Make hook h the current one: S.<field> = S.H_<field>[:, h]."""
    for f in HOOK_FIELDS:
        setattr(S, f, getattr(S, "H_" + f)[:, h])


def store_hook(S: Batch, h: int):
    """Write the current hook's state back into slot h."""
    for f in HOOK_FIELDS:
        getattr(S, "H_" + f)[:, h] = getattr(S, f)


def randint_shots(S: Batch, counter: Tensor) -> Tensor:
    u = uniform(S.seed, counter, 2)
    span = S.p_max_treasure_shots - S.p_min_treasure_shots + 1
    return (S.p_min_treasure_shots + torch.floor(u * span)).long()


def margin(S: Batch) -> Tensor:
    return S.pmoney - S.mmoney


def won(S: Batch) -> Tensor:
    return ~S.killed & (S.pmoney > S.mmoney)


# ---------------------------------------------------------------- miners

class ScriptedMiner:
    """Batched sim.ScriptedMiner."""

    def wants_pig_shot(self, S: Batch, mask: Tensor) -> Tensor:
        has = (S.alive & S.is_tre).any(1)
        dec = S.p_hook_kills & (S.shots_left > 0) & has
        pig_shot = S.p_hook_kills & ~dec
        S.shots_left = torch.where(mask & dec, S.shots_left - 1,
                                   torch.where(mask & pig_shot, randint_shots(S, S.shot_no), S.shots_left))
        return pig_shot | ~has

    def choose(self, S: Batch, mask: Tensor) -> Tensor:
        at_pig = self.wants_pig_shot(S, mask)
        w = torch.where(S.alive & S.is_tre, S.val.clamp_min(1.0), torch.zeros_like(S.val))
        cum = w.cumsum(1)
        u = uniform(S.seed, S.shot_no, 1) * cum[:, -1]
        pick = (cum <= u[:, None]).sum(1).clamp(max=w.shape[1] - 1)
        return torch.where(at_pig[:, None], S.pig_pos, gather_n(S.items_pos, pick))


class SmartMiner(ScriptedMiner):
    """Batched sim.SmartMiner."""

    lead = 0.5

    def choose(self, S: Batch, mask: Tensor) -> Tensor:
        at_pig = self.wants_pig_shot(S, mask)
        d = (S.items_pos - S.origin[:, None]).norm(dim=-1)
        hook_time = S.p_telegraph_mean[:, None] + d / S.p_hook_extend[:, None] \
            + d / (S.p_hook_retract[:, None] * S.mult)
        digest_left = (S.digest_until - S.now).clamp_min(0.0)
        pig_time = torch.maximum((S.items_pos - S.pig_pos[:, None]).norm(dim=-1) / S.p_pig_speed[:, None],
                                 digest_left[:, None])
        steal = torch.where(pig_time < hook_time + 1.0, 1.5, 1.0)
        util = torch.where(pig_time < hook_time - 1.5, torch.zeros_like(d),
                           steal * S.val / (hook_time + S.p_cooldown[:, None]))
        util = torch.where(S.alive & S.is_tre, util, torch.full_like(util, -INF))
        best = gather_n(S.items_pos, util.argmax(1))
        flight = S.p_telegraph_mean + (S.pig_pos - S.origin).norm(dim=-1) / S.p_hook_extend
        lead = S.pig_pos + S.pig_vel * (flight * self.lead)[:, None]
        return torch.where(at_pig[:, None], lead, best)


class EfficientMiner(ScriptedMiner):
    """Collects as if there were no pig: each free hook takes the gold with the best value per cycle
    time from where it stands (Smith's rule), skipping gold another hook is already going for."""

    def choose(self, S: Batch, mask: Tensor) -> Tensor:
        at_pig = self.wants_pig_shot(S, mask)
        tre = S.alive & S.is_tre
        claimed = (S.H_claim[..., None] == S.arange_n).any(1)
        free = tre & ~claimed
        free = torch.where(free.any(1, keepdim=True), free, tre)
        d = (S.items_pos - S.origin[:, None]).norm(dim=-1)
        cycle = hook_cycle(S, d, S.mult)
        j = torch.where(free, S.val / cycle, torch.full_like(cycle, -INF)).argmax(1)
        S.claim = torch.where(mask & ~at_pig, j, S.claim)
        return torch.where(at_pig[:, None], S.pig_pos, gather_n(S.items_pos, j))


def make_miner(name: str):
    if name == "efficient":
        return EfficientMiner()
    return SmartMiner() if name == "smart" else ScriptedMiner()


# ---------------------------------------------------------------- pig policies

def dodge(S: Batch, margin_: float = 0.5) -> Tuple[Tensor, Tensor]:
    """Batched sim.dodge_move over every hook -> (dodging (B,), move (B,2)); the first hook whose
    line the pig is in decides the direction."""
    dodging = torch.zeros_like(S.playing)
    move = torch.zeros_like(S.pig_pos)
    for h in range(S.hook_count):
        load_hook(S, h)
        d_h, m_h = dodge_one(S, margin_)
        move = torch.where((d_h & ~dodging)[:, None], m_h, move)
        dodging = dodging | d_h
    return dodging, move


def dodge_one(S: Batch, margin_: float) -> Tuple[Tensor, Tensor]:
    """Dodge the current hook only."""
    thr = PIG_RADIUS + HOOK_HIT_RADIUS + margin_
    a = S.origin.expand_as(S.pig_pos)

    def side(b):
        line = normalize(b - a)
        s = torch.stack([-line[:, 1], line[:, 0]], dim=1)
        flip = ((S.pig_pos - a) * s).sum(-1) < 0
        return torch.where(flip[:, None], -s, s)

    in_tel = S.tel_active & (seg_dist(S.pig_pos, a, S.tel_end) < thr)
    hook_line = (S.hook_state != 0) & (S.carried < 0)
    in_hook = hook_line & (seg_dist(S.pig_pos, a, S.shot_end) < thr)
    move = torch.where(in_tel[:, None], side(S.tel_end), side(S.shot_end))
    return S.p_hook_kills & (in_tel | in_hook), move


def seek(S: Batch, target: Tensor) -> Tensor:
    ok = (target >= 0) & gather_n(S.alive, target)
    to = gather_n(S.items_pos, target) - S.pig_pos
    move = torch.where((to.norm(dim=-1) < 0.05)[:, None], torch.zeros_like(to), normalize(to))
    return torch.where(ok[:, None], move, torch.zeros_like(move))


class GreedyPolicy:
    """Batched sim.GreedyPig."""

    def act(self, S: Batch, active: Tensor) -> Tensor:
        dodging, dmove = dodge(S)
        near = torch.zeros_like(S.alive)
        for h in range(S.hook_count):
            load_hook(S, h)
            a = S.origin[:, None]
            near = near | (S.tel_active[:, None] & (seg_dist(S.items_pos, a, S.tel_end[:, None]) < S.rad + HOOK_HIT_RADIUS + 0.3))
            hook_line = (S.hook_state != 0) & (S.carried < 0)
            near = near | (hook_line[:, None] & (seg_dist(S.items_pos, a, S.shot_end[:, None]) < S.rad + HOOK_HIT_RADIUS + 0.3))
        worth = torch.where(S.is_tre, S.val, torch.where((S.lives == 0)[:, None], 150.0, 40.0))
        d = (S.items_pos - S.pig_pos[:, None]).norm(dim=-1)
        ok = S.alive & ~S.is_rock & ~near
        score = torch.where(ok, worth / (d + 0.5), torch.zeros_like(d))
        best, j = score.max(1)
        move = torch.where((best > 0)[:, None], normalize(gather_n(S.items_pos, j) - S.pig_pos),
                           torch.zeros_like(S.pig_pos))
        return torch.where(dodging[:, None], dmove, move)


class Forecast:
    """What one hook is busy with (see sim.HookForecast), plus where it stands."""

    def __init__(self, free_at: Tensor, committed: Tensor, committed_at: Tensor, origin: Tensor):
        self.free_at, self.committed, self.committed_at, self.origin = free_at, committed, committed_at, origin


def forecast_hook(S: Batch, tel_left: Tensor, idle_wait: Tensor) -> Forecast:
    """Batched sim.forecast_hook."""
    tel = S.tel_active
    ext = ~tel & (S.hook_state == 1)
    ret = ~tel & (S.hook_state == 2)
    start = torch.where(tel[:, None], S.origin.expand_as(S.hook_pos), S.hook_pos)
    end = torch.where(tel[:, None], S.tel_end, S.shot_end)
    wait = torch.where(tel, tel_left, torch.zeros_like(tel_left))
    out = torch.where(tel, torch.zeros_like(S.hook_trav), S.hook_trav)

    direction, max_len = normalize(end - start), (end - start).norm(dim=-1)
    d = circle_cast(start, direction, max_len, S.items_pos, S.rad)
    d = torch.where(S.alive & ~S.is_life, d, torch.full_like(d, INF))
    hit_d, hit = d.min(1)
    nohit = torch.isinf(hit_d)
    hit_d0 = torch.where(nohit, torch.zeros_like(hit_d), hit_d)
    hit_tre = ~nohit & gather_n(S.is_tre, hit)
    ext_s, ret_s, cd = S.p_hook_extend, S.p_hook_retract, S.p_cooldown
    free_nohit = wait + max_len / ext_s + (out + max_len) / ret_s + cd
    grab = wait + hit_d0 / ext_s
    mult = torch.where(hit_tre, gather_n(S.mult, hit), torch.ones_like(grab))
    free_hit = grab + (out + hit_d0) / (ret_s * mult) + cd
    free_along = torch.where(nohit, free_nohit, free_hit)

    carried_mult = torch.where(S.carried >= 0, gather_n(S.mult, S.carried), torch.ones_like(grab))
    free_ret = (S.hook_pos - S.origin).norm(dim=-1) / (ret_s * carried_mult) + cd

    along = tel | ext
    free = torch.where(along, free_along, torch.where(ret, free_ret, idle_wait))
    committed = torch.where(along & hit_tre, hit, torch.full_like(hit, -1))
    committed_at = torch.where(along & hit_tre, grab, torch.full_like(grab, INF))
    return Forecast(free, committed, committed_at, S.origin)


def hook_cycle(S: Batch, d: Tensor, mult: Tensor) -> Tensor:
    """Batched sim.hook_cycle; d and mult are (B,N) or (B,)."""
    expand = (lambda x: x[:, None]) if d.dim() == 2 else (lambda x: x)
    return expand(S.p_telegraph_mean) + d / expand(S.p_hook_extend) \
        + d / (expand(S.p_hook_retract) * mult) + expand(S.p_cooldown)


def eat_times(S: Batch, pos: Tensor, t0: Tensor, ready: Tensor) -> Tuple[Tensor, Tensor, Tensor]:
    """Batched sim.eat_time from many start points. pos (B,K,2), t0/ready (B,K) ->
    eat time (B,K,N), stop point (B,K,N,2), distance (B,K,N)."""
    to = S.items_pos[:, None, :, :] - pos[:, :, None, :]
    d = to.norm(dim=-1)
    walk = (d - PIG_RADIUS - S.rad[:, None, :]).clamp_min(0.0)
    eat = torch.maximum(t0[:, :, None] + walk / S.p_pig_speed[:, None, None], ready[:, :, None])
    stop = pos[:, :, None, :] + normalize(to) * walk[..., None]
    return eat, stop, d


class PlannerPolicy:
    """Batched sim.PlannerPig: tracks the telegraph timer and the hook's idle time, dodges,
    replans a target every `replan_every` seconds (or when it disappears) and walks to it."""

    replan_every = 0.1

    def act(self, S: Batch, active: Tensor) -> Tensor:
        timers = []
        for h in range(S.hook_count):
            load_hook(S, h)
            timers.append(self.track(S, active))
            store_hook(S, h)

        dodging, dmove = dodge(S)
        target_ok = (S.target >= 0) & gather_n(S.alive, S.target)
        need = active & ~dodging & (~target_ok | (S.now >= S.next_plan))
        if bool(need.any()):
            fcs = []
            for h, (tel_left, idle_wait) in enumerate(timers):
                load_hook(S, h)
                fcs.append(forecast_hook(S, tel_left, idle_wait))
            S.target = torch.where(need, self.choose(S, fcs), S.target)
            S.next_plan = torch.where(need, S.now + self.replan_every, S.next_plan)
        return torch.where(dodging[:, None], dmove, seek(S, S.target))

    def track(self, S: Batch, active: Tensor) -> Tuple[Tensor, Tensor]:
        """For the current hook: expected time until its telegraph fires, and its remaining
        start delay / cooldown while idle (what the player can see)."""
        p_min, p_max, p_mean = S.p_telegraph_min, S.p_telegraph_max, S.p_telegraph_mean
        nan = torch.full_like(S.now, float("nan"))

        # Expected time until the shown telegraph fires
        seen = torch.where(S.tel_active, torch.where(torch.isnan(S.tel_seen), S.now, S.tel_seen), nan)
        S.tel_seen = torch.where(active, seen, S.tel_seen)
        elapsed = S.now - seen
        tel_left = torch.where(elapsed < p_min, p_mean - elapsed, (0.5 * (p_max - elapsed)).clamp_min(0.0))
        tel_left = torch.where(S.tel_active, tel_left, torch.zeros_like(tel_left))

        # Remaining start delay / cooldown while the hook is idle
        idle = (S.hook_state == 0) & ~S.tel_active
        pre = S.now < S.p_start_delay
        since = torch.where(idle & ~pre, torch.where(torch.isnan(S.idle_since), S.now, S.idle_since),
                            torch.where(idle, S.idle_since, nan))
        S.idle_since = torch.where(active, since, S.idle_since)
        idle_wait = torch.where(pre, S.p_start_delay - S.now, (S.p_cooldown - (S.now - since)).clamp_min(0.0))
        idle_wait = torch.where(idle, idle_wait, torch.zeros_like(idle_wait))
        return tel_left, idle_wait

    def choose(self, S: Batch, fcs: List[Forecast]) -> Tensor:
        raise NotImplementedError

    @staticmethod
    def nearest(S: Batch) -> Tensor:
        tre = S.alive & S.is_tre
        d = (S.items_pos - S.pig_pos[:, None]).norm(dim=-1)
        j = torch.where(tre, d, torch.full_like(d, INF)).argmin(1)
        return torch.where(tre.any(1), j, torch.full_like(j, -1))


class RacePolicy(PlannerPolicy):
    """Batched sim.RaceGreedyPig."""

    def choose(self, S, fcs):
        tre = S.alive & S.is_tre
        digest_left = (S.digest_until - S.now).clamp_min(0.0)
        eat, _, _ = eat_times(S, S.pig_pos[:, None], torch.zeros_like(S.now)[:, None], digest_left[:, None])
        eat = eat[:, 0]
        eta = torch.full_like(eat, INF)   # the earliest any hook could grab each gold
        for fc in fcs:
            is_com = S.arange_n == fc.committed[:, None]
            eta = torch.minimum(eta, torch.where(
                is_com, fc.committed_at[:, None],
                (fc.free_at + S.p_telegraph_mean)[:, None]
                + (S.items_pos - fc.origin[:, None]).norm(dim=-1) / S.p_hook_extend[:, None]))
        ok = tre & (eat < eta)
        vmax = torch.where(ok, S.val, torch.full_like(S.val, -1.0)).max(1).values
        pick = torch.where(ok & (S.val == vmax[:, None]), eat, torch.full_like(eat, INF)).argmin(1)
        return torch.where(ok.any(1), pick, self.nearest(S))


class MarginPolicy(PlannerPolicy):
    """Batched sim.MarginGreedyPig (any depth; memory grows as N^depth)."""

    min_survival = 0.05

    def __init__(self, depth: int = 2, gamma: float = 0.85, miner_model: str = "weighted"):
        self.depth, self.gamma, self.miner_model = depth, gamma, miner_model

    def survival(self, S: Batch, fc: Forecast) -> Callable[[Tensor], Tensor]:
        """Batched sim.survival_model: returns f(t) with t (B,K,N) -> P(item still there at t)."""
        tre = S.alive & S.is_tre
        is_com = (S.arange_n == fc.committed[:, None])[:, None, :]
        com_at = fc.committed_at[:, None, None]
        d0 = (S.items_pos - fc.origin[:, None]).norm(dim=-1)
        cycle = hook_cycle(S, d0, S.mult)

        if self.miner_model == "greedy":
            threat = torch.where(is_com[:, 0], com_at[:, 0], torch.full_like(d0, INF))
            left = tre & ~is_com[:, 0]
            t = fc.free_at.clone()
            running = torch.ones_like(S.playing)
            mean_shots = 0.5 * (S.p_min_treasure_shots + S.p_max_treasure_shots)
            pig_cycle = hook_cycle(S, (S.pig_pos - fc.origin).norm(dim=-1) + S.p_overshoot, torch.ones_like(t))
            debt = torch.zeros_like(t)
            time_left = S.p_round_time - S.now
            for _ in range(d0.shape[1]):
                rate = torch.where(left, S.val / cycle, torch.full_like(cycle, -INF))
                nxt = rate.argmax(1)
                grab = t + S.p_telegraph_mean + gather_n(d0, nxt) / S.p_hook_extend
                running = running & left.any(1) & (grab <= time_left)
                hit = running[:, None] & (S.arange_n == nxt[:, None])
                threat = torch.where(hit, grab[:, None], threat)
                left = left & ~hit
                t = torch.where(running, t + gather_n(cycle, nxt), t)
                debt = torch.where(running & S.p_hook_kills, debt + 1.0 / mean_shots, debt)
                extra = debt >= 1.0
                debt = torch.where(extra, debt - 1.0, debt)
                t = torch.where(extra, t + pig_cycle, t)
            return lambda tt: (tt < threat[:, None, :]).float()

        left = tre & ~is_com[:, 0]
        total = (S.val * left).sum(1)
        total = torch.where(total > 0, total, torch.ones_like(total))
        avg_cycle = (S.val / total[:, None] * cycle * left).sum(1)
        mean_shots = 0.5 * (S.p_min_treasure_shots + S.p_max_treasure_shots)
        pig_cycle = hook_cycle(S, (S.pig_pos - fc.origin).norm(dim=-1) + S.p_overshoot, torch.ones_like(total))
        avg_cycle = torch.where(S.p_hook_kills, avg_cycle + pig_cycle / mean_shots, avg_cycle)
        aim_share = torch.where(S.p_hook_kills, mean_shots / (mean_shots + 1), torch.ones_like(total))
        first_grab = (fc.free_at + S.p_telegraph_mean)[:, None] + d0 / S.p_hook_extend[:, None]
        keep = (1.0 - aim_share[:, None] * S.val / total[:, None])[:, None, :]
        first_grab, avg = first_grab[:, None, :], avg_cycle[:, None, None]

        def f(tt):
            shots = torch.floor((tt - first_grab) / avg.clamp_min(1e-9)) + 1
            p = torch.where((tt < first_grab) | (avg <= 0), torch.ones_like(tt), keep ** shots)
            return torch.where(is_com, (tt < com_at).float(), p)
        return f

    def choose(self, S, fcs):
        B, N = S.alive.shape
        tre = S.alive & S.is_tre
        per_hook = [self.survival(S, fc) for fc in fcs]

        def survival(tt):   # hooks act independently: the gold survives all of them
            out = per_hook[0](tt)
            for f in per_hook[1:]:
                out = out * f(tt)
            return out
        time_left = S.p_round_time - S.now
        contested = 1.0 - survival(time_left[:, None, None].expand(B, 1, N))[:, 0]
        digest_left = (S.digest_until - S.now).clamp_min(0.0)

        pos = S.pig_pos[:, None, :]
        t = torch.zeros(B, 1, device=S.device)
        ready = digest_left[:, None]
        used = ~tre[:, None, :]
        score = torch.zeros(B, 1, device=S.device)
        first = torch.full((B, 1), -1, dtype=torch.long, device=S.device)
        valid = torch.ones(B, 1, dtype=torch.bool, device=S.device)
        best = torch.zeros(B, device=S.device)
        best_first = torch.full((B,), -1, dtype=torch.long, device=S.device)

        for level in range(self.depth + 1):
            if level < self.depth:
                eat, stop, _ = eat_times(S, pos, t, ready)
                alive_p = survival(eat)
                feas = valid[:, :, None] & ~used & (alive_p >= self.min_survival) & (eat <= time_left[:, None, None])
                extended = feas.any(-1)
            else:
                extended = torch.zeros_like(valid)
            # Sequences that can't be extended are complete plans: keep the best one's first step
            leaf = valid & ~extended & (first >= 0)
            m, k = torch.where(leaf, score, torch.full_like(score, -INF)).max(1)
            better = m > best
            best = torch.where(better, m, best)
            best_first = torch.where(better, first.gather(1, k[:, None])[:, 0], best_first)
            if level == self.depth or not bool(feas.any()):
                break
            K = valid.shape[1]
            gain = alive_p * S.val[:, None, :] * (1.0 + contested[:, None, :])
            score = (score[:, :, None] + gain * self.gamma ** eat).reshape(B, K * N)
            valid = feas.reshape(B, K * N)
            t = eat.reshape(B, K * N)
            ready = (eat + (S.p_digest_time[:, None] + S.p_digest_per_value[:, None] * S.val)[:, None, :]).reshape(B, K * N)
            pos = stop.reshape(B, K * N, 2)
            eye = torch.eye(N, dtype=torch.bool, device=S.device)
            used = (used[:, :, None, :] | eye[None, None]).reshape(B, K * N, N)
            first = torch.where(first[:, :, None] < 0, S.arange_n[None, None, :], first[:, :, None]).reshape(B, K * N)

        return torch.where(best_first >= 0, best_first, self.nearest(S))


class OrderPolicy(PlannerPolicy):
    """Batched sim.OrderPig: S.order_rank (B,N) gives each item's place in the order."""

    def choose(self, S, fcs):
        rank = torch.where(S.alive & S.is_tre, S.order_rank, torch.full_like(S.order_rank, INF))
        best, j = rank.min(1)
        return torch.where(torch.isfinite(best), j, torch.full_like(j, -1))


class FollowPolicy:
    """Batched sim.FollowPig: walk to S.target, set by the oracle."""

    def act(self, S: Batch, active: Tensor) -> Tensor:
        dodging, dmove = dodge(S)
        return torch.where(dodging[:, None], dmove, seek(S, S.target))


MINER_MODELS = sim.MINER_MODELS


def make_policy(name: str, depth: int = 2, gamma: float = 0.85, miner_name: str = "scripted"):
    if name == "race":
        return RacePolicy()
    if name == "margin":
        return MarginPolicy(depth, gamma, MINER_MODELS[miner_name])
    if name == "order":
        return OrderPolicy()
    return GreedyPolicy()


# ---------------------------------------------------------------- simulation step

def step(S: Batch, policy, miner, active: Tensor):
    """One fixed step for the rows in `active`: pig, miner brain, hook, round rules."""
    move_pig(S, policy.act(S, active), active)
    for h in range(S.hook_count):
        load_hook(S, h)
        update_miner(S, miner, active)
        update_hook(S, active)
        store_hook(S, h)
    S.now = torch.where(active, S.now + S.dt, S.now)
    tre_left = (S.alive & S.is_tre).any(1)
    end = active & S.playing & ((S.now >= S.p_round_time - 1e-4) | (S.had_treasure & ~tre_left))
    S.playing = S.playing & ~end


def move_pig(S: Batch, move: Tensor, active: Tensor):
    n = move.norm(dim=-1, keepdim=True)
    move = torch.where(n > 1, move / n.clamp_min(1e-9), move)
    vel = move * S.p_pig_speed[:, None]
    p = S.pig_pos + vel * S.dt

    # Rocks are solid: push the pig back out
    if S.has_rocks:
        up = torch.tensor([0.0, 1.0], device=S.device)
        for j in range(S.items_pos.shape[1]):
            c = S.items_pos[:, j]
            r = PIG_RADIUS + S.rad[:, j]
            off = p - c
            d = off.norm(dim=-1)
            push = S.alive[:, j] & S.is_rock[:, j] & (d < r)
            dirn = torch.where((d > 1e-6)[:, None], off / d.clamp_min(1e-9)[:, None], up)
            p = torch.where(push[:, None], c + dirn * r[:, None], p)
    p = torch.stack([p[:, 0].clamp(-ARENA_X + PIG_RADIUS, ARENA_X - PIG_RADIUS),
                     p[:, 1].clamp(-ARENA_Y + PIG_RADIUS, ARENA_Y - PIG_RADIUS)], dim=1)
    S.pig_pos = torch.where(active[:, None], p, S.pig_pos)
    S.pig_vel = torch.where(active[:, None], vel, S.pig_vel)

    # Touching treasure / Life pickups collects them; gold only when not digesting
    d = (S.items_pos - S.pig_pos[:, None]).norm(dim=-1)
    touch = active[:, None] & S.alive & (d < PIG_RADIUS + S.rad) & ~S.is_rock
    life = touch & S.is_life
    S.lives = S.lives + life.sum(1)
    gold = touch & S.is_tre & (S.now >= S.digest_until)[:, None]
    first_gold = gold & (gold.long().cumsum(1) == 1)   # with a digest time only one per step
    eat = torch.where(((S.p_digest_time > 0) | (S.p_digest_per_value > 0))[:, None], first_gold, gold)
    eaten = (S.val * eat).sum(1)
    S.pmoney = S.pmoney + eaten
    S.digest_until = torch.where(eat.any(1), S.now + S.p_digest_time + S.p_digest_per_value * eaten,
                                 S.digest_until)
    S.alive = S.alive & ~life & ~eat
    S.pig_ate = S.pig_ate | eat
    S.taken_at = torch.where(eat, S.now[:, None], S.taken_at)


def update_miner(S: Batch, miner, active: Tensor):
    tel = active & S.tel_active
    S.tel_left = torch.where(tel, S.tel_left - S.dt, S.tel_left)
    fire = tel & (S.tel_left <= 0)
    if bool(fire.any()):
        direction = normalize(S.tel_end - S.origin)
        S.hook_state = torch.where(fire, 1, S.hook_state)
        S.hook_pos = torch.where(fire[:, None], S.origin.expand_as(S.hook_pos), S.hook_pos)
        S.hook_dir = torch.where(fire[:, None], direction, S.hook_dir)
        S.hook_max = torch.where(fire, (S.tel_end - S.origin).norm(dim=-1), S.hook_max)
        S.hook_trav = torch.where(fire, torch.zeros_like(S.hook_trav), S.hook_trav)
        S.carried = torch.where(fire, -1, S.carried)
        S.touched = S.touched & ~fire[:, None]
        S.touched_pig = S.touched_pig & ~fire
        S.shot_end = torch.where(fire[:, None], S.tel_end, S.shot_end)
        S.tel_active = S.tel_active & ~fire

    idle = active & ~S.tel_active & (S.hook_state == 0)
    S.miner_wait = torch.where(idle, S.miner_wait - S.dt, S.miner_wait)
    choose = idle & (S.miner_wait <= 0)
    if bool(choose.any()):
        aim = miner.choose(S, choose)
        to = aim - S.origin
        direction = normalize(to)
        direction = torch.where((direction.norm(dim=-1) == 0)[:, None],
                                torch.tensor([0.0, -1.0], device=S.device), direction)
        end = S.origin + direction * (to.norm(dim=-1) + S.p_overshoot)[:, None]
        u = uniform(S.seed, S.shot_no, 0)
        S.tel_left = torch.where(choose, S.p_telegraph_min + u * (S.p_telegraph_max - S.p_telegraph_min), S.tel_left)
        S.tel_end = torch.where(choose[:, None], end, S.tel_end)
        S.tel_active = S.tel_active | choose
        S.shot_no = S.shot_no + choose.long()


def player_hit(S: Batch, mask: Tensor):
    m = mask & S.playing & ~(S.now < S.invincible_until)
    save = m & (S.lives > 0)
    S.lives = S.lives - save.long()
    S.invincible_until = torch.where(save, S.now + S.p_invincible_time, S.invincible_until)
    die = m & ~save
    S.playing = S.playing & ~die
    S.killed = S.killed | die


def grab(S: Batch, mask: Tensor, j: Tensor):
    """The hook takes item j (treasure: carried and paid; rock: shatters)."""
    onehot = mask[:, None] & (S.arange_n == j[:, None])
    S.touched = S.touched | onehot
    S.alive = S.alive & ~onehot
    S.taken_at = torch.where(onehot, S.now[:, None], S.taken_at)
    tre = mask & gather_n(S.is_tre, j)
    S.carried = torch.where(tre, j, S.carried)
    S.mmoney = S.mmoney + torch.where(tre & S.playing, gather_n(S.val, j), torch.zeros_like(S.mmoney))


def update_hook(S: Batch, active: Tensor):
    ext = active & (S.hook_state == 1)
    ret = active & (S.hook_state == 2)   # decided before extending, like the if/elif on the CPU
    if bool(ext.any()):
        extend_hook(S, ext)
    if bool(ret.any()):
        retract_hook(S, ret)


def hook_hits(S: Batch, start: Tensor, direction: Tensor, distance: Tensor, mask: Tensor):
    """Distances to items and to the pig the hook can still react to, inf where no hit."""
    d = circle_cast(start, direction, distance, S.items_pos, S.rad)
    d = torch.where(mask[:, None] & S.alive & ~S.is_life & ~S.touched, d, torch.full_like(d, INF))
    dp = circle_cast(start, direction, distance, S.pig_pos[:, None], torch.full_like(S.now, PIG_RADIUS)[:, None])[:, 0]
    dp = torch.where(mask & S.p_hook_kills & ~S.touched_pig, dp, torch.full_like(dp, INF))
    return d, dp


def extend_hook(S: Batch, m: Tensor):
    step_len = torch.minimum(S.p_hook_extend * S.dt, S.hook_max - S.hook_trav)
    d, dp = hook_hits(S, S.hook_pos, S.hook_dir, step_len, m)
    d_item, j = d.min(1)
    hit_pig = m & (dp < d_item)
    hit_item = m & ~hit_pig & torch.isfinite(d_item)
    stop = hit_pig | hit_item
    d_stop = torch.where(hit_pig, dp, d_item)
    moved = torch.where(stop, d_stop, step_len)
    S.hook_pos = torch.where(m[:, None], S.hook_pos + S.hook_dir * moved[:, None], S.hook_pos)
    S.hook_trav = torch.where(m & ~stop, S.hook_trav + step_len, S.hook_trav)
    done = m & (stop | (S.hook_trav >= S.hook_max - 0.001))
    S.hook_state = torch.where(done, 2, S.hook_state)
    S.touched_pig = S.touched_pig | hit_pig
    player_hit(S, hit_pig)
    grab(S, hit_item, j)


def retract_hook(S: Batch, m: Tensor):
    to_origin = S.origin - S.hook_pos
    remaining = to_origin.norm(dim=-1)
    mult = torch.where(S.carried >= 0, gather_n(S.mult, S.carried), torch.ones_like(remaining))
    step_len = torch.minimum(S.p_hook_retract * mult * S.dt, remaining)

    # An empty hook still reacts on the way back: rocks shatter, the pig is hit, first gold is taken
    react = m & S.playing & (S.carried < 0) & (remaining > 1e-4)
    if bool(react.any()):
        d, dp = hook_hits(S, S.hook_pos, normalize(to_origin), step_len, react)
        d_gold, j = torch.where(S.is_tre, d, torch.full_like(d, INF)).min(1)
        rocks = torch.isfinite(d) & S.is_rock & (d < d_gold[:, None])
        S.touched = S.touched | rocks
        S.alive = S.alive & ~rocks
        hit_pig = torch.isfinite(dp) & (dp < d_gold)
        S.touched_pig = S.touched_pig | hit_pig
        player_hit(S, hit_pig)
        grab(S, react & torch.isfinite(d_gold), j)

    new_pos = S.hook_pos + normalize(to_origin) * step_len[:, None]
    home = m & ((new_pos - S.origin).norm(dim=-1) < 1e-3)
    S.hook_pos = torch.where(m[:, None], torch.where(home[:, None], S.origin.expand_as(new_pos), new_pos), S.hook_pos)
    S.hook_state = torch.where(home, 0, S.hook_state)
    S.carried = torch.where(home, -1, S.carried)
    S.miner_wait = torch.where(home, S.p_cooldown, S.miner_wait)
    S.claim = torch.where(home, -1, S.claim)


def run(S: Batch, policy, miner, keep_going: Optional[Callable[[Batch], Tensor]] = None, check_every: int = 10):
    """Step every row until it is over (or `keep_going` turns false for it)."""
    while True:
        for _ in range(check_every):
            active = S.playing if keep_going is None else S.playing & keep_going(S)
            step(S, policy, miner, active)
        active = S.playing if keep_going is None else S.playing & keep_going(S)
        if not bool(active.any()):
            return S


# ---------------------------------------------------------------- experiments

def play_many(maps, map_idx, params, param_idx, seeds, pig: str, miner_name: str, device,
              depth: int = 2, gamma: float = 0.85, chunk: int = 200_000, order_ranks: Optional[Tensor] = None):
    """Play all rows; returns (margin, won, killed) tensors. Rows are processed in chunks."""
    out_m, out_w, out_k = [], [], []
    n = len(seeds)
    for lo in range(0, n, chunk):
        hi = min(n, lo + chunk)
        S = new_batch(maps, map_idx[lo:hi], params, param_idx[lo:hi], seeds[lo:hi], device)
        if order_ranks is not None:
            S.order_rank = order_ranks[lo:hi]
        run(S, make_policy(pig, depth, gamma, miner_name), make_miner(miner_name))
        out_m.append(margin(S)); out_w.append(won(S)); out_k.append(S.killed)
    return torch.cat(out_m), torch.cat(out_w), torch.cat(out_k)


def evaluate_many(maps: List[GameMap], params: List[GameParams], seeds: List[int], pig: str, miner_name: str,
                  device, depth: int = 2, gamma: float = 0.85) -> List[Evaluation]:
    """sim.evaluate for each (maps[k], params[k]) on the same match seeds, in one batch."""
    K, E = len(maps), len(seeds)
    idx = [k for k in range(K) for _ in range(E)]
    m, w, k = play_many(maps, idx, params, idx, seeds * K, pig, miner_name, device, depth, gamma)
    m, w, k = m.view(K, E), w.view(K, E).float(), k.view(K, E).float()
    return [Evaluation(w[i].mean().item(), k[i].mean().item(), m[i].mean().item(), E) for i in range(K)]


def best_fixed_order(maps: List[GameMap], params: GameParams, seeds: List[int], miner_name: str, device,
                     chunk: int = 200_000) -> List[float]:
    """sim.best_fixed_order for every map: all permutations x seeds run as one batch."""
    rows_map, rows_rank, owners = [], [], []
    N = max(len(m.items) for m in maps)
    for k, m in enumerate(maps):
        gold = [j for j, it in enumerate(m.items) if it.is_treasure]
        for order in itertools.permutations(gold):
            rank = [INF] * N
            for place, j in enumerate(order):
                rank[j] = float(place)
            owners.append(k)
            rows_rank.append(rank)
    P = len(owners)
    E = len(seeds)
    map_idx = [o for o in owners for _ in range(E)]
    ranks = torch.tensor(rows_rank, device=device).repeat_interleave(E, dim=0)
    res, _, _ = play_many(maps, map_idx, [params], [0] * len(map_idx), seeds * P, "order", miner_name,
                          device, chunk=chunk, order_ranks=ranks)
    per_order = res.view(P, E).mean(1)
    owner_t = torch.tensor(owners, device=device)
    best = torch.full((len(maps),), -INF, device=device).scatter_reduce(0, owner_t, per_order, "amax")
    return best.tolist()


def oracle_many(maps: List[GameMap], params: GameParams, seeds: List[int], miner_name: str, device,
                chunk: int = 400_000) -> Tuple[List[float], int]:
    """sim.oracle for every (maps[k], seeds[k]) match: the search tree is expanded one level at a
    time and every branch of every match steps in parallel. Same random numbers in every branch
    of a match. Returns (best margin per match, nodes expanded)."""
    M = len(maps)
    miner, follow = make_miner(miner_name), FollowPolicy()
    root = new_batch(maps, range(M), [params], [0] * M, seeds, device)
    root.match = torch.arange(M, device=device)
    best = torch.full((M,), -INF, device=device)
    frontier, nodes = [root], 0

    def keep_going(S):
        return (S.target >= 0) & gather_n(S.alive, S.target)

    while frontier:
        nxt = []
        for S in frontier:
            # Finished games are leaves
            done = ~S.playing
            best.scatter_reduce_(0, S.match[done], margin(S)[done], "amax")
            gold = S.alive & S.is_tre
            remaining = (S.val * gold).sum(1)
            open_ = S.playing & (S.pmoney + remaining - S.mmoney > best[S.match])  # bound: else can't win
            # Playing with no gold left can't happen (the round ends), but finish such rows anyway
            stuck = open_ & ~gold.any(1)
            if bool(stuck.any()):
                T = S.take(stuck.nonzero()[:, 0])
                T.target.fill_(-1)
                run(T, follow, miner)
                best.scatter_reduce_(0, T.match, margin(T), "amax")
            rows, cols = (gold & (open_ & ~stuck)[:, None]).nonzero(as_tuple=True)
            for lo in range(0, rows.shape[0], chunk):
                C = S.take(rows[lo:lo + chunk])
                C.target = cols[lo:lo + chunk].clone()
                nodes += C.size
                run(C, follow, miner, keep_going)
                nxt.append(C)
        frontier = nxt
    return best.tolist(), nodes


def compare(args, params: GameParams, device):
    """GPU version of sim.compare."""
    pigs = [p.strip() for p in args.pigs.split(",")]
    maps = [sim.gold_map(random.Random(args.map_seed * 7919 + m), args.n_gold) for m in range(args.maps)]
    seeds = [args.seed * 100003 + s for s in range(args.seeds)]
    M, E = len(maps), len(seeds)
    match_map = [m for m in range(M) for _ in range(E)]
    match_seed = seeds * M
    gold = torch.tensor([maps[m].total_value() for m in match_map], device=device, dtype=torch.float32)

    print(f"{M} maps x {E} seeds, {args.n_gold} gold, miner={args.miner}, device={device}")
    print(f"params: {params}")
    results = {}
    for pig in pigs:
        t0 = time.time()
        m, w, _ = play_many(maps, match_map, [params], [0] * len(match_map), match_seed, pig, args.miner,
                            device, args.lookahead, args.gamma)
        results[pig] = (m, w.float())
        print(f"  {pig:8s} {len(match_map)} matches in {time.time() - t0:.1f}s", flush=True)
    oracle_t = None
    if args.n_gold <= args.oracle_max_gold:
        t0 = time.time()
        o, nodes = oracle_many([maps[m] for m in match_map], params, match_seed, args.miner, device)
        oracle_t = torch.tensor(o, device=device)
        print(f"  oracle   {nodes} search nodes in {time.time() - t0:.1f}s", flush=True)
    fixed = None
    if args.best_order:
        t0 = time.time()
        fixed = torch.tensor(best_fixed_order(maps, params, seeds, args.miner, device), device=device)
        print(f"  best order in {time.time() - t0:.1f}s", flush=True)

    header = f"\n{'policy':10s} {'margin':>8s} {'win':>6s}"
    if oracle_t is not None:
        header += f" {'gap':>8s} {'gap/gold':>9s} {'=oracle':>8s}"
    print(header)
    for pig, (m, w) in results.items():
        line = f"{pig:10s} {m.mean().item():+8.1f} {w.mean().item():6.1%}"
        if oracle_t is not None:
            gap = oracle_t - m
            line += f" {gap.mean().item():8.1f} {(gap / gold).mean().item():9.1%} {(gap <= 0).float().mean().item():8.1%}"
        print(line)
    if oracle_t is not None:
        print(f"{'oracle':10s} {oracle_t.mean().item():+8.1f} {(oracle_t > 0).float().mean().item():6.1%}"
              f"   (knows the future)")
    if fixed is not None:
        print(f"{'best order':10s} {fixed.mean().item():+8.1f}           (best fixed order per map, averaged over its seeds)")

    if args.csv:
        import csv
        with open(args.csv, "w", newline="", encoding="utf-8") as f:
            w = csv.writer(f)
            cols = ["map", "seed", "gold"] + pigs + (["oracle"] if oracle_t is not None else []) \
                + (["best_order"] if fixed is not None else [])
            w.writerow(cols)
            for r in range(len(match_map)):
                row = [match_map[r], match_seed[r], int(gold[r].item())] + [int(results[p][0][r].item()) for p in pigs]
                if oracle_t is not None:
                    row.append(int(oracle_t[r].item()))
                if fixed is not None:
                    row.append(round(fixed[match_map[r]].item(), 1))
                w.writerow(row)
        print(f"wrote {args.csv}")


def tune(start: GameMap, params: GameParams, args, tune_params: List[str], allow_rocks: bool, device,
         tolerance: float = 0.05):
    """Population version of sim.tune: each iteration proposes `population` changes to the current
    best, evaluates them all in one batch on the same match seeds, and keeps the best if it improves."""
    rng = random.Random(args.seed)
    seeds = [args.seed * 100003 + e for e in range(args.episodes)]
    ev = lambda ms, ps: evaluate_many(ms, ps, seeds, args.pig, args.miner, device, args.lookahead, args.gamma)
    key = lambda e: (e.imbalance, abs(e.mean_margin))
    best, best_params = start, params
    best_eval = ev([best], [best_params])[0]
    print(f"start   {best_eval}")
    for it in range(1, args.iters + 1):
        if best_eval.imbalance <= tolerance:
            print(f"balanced within +/-{tolerance:.0%}, stopping")
            break
        help_player = best_eval.win_rate < 0.5
        cands = [sim.propose(best, best_params, help_player, rng, tune_params, allow_rocks)
                 for _ in range(args.population)]
        evals = ev([c[0] for c in cands], [c[1] for c in cands])
        i = min(range(len(evals)), key=lambda k: key(evals[k]))
        keep = key(evals[i]) < key(best_eval)
        if keep:
            (best, best_params), best_eval = cands[i], evals[i]
        print(f"iter {it:3d} {'help player' if help_player else 'help miner '} "
              f"{'KEEP' if keep else 'skip'}  best of {len(cands)}: {evals[i]}", flush=True)
    print(f"final   {best_eval}")
    return best, best_params


def check(args, params: GameParams, device):
    """Same maps on the CPU and GPU simulators: mean margins should agree within noise."""
    maps = [sim.gold_map(random.Random(args.map_seed * 7919 + m), args.n_gold) for m in range(args.maps)]
    seeds = [args.seed * 100003 + s for s in range(args.seeds)]
    match_map = [m for m in range(len(maps)) for _ in seeds]
    print(f"{len(match_map)} matches per policy, params: {params}")
    print(f"{'policy':8s} {'cpu margin':>16s} {'gpu margin':>16s} {'cpu win':>8s} {'gpu win':>8s} {'cpu s':>6s} {'gpu s':>6s}")
    for pig in [p.strip() for p in args.pigs.split(",")]:
        t0 = time.time()
        cpu = [sim.play(maps[m], params, pig, args.miner, s, args.lookahead, args.gamma)
               for m in range(len(maps)) for s in seeds]
        t_cpu = time.time() - t0
        t0 = time.time()
        gm, gw, _ = play_many(maps, match_map, [params], [0] * len(match_map), seeds * len(maps), pig,
                              args.miner, device, args.lookahead, args.gamma)
        t_gpu = time.time() - t0
        cm = torch.tensor([r.margin for r in cpu], dtype=torch.float32)
        cw = sum(r.player_won for r in cpu) / len(cpu)
        se = lambda x: x.std().item() / math.sqrt(len(x))
        gm = gm.cpu()
        print(f"{pig:8s} {cm.mean().item():+9.1f}+/-{se(cm):5.1f} {gm.mean().item():+9.1f}+/-{se(gm):5.1f} "
              f"{cw:8.1%} {gw.float().mean().item():8.1%} {t_cpu:6.1f} {t_gpu:6.1f}")


# ---------------------------------------------------------------- command line

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", choices=["eval", "tune", "compare", "check"])
    t, _ = sim.add_arguments(ap)
    t.add_argument("--population", type=int, default=32, help="candidates evaluated per tune iteration")
    ap.add_argument("--device", default="cuda" if torch.cuda.is_available() else "cpu")
    args = ap.parse_args()
    device = torch.device(args.device)

    if args.command in ("compare", "check"):
        params = sim.resolve_params(args, None)
        (compare if args.command == "compare" else check)(args, params, device)
        return

    game_map, stored = sim.load_map(args)
    params = sim.resolve_params(args, stored)
    print(f"map: {len(game_map.items)} items, ${game_map.total_value()} of treasure, "
          f"pig={args.pig}, miner={args.miner}, device={device}")
    print(f"params: {params}")
    if args.command == "eval":
        t0 = time.time()
        seeds = [args.seed * 100003 + e for e in range(args.episodes)]
        print(evaluate_many([game_map], [params], seeds, args.pig, args.miner, device, args.lookahead, args.gamma)[0],
              f"in {time.time() - t0:.1f}s")
    else:
        tune_params = sim.parse_tune_params(ap, args.tune_params)
        allow_rocks = any(i.type == "Rock" for i in game_map.items)
        game_map, params = tune(game_map, params, args, tune_params, allow_rocks, device)
        print(f"params: {params}")
        for i in game_map.items:
            print(f"  {i.type:12s} ({i.x:+5.2f}, {i.y:+5.2f})")

    if args.out:
        with open(args.out, "w", encoding="utf-8") as f:
            json.dump({**game_map.to_json(), "params": asdict(params)}, f, indent=2)
        print(f"wrote {args.out}")


if __name__ == "__main__":
    main()
