using System.Collections.Generic;
using UnityEngine;

// Owner: Butian (roam mode)
// The hunting enemies (RedEnemy: Red Squares and Red Rounds) working as one pack ("pack_ai": 1),
// instead of each walking straight at the player, all from the same side. Every Replan seconds it
// gives each awake enemy a spot to walk to:
//
//   1. A walking-distance map from the player round the wall rocks (EnemyPathfinder.DistanceField)
//      gives each enemy its true distance to the player.
//   2. THE NET (attack; Fang et al. 2020, the "fishing" strategy): the player is faster than every
//      enemy, so chasing never works. Seen from the player, an enemy with speed ratio L covers the
//      angle 2 asin(L + reach / distance) (its Apollonius circle: the points it gets to first), and
//      the player can only get out through the gaps between those angles. Walls cover directions
//      too. Each Red Square (cyan, BLOCKER) moves round the player to even out the gaps on its two
//      sides, so the largest gaps close first, never backing off (at most Net Radius out). Once no
//      gap is left the net is CLOSED and they SQUEEZE (orange): Squeeze Step further in each replan,
//      stopping the moment a gap opens. When they can't close it in the open, the gap is left on the
//      side of the nearest wall (HERDING), so the player's way out leads into the walls.
//   3. PREDICTION: every spot is round where the player WILL be by the time the enemy gets there:
//      its walking time moves the player on along their (smoothed) run, up to Predict seconds ahead.
//      On the way the squares keep Spacing apart (no bunching up).
//   4. RAM (orange, CLOSER): a square within Ram Range of the player (Greedy Radius once the net is
//      closed) goes for the player itself, and keeps at it until they are Let Go further off.
//   5. SHOOTERS (magenta): each Red Round walks to a spot Shoot Radius from the player with a
//      clear line to them, at least 45 degrees round from the other Rounds, so their shots come
//      in from different sides (crossfire). Their hooks lead the player (PackLeadAimer).
//   6. ATTACK OR DEFEND, by the KILL CHANCE: the share of directions round the player the net of
//      the enemies within Engage Range (and the walls) covers. At Attack Enter or more (Pounce Bonus
//      less while the player's K skill is cooling down) the pack ATTACKS: everyone within Engage Range hunts
//      (2-5 above). Below Attack Leave it DEFENDS. In between it keeps what it was doing, so it
//      doesn't flip to and fro. The nearest Min Attackers, and anyone the player is within Greedy
//      Radius of, attack either way. Every other enemy is a DEFENDER (green): it holds a POST on the
//      player's way to one of the Guarded Gold pieces nearest the PLAYER on foot, at the narrowest
//      point between the walls (EnemyPathfinder.Room: the least room round it, so one body blocks
//      it), Guard Min to Guard Reach from the gold, Spacing from the other posts. It does no circle
//      and no chasing there; it only rams a player who comes within Defend Strike. It gets a new
//      post when it can't defend this one: the player got past the line (nearer the gold on foot
//      than the post is), or the gold was taken or is no longer near the player.
//      The player walking into the posts raises the kill chance, and then the pack attacks.
//   7. ALERT: one enemy waking (the player came near or hit it) wakes every enemy within Alert
//      Radius of it.
//   8. MAP ANALYSIS (MapAnalysis): worked out once per level, like the Counter-Strike bot's nav
//      analysis: hiding spots, choke points, and per piece of gold its ways in, each with its choke
//      and an AMBUSH spot in cover beside it. Defenders wait at ambush spots, and spring the ambush
//      when the player reaches the choke (or comes within Defend Strike).
//   9. SIGHT AND LAST KNOWN POSITION: no seeing through walls. The pack only knows where the player
//      is while one of them sees them (Sight Range, no wall rock in between). Everything above runs
//      on where the pack BELIEVES the player is: the last known spot, carried on along their run
//      for up to a second. Before anyone has seen the player, each enemy holds a way in near
//      itself. Lost for over Lost After: the nearest search (yellow) the last known spot and the
//      hiding spots round it; after Search Time everyone defends the gold near where the player
//      was last seen. The Red Rounds only shoot while the pack can see the player.
//
// With the Scene view open in Play mode, each enemy draws a line to its spot in its role's colour.
public static class EnemyPack
{
    public enum Role { None, Striker, Closer, Blocker, Shooter, Guard, Search }

    public static bool Enabled;
    public static float Replan = 0.4f;
    public static float BlockRadius = 4.5f;
    public static float ShootRadius = 5f;
    public static float AlertRadius = 14f;
    public static float Lead = 0.7f;
    public static float Predict = 2.5f;       // how many seconds ahead a slot can be guessed
    public static float GreedyRadius = 6f;    // a square this close to the player goes for them (the greedy attack)
    public static float LetGo = 1.5f;         // ...and keeps at it until they are this much further off
    public static float AttackEnter = 0.5f;   // the pack attacks once its kill chance is this high...
    public static float AttackLeave = 0.3f;   // ...and falls back to defending the gold below this
    public static int MinAttackers = 2;       // the nearest this many always attack (pressure on the player)
    public static float InterceptSlack = 0.3f; // seconds of the player's reaction an enemy gets to cut a way off
    public static float PlayerSpeed = 5.5f;   // the player's full speed (Tick reads it off the character)
    public static float KillChance { get; private set; }
    public static bool Attacking { get; private set; }
    public static float DefendStrike = 2.5f;  // a holding defender rams a player this close, and goes back to its post after
    public static float GuardOffset = 1.5f;   // how far from the treasure it waits, on the player's side
    public static float GuardMin = 1f;        // a guard's choke point is this far from its gold on the way in, at least...
    public static float GuardReach = 5f;      // ...and at most this far
    public static int GuardedGold = 3;        // how many of the pieces nearest the player the guards defend
    public static float Spacing = 2.2f;       // pack members keep this far apart (RedEnemy.AvoidOthers)

    const float FieldClearance = 0.4f;   // the walker size the shared distance map is made for
    const int Bins = 24;                 // the escape ring is split into 15-degree slices

    struct Order { public Vector2 goal; public Role role; }
    static readonly Dictionary<RedEnemy, Order> orders = new Dictionary<RedEnemy, Order>();
    static float tickedAt = -1f, replanAt;
    static Vector2 lastPlayer, velocity;
    static bool seenPlayer;
    static PlayerCharacter playerBody;

    // Play mode, Scene view: the kill chance and what the pack is doing, over the player
    public static void DrawState()
    {
#if UNITY_EDITOR
        if (!Enabled || playerBody == null) return;
        MapAnalysis.DrawGizmos();
        string state = !everSeen ? "  UNSEEN (holding)"
            : PlayerVisible ? (Attacking ? (NetClosed ? "  NET CLOSED: SQUEEZE" : "  ATTACK: CLOSING THE NET") : "  DEFEND")
            : SinceSeen <= LostAfter ? "  just lost"
            : SinceSeen < SearchTime ? "  SEARCH" : "  LOST (defending)";
        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(playerBody.transform.position + new Vector3(-1f, 1.4f),
            "kill chance " + KillChance.ToString("0.00") + state);
        if (everSeen && !PlayerVisible)
        {
            // The last known spot: a white cross, with how long ago
            Gizmos.color = Color.white;
            Gizmos.DrawLine(known + new Vector2(-0.4f, -0.4f), known + new Vector2(0.4f, 0.4f));
            Gizmos.DrawLine(known + new Vector2(-0.4f, 0.4f), known + new Vector2(0.4f, -0.4f));
            UnityEditor.Handles.Label(known + new Vector2(-0.6f, 0.9f), "last seen " + SinceSeen.ToString("0.0") + "s");
        }
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Enabled = false; orders.Clear(); tickedAt = -1f; replanAt = 0f; seenPlayer = false; velocity = Vector2.zero; attacking.Clear(); NetClosed = false; posts.Clear(); KillChance = 0f; Attacking = false;
        started = false; everSeen = false; seenAt = -999f; PlayerVisible = false; searched.Clear(); lastKnownChecked = false;
    }

    // A level's settings (LevelLoader); off unless the level asks for "pack_ai"
    public static void Configure(LevelData level)
    {
        ResetStatics();
        if (level == null) return;
        Enabled = level.Param("pack_ai", 0f) > 0.5f;
        Replan = Mathf.Max(0.1f, level.Param("pack_replan", Replan));
        BlockRadius = level.Param("pack_block_radius", BlockRadius);
        ShootRadius = level.Param("pack_shoot_radius", ShootRadius);
        AlertRadius = level.Param("pack_alert", AlertRadius);
        Lead = level.Param("pack_lead", Lead);
        Predict = level.Param("pack_predict", Predict);
        GreedyRadius = level.Param("pack_greedy_radius", GreedyRadius);
        LetGo = level.Param("pack_let_go", LetGo);
        AttackEnter = level.Param("pack_attack_enter", AttackEnter);
        AttackLeave = level.Param("pack_attack_leave", AttackLeave);
        MinAttackers = Mathf.RoundToInt(level.Param("pack_min_attackers", MinAttackers));
        InterceptSlack = level.Param("pack_intercept_slack", InterceptSlack);
        DefendStrike = level.Param("pack_defend_strike", DefendStrike);
        GuardOffset = level.Param("pack_guard_offset", GuardOffset);
        GuardMin = level.Param("pack_guard_min", GuardMin);
        GuardReach = level.Param("pack_guard_reach", GuardReach);
        GuardedGold = Mathf.RoundToInt(level.Param("pack_guarded_gold", GuardedGold));
        Spacing = level.Param("pack_spacing", Spacing);
        SightRange = level.Param("pack_sight", SightRange);
        LostAfter = level.Param("pack_lost_after", LostAfter);
        SearchTime = level.Param("pack_search_time", SearchTime);
        SearchRadius = level.Param("pack_search_radius", SearchRadius);
        ShootMemory = level.Param("pack_shoot_memory", ShootMemory);
        NetRadius = level.Param("pack_net_radius", NetRadius);
        SqueezeStep = level.Param("pack_squeeze_step", SqueezeStep);
        RamRange = level.Param("pack_ram_range", RamRange);
        ContactReach = level.Param("pack_contact_reach", ContactReach);
        RoundReach = level.Param("pack_round_reach", RoundReach);
        EngageRange = level.Param("pack_engage_range", EngageRange);
        PounceBonus = level.Param("pack_pounce_bonus", PounceBonus);
        MapAnalysis.Configure(level);
    }

    // The player's velocity, smoothed over the last few physics steps
    public static Vector2 PlayerVelocity => velocity;

    public static bool TryGetOrder(RedEnemy e, out Vector2 goal, out Role role)
    {
        if (Enabled && orders.TryGetValue(e, out Order o)) { goal = o.goal; role = o.role; return true; }
        goal = default; role = Role.None;
        return false;
    }

    public static Role RoleOf(RedEnemy e)
    {
        return Enabled && orders.TryGetValue(e, out Order o) ? o.role : Role.None;
    }

    public static Color RoleColor(Role role)
    {
        switch (role)
        {
            case Role.Striker: return new Color(1f, 0.2f, 0.2f);
            case Role.Closer: return new Color(1f, 0.6f, 0.1f);
            case Role.Blocker: return new Color(0.2f, 0.9f, 1f);
            case Role.Shooter: return new Color(1f, 0.3f, 1f);
            case Role.Guard: return new Color(0.3f, 1f, 0.3f);
            case Role.Search: return new Color(1f, 1f, 0.3f);
            default: return Color.white;
        }
    }

    // Called by every pack enemy each physics step (RedEnemy.Ready); runs once per step
    public static void Tick(Transform player)
    {
        if (!Enabled || player == null || Time.fixedTime == tickedAt) return;
        tickedAt = Time.fixedTime;

        Vector2 p = player.position;
        if (playerBody == null || playerBody.transform != player) playerBody = player.GetComponent<PlayerCharacter>();
        if (playerBody != null && playerBody.moveSpeed > 0f) PlayerSpeed = playerBody.moveSpeed;

        // The round's start: every enemy takes up its place, and the pack knows where the player starts
        if (!started)
        {
            started = true;
            known = p;
            foreach (RedEnemy e in RedEnemy.All) if (e != null && e.InPack) e.WakeUp();
        }

        // Sight: the pack only knows where the player is while one of them can see them
        PlayerVisible = Sees(p);
        if (PlayerVisible)
        {
            if (seenPlayer)
            {
                Vector2 moved = p - lastPlayer;
                if (moved.magnitude > 3f) velocity = Vector2.zero;   // a respawn, not a run
                else velocity = Vector2.ClampMagnitude(Vector2.Lerp(velocity, moved / Time.fixedDeltaTime, 0.15f), 12f);
            }
            lastPlayer = p;
            known = p;
            seenAt = Time.time;
            everSeen = true;
        }
        seenPlayer = PlayerVisible;

        Alert();
        if (Time.time < replanAt) return;
        replanAt = Time.time + Replan;
        Plan(Believed());
    }

    // --- What the pack knows -------------------------------------------------------------------------
    // Like the Counter-Strike bot: no seeing through walls. An enemy sees the player within Sight
    // Range when no wall rock is in between; seeing them wakes it, and it tells the rest (they share
    // what one of them sees). Unseen, the pack works from the LAST KNOWN position, carried on along
    // the player's last run for up to a second. Lost for more than Lost After seconds: the attackers
    // SEARCH (the last known spot, then the hiding spots round it); after Search Time they give up
    // and everyone defends the gold near where the player was last seen

    public static float SightRange = 10f;
    public static float LostAfter = 0.6f;
    public static float SearchTime = 8f;
    public static float SearchRadius = 6f;
    public static float ShootMemory = 0.4f;

    static bool started, everSeen;
    static Vector2 known;
    static float seenAt = -999f;
    public static bool PlayerVisible { get; private set; }
    public static float SinceSeen => Time.time - seenAt;
    public static Vector2 LastKnown => known;

    // The pack has seen the player just now: the Red Rounds may shoot (their hooks need a target)
    public static bool CanTarget => !Enabled || SinceSeen <= ShootMemory;

    static bool Sees(Vector2 p)
    {
        bool seen = false;
        foreach (RedEnemy e in RedEnemy.All)
        {
            if (e == null || !e.InPack || !e.isActiveAndEnabled) continue;
            Vector2 me = e.Position;
            if ((me - p).sqrMagnitude > SightRange * SightRange) continue;
            if (!EnemyPathfinder.Clear(me, p, 0.05f)) continue;
            e.WakeUp();
            seen = true;
        }
        return seen;
    }

    // Where the pack thinks the player is
    static Vector2 Believed()
    {
        float since = SinceSeen;
        if (since <= 0.05f) return known;
        return known + velocity * Mathf.Min(since, 1f);
    }

    // Hiding spots already looked in (MapAnalysis index -> when), so a search moves on
    static readonly Dictionary<int, float> searched = new Dictionary<int, float>();
    static bool lastKnownChecked;

    static void Alert()
    {
        var all = RedEnemy.All;
        for (int i = 0; i < all.Count; i++)
        {
            RedEnemy a = all[i];
            if (a == null || !a.InPack || !a.Hunting) continue;
            for (int j = 0; j < all.Count; j++)
            {
                RedEnemy b = all[j];
                if (b == null || b == a || !b.InPack || b.Hunting) continue;
                if ((b.Position - a.Position).sqrMagnitude <= AlertRadius * AlertRadius) b.WakeUp();
            }
        }
    }

    // --- The plan ------------------------------------------------------------------------------------

    struct Member { public RedEnemy e; public float d; }

    static void Plan(Vector2 p)
    {
        orders.Clear();
        var field = EnemyPathfinder.DistanceField(p, FieldClearance);
        if (field == null) return;

        var all = new List<Member>();
        foreach (RedEnemy e in RedEnemy.All)
        {
            if (e == null || !e.InPack || !e.Hunting || !e.isActiveAndEnabled) continue;
            float d = field.At(e.Position);
            if (float.IsInfinity(d)) d = Vector2.Distance(e.Position, p) * 1.5f;
            all.Add(new Member { e = e, d = d });
        }
        all.Sort((a, b) => a.d.CompareTo(b.d));

        // Lost the player: search, then give up and defend
        if (!everSeen || SinceSeen > LostAfter)
        {
            KillChance = 0f;
            Attacking = false;
            attacking.Clear();
            var searchers = new List<Member>();
            var holders = new List<Member>();
            bool searching = everSeen && SinceSeen < SearchTime;
            for (int i = 0; i < all.Count; i++)
                if (searching && i < Mathf.Max(MinAttackers, 1)) searchers.Add(all[i]); else holders.Add(all[i]);
            PlanSearch(p, field, searchers);
            if (everSeen) PlanDefence(p, field, holders);
            else PlanHoldOwn(holders);   // nobody has seen the player yet: each holds a way in near itself
            return;
        }
        lastKnownChecked = false;

        // How closed the net round the player could be (the enemies within Engage Range, and the walls):
        // the kill chance. Attack when it is high enough; a player whose skill is cooling down (no
        // block, roll or charge to save them) is attacked sooner (Pounce Bonus)
        var engaged = new List<Member>();
        foreach (Member m in all) if (m.d <= EngageRange) engaged.Add(m);
        KillChance = NetCoverage(p, field, engaged, null);
        float enter = AttackEnter - (PlayerExposed ? PounceBonus : 0f);
        if (Attacking && KillChance < AttackLeave) Attacking = false;
        else if (!Attacking && KillChance >= enter) Attacking = true;

        // Who attacks: the nearest Min Attackers always (they keep the pressure on), anyone the player
        // is right by, and while attacking everyone within Engage Range. Everyone else defends
        var squares = new List<Member>();
        var rounds = new List<Member>();
        var defenders = new List<Member>();
        for (int i = 0; i < all.Count; i++)
        {
            Member m = all[i];
            bool attack = i < MinAttackers
                || Vector2.Distance(m.e.Position, p) <= GreedyRadius
                || (Attacking && m.d <= EngageRange);
            if (!attack) defenders.Add(m);
            else if (m.e is RedRound) rounds.Add(m);
            else squares.Add(m);
        }

        PlanDefence(p, field, defenders);
        PlanRounds(p, field, rounds);
        PlanNet(p, field, squares, rounds);
    }

    // The player can't save themselves right now: their K skill (block, roll, charge, ...) is cooling down
    static bool PlayerExposed => playerBody != null && playerBody.HasSkill && !playerBody.SkillActive && playerBody.SkillReadyIn > 0f;

    // --- Search: where the player was last seen ----------------------------------------------------------

    // The searchers go to the last known spot (the nearest one) and to the hiding spots within Search
    // Radius of it, nearest first, each to a different one; a spot is crossed off once a searcher
    // has stood on it (looked again only after 15 s), so the search spreads outward
    static void PlanSearch(Vector2 p, EnemyPathfinder.Field field, List<Member> searchers)
    {
        if (searchers.Count == 0) return;
        if (!MapAnalysis.Built) MapAnalysis.Build();

        // Cross off what they are standing on
        foreach (Member m in searchers)
        {
            if ((m.e.Position - p).sqrMagnitude < 1f) lastKnownChecked = true;
            for (int i = 0; i < MapAnalysis.HidingSpots.Count; i++)
                if ((MapAnalysis.HidingSpots[i].at - m.e.Position).sqrMagnitude < 0.8f) searched[i] = Time.time;
        }

        var spots = new List<Vector2>();
        if (!lastKnownChecked) spots.Add(field.Snap(p));
        var candidates = new List<int>();
        for (int i = 0; i < MapAnalysis.HidingSpots.Count; i++)
        {
            float d = field.At(MapAnalysis.HidingSpots[i].at);
            if (d > SearchRadius) continue;
            if (searched.TryGetValue(i, out float at) && Time.time - at < 15f) continue;
            candidates.Add(i);
        }
        candidates.Sort((a, b) => field.At(MapAnalysis.HidingSpots[a].at).CompareTo(field.At(MapAnalysis.HidingSpots[b].at)));
        foreach (int i in candidates) { if (spots.Count >= searchers.Count) break; spots.Add(MapAnalysis.HidingSpots[i].at); }
        if (spots.Count == 0) spots.Add(field.Snap(p));

        var placed = new bool[searchers.Count];
        var taken = new bool[spots.Count];
        for (int n = 0; n < searchers.Count; n++)
        {
            int fi = -1, si = -1; float best = float.PositiveInfinity;
            for (int i = 0; i < searchers.Count; i++)
            {
                if (placed[i]) continue;
                for (int s = 0; s < spots.Count; s++)
                {
                    if (taken[s] && spots.Count >= searchers.Count) continue;
                    float cost = (spots[s] - searchers[i].e.Position).sqrMagnitude;
                    if (cost < best) { best = cost; fi = i; si = s; }
                }
            }
            if (fi < 0) break;
            placed[fi] = true; taken[si] = true;
            orders[searchers[fi].e] = new Order { goal = spots[si], role = Role.Search };
        }
    }

    // Before anyone has seen the player: each enemy holds the way into a piece of gold nearest
    // itself (its ambush spot), a different way each, so they are spread over the map
    static void PlanHoldOwn(List<Member> holders)
    {
        if (holders.Count == 0) return;
        var ways = new List<MapAnalysis.Approach>();
        foreach (Treasure t in Treasure.Active)
        {
            if (t == null || t.IsTaken || !t.isActiveAndEnabled) continue;
            MapAnalysis.GoldPlan plan = MapAnalysis.PlanFor(t);
            if (plan != null) ways.AddRange(plan.approaches);
        }
        var placed = new bool[holders.Count];
        var taken = new bool[ways.Count];
        for (int n = 0; n < holders.Count; n++)
        {
            int fi = -1, si = -1; float best = float.PositiveInfinity;
            for (int i = 0; i < holders.Count; i++)
            {
                if (placed[i]) continue;
                for (int s = 0; s < ways.Count; s++)
                {
                    if (taken[s]) continue;
                    float cost = (ways[s].ambush - holders[i].e.Position).sqrMagnitude;
                    if (cost < best) { best = cost; fi = i; si = s; }
                }
            }
            if (fi < 0) break;
            placed[fi] = true; taken[si] = true;
            orders[holders[fi].e] = new Order { goal = ways[si].ambush, role = Role.Guard };
        }
        for (int i = 0; i < holders.Count; i++)
            if (!placed[i]) orders[holders[i].e] = new Order { goal = holders[i].e.Position, role = Role.Guard };
    }

    // --- The net: surround, then squeeze (Fang et al. 2020, "Cooperative pursuit with multi-pursuer and
    // one faster free-moving evader"; the "fishing" strategy) -------------------------------------------
    //
    // The player is faster than every enemy, so no enemy can catch them by chasing. An enemy with speed
    // ratio L (its speed over the player's) reaches first only the points of its APOLLONIUS CIRCLE,
    // which, seen from the player, always spans the same angle: 2 asin(L), however far off it is.
    // Catching on contact widens it as it closes in: 2 asin(L + reach / distance). The player gets out
    // through any gap between those angles; with no gap left (walls count as covered directions) the
    // net is CLOSED and there is no way out. So the pack:
    //   1. ENCIRCLES: each Red Square moves round the player to even out the gaps on its two sides
    //      (aiming for the middle between its neighbours' edges), which closes the largest gaps first.
    //      Walls and the Red Rounds' spots are fixed neighbours. A square never steps back: out of the
    //      net it holds at most Net Radius from the player, nearer if it already is.
    //   2. SQUEEZES once the net is closed: everyone steps Squeeze Step further in each replan, and
    //      stops squeezing the moment a gap opens again (the net must stay closed).
    //   3. HERDS when the enemies can't close it in the open (their angles don't add up to the open
    //      directions): the missing angle is left on the side of the nearest wall, so the way out the
    //      player gets leads into the walls, where the net is smaller.
    //   4. RAMS within Ram Range, and once the net is closed, within Greedy Radius.
    // The kill chance is the share of directions the net covers.

    public static float NetRadius = 5f;
    public static float SqueezeStep = 1.5f;
    public static float RamRange = 2f;
    public static float ContactReach = 0.8f;   // how far a Red Square's ram reaches (its body and the player's)
    public static float RoundReach = 1.5f;     // how far past its body a Red Round's hook makes a direction unsafe
    public static float EngageRange = 16f;     // enemies this far on foot or nearer take part in the net
    public static float PounceBonus = 0.15f;   // the kill chance needed to attack drops this much while the player's skill cools down
    public static bool NetClosed { get; private set; }
    static readonly HashSet<RedEnemy> attacking = new HashSet<RedEnemy>();   // ramming now (kept until Let Go further off)

    const int Fine = 72;   // the net's directions, 5 degrees each

    struct Arc { public float angle, half; public RedEnemy e; }   // e null: a wall, or the herding side

    // The angle (each side) an enemy covers, seen from the player
    static float HalfAngle(Member m, Vector2 p)
    {
        float straight = Mathf.Max(0.3f, Vector2.Distance(m.e.Position, p));
        float ratio = Mathf.Min(0.99f, m.e.moveSpeed / Mathf.Max(0.1f, PlayerSpeed));
        float reach = m.e is RedRound ? RoundReach : ContactReach;
        float half = Mathf.Asin(Mathf.Clamp01(ratio + reach / straight));
        // Round the walls its way in is longer than the straight line, so it covers less
        float detour = Mathf.Clamp(straight / Mathf.Max(straight, m.d), 0.3f, 1f);
        return half * detour;
    }

    // The walls round the player: the directions with no way out Net Radius away on foot, as arcs
    static void WallArcs(Vector2 p, EnemyPathfinder.Field field, List<Arc> arcs)
    {
        var cell = new Vector2[Bins];
        var open = new bool[Bins];
        Ring(p, field, NetRadius, false, cell, open);
        int start = -1;
        for (int b = 0; b < Bins; b++) if (open[b]) { start = b; break; }
        if (start < 0) { arcs.Add(new Arc { angle = 0f, half = Mathf.PI }); return; }   // walled in all round
        int run = 0, first = 0;
        for (int k = 1; k <= Bins; k++)
        {
            int b = (start + k) % Bins;
            if (!open[b]) { if (run == 0) first = b; run++; continue; }
            if (run > 0)
            {
                float a0 = BinAngle(first) - Mathf.PI / Bins;
                float width = run * 2f * Mathf.PI / Bins;
                arcs.Add(new Arc { angle = a0 + width * 0.5f, half = width * 0.5f });
                run = 0;
            }
        }
    }

    static float BinAngle(int b) => (b + 0.5f) / Bins * 2f * Mathf.PI - Mathf.PI;

    // Walls and enemies as arcs round the player; returns the share of directions they cover
    static float NetCoverage(Vector2 p, EnemyPathfinder.Field field, List<Member> net, List<Arc> arcs)
    {
        if (arcs == null) arcs = new List<Arc>();
        arcs.Clear();
        WallArcs(p, field, arcs);
        foreach (Member m in net)
            arcs.Add(new Arc { angle = Angle(m.e.Position - p), half = HalfAngle(m, p), e = m.e });
        int covered = 0;
        for (int j = 0; j < Fine; j++)
        {
            float a = (j + 0.5f) / Fine * 2f * Mathf.PI - Mathf.PI;
            foreach (Arc arc in arcs)
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, arc.angle * Mathf.Rad2Deg)) <= arc.half * Mathf.Rad2Deg) { covered++; break; }
        }
        return (float)covered / Fine;
    }

    static void PlanNet(Vector2 p, EnemyPathfinder.Field field, List<Member> squares, List<Member> rounds)
    {
        if (squares.Count == 0) { NetClosed = false; return; }
        var net = new List<Member>(squares);
        net.AddRange(rounds);
        var arcs = new List<Arc>();
        float coverage = NetCoverage(p, field, net, arcs);
        if (NetClosed && coverage < 0.9f) NetClosed = false;
        else if (!NetClosed && coverage >= 0.98f) NetClosed = true;

        // Herding: what the net is short of, left on the side of the nearest wall
        float total = 0f;
        foreach (Arc arc in arcs) total += 2f * arc.half;
        float shortBy = 2f * Mathf.PI - total;
        if (shortBy > 0f && WallSide(p, out float wallAngle))
            arcs.Add(new Arc { angle = wallAngle, half = Mathf.Min(Mathf.PI * 0.5f, shortBy * 0.5f) });

        // Round the player in angle order; each square aims for the middle between its neighbours' edges
        arcs.Sort((a, b) => a.angle.CompareTo(b.angle));
        var target = new Dictionary<RedEnemy, float>();
        int n = arcs.Count;
        for (int i = 0; i < n; i++)
        {
            Arc me = arcs[i];
            if (me.e == null || me.e is RedRound) continue;
            if (n == 1)
            {
                // Alone: cut off the way the player is running
                target[me.e] = velocity.sqrMagnitude > 1f ? Angle(velocity) : me.angle;
                continue;
            }
            // Going round from the previous arc to the next one (the whole way round when there are two)
            Arc prev = arcs[(i - 1 + n) % n], next = arcs[(i + 1) % n];
            float from = prev.angle, to = next.angle;
            if (n == 2) to = from + 2f * Mathf.PI;
            else while (to <= from) to += 2f * Mathf.PI;
            target[me.e] = ((from + prev.half) + (to - next.half)) * 0.5f;
        }

        foreach (Member m in squares)
        {
            float d = Vector2.Distance(m.e.Position, p);
            // Ram: right by the player, or anyone near once the net is closed (with hysteresis)
            float reach = attacking.Contains(m.e) ? (NetClosed ? GreedyRadius : RamRange) + LetGo : (NetClosed ? GreedyRadius : RamRange);
            if (d <= reach)
            {
                attacking.Add(m.e);
                float t = Mathf.Min(d / Mathf.Max(0.1f, m.e.moveSpeed), 1f);
                orders[m.e] = new Order { goal = field.Snap(p + velocity * t), role = Role.Closer };
                continue;
            }
            attacking.Remove(m.e);
            if (!target.TryGetValue(m.e, out float angle)) angle = Angle(m.e.Position - p);
            // Never back off: hold at Net Radius at most, nearer if already nearer; squeeze when closed
            float radius = NetClosed ? Mathf.Max(0f, d - SqueezeStep) : Mathf.Min(d, NetRadius);
            orders[m.e] = new Order { goal = PredictedSlot(p, Dir(angle) * radius, m, field), role = NetClosed ? Role.Closer : Role.Blocker };
        }
    }

    // The direction of the nearest wall from the player (within 10 units)
    static bool WallSide(Vector2 p, out float angle)
    {
        angle = 0f;
        float best = 10f;
        bool found = false;
        for (int b = 0; b < Bins; b++)
        {
            float a = BinAngle(b);
            float dist = MapAnalysis.WallDistance(p, Dir(a), 10f);
            if (dist < best) { best = dist; angle = a; found = true; }
        }
        return found;
    }

    // --- Defence: holding the ways to the gold near the player ---------------------------------------

    // A defender's post: the gold it defends, where it stands (a choke point on the player's way in),
    // and how far that is from the gold on foot. Kept while it defends, so it doesn't drift
    class Post
    {
        public Treasure gold;
        public MapAnalysis.Approach way;   // the way in it holds (null: none)
        public Vector2 spot;
        public float along;
    }
    static readonly Dictionary<RedEnemy, Post> posts = new Dictionary<RedEnemy, Post>();

    // The defenders this moment (the pack can't get the player now) each hold a post on the player's
    // way to one of the Guarded Gold pieces nearest the player: no circle, no chasing; it only rams a
    // player who comes within Defend Strike. A post is given again when it can't be defended:
    //   - the player got past it: they are nearer the gold on foot than the post is
    //   - its gold was taken, or is no longer one of the Guarded Gold (+1) pieces nearest the player
    // An enemy that goes over to the attack gives its post up
    static void PlanDefence(Vector2 p, EnemyPathfinder.Field field, List<Member> defenders)
    {
        var keep = new HashSet<RedEnemy>();
        foreach (Member m in defenders) keep.Add(m.e);
        var drop = new List<RedEnemy>();
        foreach (var kv in posts) if (kv.Key == null || !keep.Contains(kv.Key)) drop.Add(kv.Key);
        foreach (RedEnemy e in drop) posts.Remove(e);
        if (defenders.Count == 0) return;

        // The gold near the player, nearest on foot first; not a piece they are already on (theirs anyway)
        var near = new List<Treasure>();
        var nearD = new Dictionary<Treasure, float>();
        foreach (Treasure t in Treasure.Active)
        {
            if (t == null || t.IsTaken || !t.isActiveAndEnabled) continue;
            float d = field.At((Vector2)t.transform.position);
            if (float.IsInfinity(d) || d < 2f) continue;
            near.Add(t); nearD[t] = d;
        }
        near.Sort((a, b) => nearD[a].CompareTo(nearD[b]));
        int pieces = Mathf.Max(1, GuardedGold);

        // Posts that can't be defended any more
        drop.Clear();
        foreach (var kv in posts)
        {
            Treasure g = kv.Value.gold;
            bool lost = g == null || g.IsTaken || !g.isActiveAndEnabled;
            int rank = lost ? -1 : near.IndexOf(g);
            bool moved = rank < 0 || rank > pieces;   // the player went off towards other gold
            bool passed = !lost && field.At((Vector2)g.transform.position) < kv.Value.along - 0.5f;
            if (lost || moved || passed) drop.Add(kv.Key);
        }
        foreach (RedEnemy e in drop) posts.Remove(e);

        // New posts, from the map analysis (MapAnalysis.PlanFor): each nearby piece's ways in, the way
        // the player would take first, then the others nearest the player; round the pieces in turn
        // (every piece's likeliest way, then every piece's second, ...). The post is the way's AMBUSH
        // spot: in cover beside its choke point
        var free = new List<Member>();
        foreach (Member m in defenders) if (!posts.ContainsKey(m.e)) free.Add(m);
        if (free.Count > 0 && near.Count > 0)
        {
            var held = new HashSet<MapAnalysis.Approach>();
            foreach (Post post in posts.Values) if (post.way != null) held.Add(post.way);
            var ranked = new List<List<MapAnalysis.Approach>>();
            var goldOf = new Dictionary<MapAnalysis.Approach, Treasure>();
            for (int i = 0; i < Mathf.Min(pieces, near.Count); i++)
            {
                MapAnalysis.GoldPlan plan = MapAnalysis.PlanFor(near[i]);
                if (plan == null || plan.approaches.Count == 0) continue;
                List<Vector2Int> playerWay = WayIn(field, plan.gold);
                var likely = new Dictionary<MapAnalysis.Approach, float>();
                foreach (MapAnalysis.Approach a in plan.approaches) likely[a] = Likely(a, playerWay, field);
                var ways = new List<MapAnalysis.Approach>(plan.approaches);
                ways.Sort((a, b) => likely[a].CompareTo(likely[b]));
                ranked.Add(ways);
                foreach (MapAnalysis.Approach a in ways) goldOf[a] = near[i];
            }
            var slots = new List<MapAnalysis.Approach>();
            for (int r = 0; slots.Count < free.Count; r++)
            {
                bool any = false;
                foreach (List<MapAnalysis.Approach> ways in ranked)
                {
                    if (r >= ways.Count || slots.Count >= free.Count) continue;
                    any = true;
                    if (!held.Contains(ways[r])) slots.Add(ways[r]);
                }
                if (!any) break;
            }

            // Who takes which: the closest pair first, again and again
            var placed = new bool[free.Count];
            var taken = new bool[slots.Count];
            for (int n = 0; n < free.Count; n++)
            {
                int fi = -1, si = -1; float best = float.PositiveInfinity;
                for (int i = 0; i < free.Count; i++)
                {
                    if (placed[i]) continue;
                    for (int s = 0; s < slots.Count; s++)
                    {
                        if (taken[s]) continue;
                        float cost = (slots[s].ambush - free[i].e.Position).sqrMagnitude;
                        if (cost < best) { best = cost; fi = i; si = s; }
                    }
                }
                if (fi < 0) break;
                placed[fi] = true; taken[si] = true;
                MapAnalysis.Approach way = slots[si];
                posts[free[fi].e] = new Post { gold = goldOf[way], way = way, spot = way.ambush, along = way.along };
            }
        }

        foreach (Member m in defenders)
        {
            if (!posts.TryGetValue(m.e, out Post post))
            {
                orders[m.e] = new Order { goal = m.e.Position, role = Role.Guard };   // nothing left to hold: it waits
                continue;
            }
            // The player at its choke, or right by it: it springs the ambush
            bool strike = Vector2.Distance(m.e.Position, p) <= DefendStrike
                || (post.way != null && Vector2.Distance(post.way.choke, p) <= 1.5f);
            orders[m.e] = strike ? new Order { goal = p, role = Role.Closer } : new Order { goal = post.spot, role = Role.Guard };
        }
    }

    // How likely the player is to come in this way: on their own way to the gold (its choke within
    // 1.5 units of it) first, nearest the gold first; then the rest, nearest the player first
    static float Likely(MapAnalysis.Approach a, List<Vector2Int> playerWay, EnemyPathfinder.Field field)
    {
        float best = float.PositiveInfinity;
        foreach (Vector2Int c in playerWay) best = Mathf.Min(best, (field.Center(c) - a.choke).sqrMagnitude);
        if (best <= 2.25f) return a.along;
        return 100f + field.At(a.entry);
    }

    // The way the player would walk to `goldAt`, as cells from the gold back to the player: downhill
    // on the player's walking-distance map
    static List<Vector2Int> WayIn(EnemyPathfinder.Field field, Vector2 goldAt)
    {
        var way = new List<Vector2Int>();
        Vector2Int c = field.CellOf(field.Snap(goldAt));
        for (int guard = 0; guard < 4000; guard++)
        {
            way.Add(c);
            float here = field.At(c);
            if (here <= 0f) break;
            Vector2Int next = c; float lowest = here;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int x = c.x + dx, y = c.y + dy;
                    if ((dx == 0 && dy == 0) || x < 0 || y < 0 || x >= field.Cols || y >= field.Rows) continue;
                    float d = field.dist[x, y];
                    if (d < lowest) { lowest = d; next = new Vector2Int(x, y); }
                }
            if (next == c) break;
            c = next;
        }
        return way;
    }

    // A guard's spot for the gold at `goldAt`: on the player's way to it, Guard Min to Guard Reach
    // from it, the cell with the least room round it (a choke point; nearer the gold on a tie), at
    // least Spacing from the spots already given out. No such cell (the player is right by the
    // gold, or the others have the way): Guard Offset from the gold, round to a free side
    static Vector2 ChokeSpot(Vector2 p, EnemyPathfinder.Field field, float[,] room, Vector2 goldAt, List<Vector2> given)
    {
        List<Vector2Int> way = WayIn(field, goldAt);
        float goldD = field.At(way[0]);
        int best = -1; float bestScore = float.PositiveInfinity;
        for (int i = 0; i < way.Count; i++)
        {
            Vector2Int c = way[i];
            float along = goldD - field.At(c);
            if (along < GuardMin) continue;
            if (along > GuardReach) break;
            Vector2 at = field.Center(c);
            if (TooClose(at, given)) continue;
            float score = room[c.x, c.y] + 0.1f * along;
            if (score < bestScore) { bestScore = score; best = i; }
        }
        if (best >= 0) return field.Center(way[best]);

        float toward = Angle(p - goldAt);
        for (int k = 0; k < 6; k++)
        {
            float a = toward + (k % 2 == 0 ? 1f : -1f) * ((k + 1) / 2) * Mathf.PI / 3f;
            Vector2 at = field.Snap(goldAt + Dir(a) * GuardOffset);
            if (!TooClose(at, given)) return at;
        }
        return field.Snap(goldAt + Dir(toward) * GuardOffset);
    }

    static bool TooClose(Vector2 at, List<Vector2> given)
    {
        foreach (Vector2 g in given) if ((g - at).sqrMagnitude < Spacing * Spacing) return true;
        return false;
    }

    // Where to go for the player: a little ahead of where they are running, on open ground
    static Vector2 Intercept(Vector2 p, EnemyPathfinder.Field field, Member m, float maxLead)
    {
        float t = Lead * Mathf.Min(m.d / Mathf.Max(0.1f, m.e.moveSpeed), maxLead);
        return field.Snap(p + velocity * t);
    }

    static float Angle(Vector2 v) => Mathf.Atan2(v.y, v.x);
    static int BinOf(Vector2 from, Vector2 to)
    {
        float a = Angle(to - from);
        int b = Mathf.FloorToInt((a + Mathf.PI) / (2f * Mathf.PI) * Bins);
        return ((b % Bins) + Bins) % Bins;
    }

    // The ring of cells `radius` from the player on foot: per slice, the cell nearest the radius
    // (has[b] false where the slice has none: a wall that way, or no room)
    static void Ring(Vector2 p, EnemyPathfinder.Field field, float radius, bool needLine,
                     Vector2[] cell, bool[] has)
    {
        var best = new float[Bins];
        for (int b = 0; b < Bins; b++) { has[b] = false; best[b] = float.PositiveInfinity; }
        const float band = 0.5f;
        for (int x = 0; x < field.Cols; x++)
            for (int y = 0; y < field.Rows; y++)
            {
                var c = new Vector2Int(x, y);
                float off = Mathf.Abs(field.At(c) - radius);
                if (off > band) continue;
                Vector2 at = field.Center(c);
                int b = BinOf(p, at);
                if (off >= best[b]) continue;
                if (needLine && !EnemyPathfinder.Clear(at, p, 0.1f)) continue;
                best[b] = off; cell[b] = at; has[b] = true;
            }
    }

    static Vector2 Dir(float a) => new Vector2(Mathf.Cos(a), Mathf.Sin(a));

    // Its slot round where the player will be when it gets there: the time to walk there (on foot
    // round the walls at first, then straight to the guessed slot) moves the player on at their
    // run, up to Predict seconds ahead; on open ground in the sector
    static Vector2 PredictedSlot(Vector2 p, Vector2 offset, Member m, EnemyPathfinder.Field field)
    {
        float speed = Mathf.Max(0.1f, m.e.moveSpeed);
        float t = Mathf.Min(m.d / speed, Predict);
        Vector2 slot = p + offset;
        for (int k = 0; k < 3; k++)
        {
            slot = p + velocity * t + offset;
            t = Mathf.Min(Vector2.Distance(m.e.Position, slot) / speed, Predict);
        }
        return field.Snap(slot);
    }

    static void Cover(bool[] covered, int bin, int spread)
    {
        for (int k = -spread; k <= spread; k++) covered[((bin + k) % Bins + Bins) % Bins] = true;
    }

    static void PlanRounds(Vector2 p, EnemyPathfinder.Field field, List<Member> rounds)
    {
        if (rounds.Count == 0) return;
        var cell = new Vector2[Bins];
        var visible = new bool[Bins];
        // Hooks that fly through rocks need no clear line: any spot on the ring will do
        bool needLine = false;
        foreach (Member m in rounds) if (!(m.e is RedRound r) || !r.throughWalls) needLine = true;
        Ring(p, field, ShootRadius, needLine, cell, visible);
        var taken = new bool[Bins];
        const int apart = 3;   // slices (45 degrees) kept free either side of a Round's line of fire

        foreach (Member m in rounds)
        {
            Vector2 me = m.e.Position;
            var round = m.e as RedRound;
            float range = round != null ? round.shootRange : ShootRadius + 2f;
            int mine = BinOf(p, me);
            // Already with a clear shot, in range, and from its own side: it holds the spot
            if (!taken[mine] && Vector2.Distance(me, p) <= range
                && ((round != null && round.throughWalls) || EnemyPathfinder.Clear(me, p, 0.1f)))
            {
                orders[m.e] = new Order { goal = me, role = Role.Shooter };
                Cover(taken, mine, apart);
                continue;
            }
            int pick = Nearest(cell, visible, taken, me);
            if (pick < 0) pick = Nearest(cell, visible, null, me);
            if (pick < 0) { orders[m.e] = new Order { goal = Intercept(p, field, m, 1f), role = Role.Shooter }; continue; }
            orders[m.e] = new Order { goal = cell[pick], role = Role.Shooter };
            Cover(taken, pick, apart);
        }
    }

    static int Nearest(Vector2[] cell, bool[] has, bool[] taken, Vector2 me)
    {
        int best = -1; float bestDist = float.PositiveInfinity;
        for (int b = 0; b < Bins; b++)
        {
            if (!has[b] || (taken != null && taken[b])) continue;
            float d = (cell[b] - me).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = b; }
        }
        return best;
    }
}
