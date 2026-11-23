using System.Collections.Generic;
using UnityEngine;

// Owner: Butian (roam mode)
// The hunting enemies (RedEnemy: Red Squares and Red Rounds) working as one pack ("pack_ai": 1),
// instead of each walking straight at the player, all from the same side. Every Replan seconds it
// gives each awake enemy a spot to walk to:
//
//   1. A walking-distance map from the player round the wall rocks (EnemyPathfinder.DistanceField)
//      gives each enemy its true distance to the player.
//   2. THE CIRCLE: Block Radius round the player, one slot per Red Square outside it, evenly
//      spaced, one slot straight ahead of the player's run (cutting off where they are going).
//      The squares take the slots in the order they already stand round the player, so their
//      paths never cross, and keep Spacing apart on the way (no bunching up).
//   3. PREDICTION (cyan, BLOCKER): a square outside the circle does not chase. It walks to its
//      slot round where the player WILL be by the time it gets there: its walking time moves the
//      player on along their (smoothed) run, up to Predict seconds ahead.
//   4. GREEDY (orange, CLOSER): a square within Greedy Radius of the player (a bit wider than the
//      circle) goes for the player itself, a little ahead of their run, and keeps at it until the
//      player is Let Go further off. With the slots all round, the player runs into the circle somewhere.
//   5. SHOOTERS (magenta): each Red Round walks to a spot Shoot Radius from the player with a
//      clear line to them, at least 45 degrees round from the other Rounds, so their shots come
//      in from different sides (crossfire). Their hooks lead the player (PackLeadAimer).
//   6. ATTACK OR DEFEND, by the KILL CHANCE (Expectation): the share of the player's ways out
//      (the open slices of the escape ring) some enemy can reach no later than the player can.
//      At Attack Enter or more the pack ATTACKS: every enemy that can cut off a way out hunts
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
            : PlayerVisible ? (Attacking ? "  ATTACK" : "  DEFEND")
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
        Enabled = false; orders.Clear(); tickedAt = -1f; replanAt = 0f; seenPlayer = false; velocity = Vector2.zero; haveBase = false; attacking.Clear(); posts.Clear(); KillChance = 0f; Attacking = false;
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

        // How likely the pack is to get the player now, and so: attack, or defend the gold
        var covers = new Dictionary<RedEnemy, int>();
        KillChance = Expectation(p, field, all, covers);
        if (Attacking && KillChance < AttackLeave) Attacking = false;
        else if (!Attacking && KillChance >= AttackEnter) Attacking = true;

        // Who attacks: the nearest Min Attackers always (they keep the pressure on), anyone the player
        // is right by, and while attacking every enemy that can cut off a way out. Everyone else defends
        var squares = new List<Member>();
        var rounds = new List<Member>();
        var defenders = new List<Member>();
        for (int i = 0; i < all.Count; i++)
        {
            Member m = all[i];
            bool attack = i < MinAttackers
                || Vector2.Distance(m.e.Position, p) <= GreedyRadius
                || (Attacking && covers.TryGetValue(m.e, out int c) && c > 0);
            if (!attack) defenders.Add(m);
            else if (m.e is RedRound) rounds.Add(m);
            else squares.Add(m);
        }

        PlanDefence(p, field, defenders);
        PlanSquares(p, field, squares);
        PlanRounds(p, field, rounds);
    }

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

    // --- The kill chance --------------------------------------------------------------------------------

    // The pack's chance to get the player right now (0..1), as the share of the player's ways out it
    // can cut off. The ways out are the open slices of the escape ring, Block Radius from the player
    // on foot. A slice is cut off when some enemy gets there no later than the player does (the
    // player running at full speed, plus Intercept Slack seconds): a Red Square on foot (straight
    // line, a little longer for the walls), a Red Round when that spot is within its shot with a clear
    // line. Walls all round (no way out at all): 1. `covers`: how many ways out each enemy cuts off
    static float Expectation(Vector2 p, EnemyPathfinder.Field field, List<Member> all, Dictionary<RedEnemy, int> covers)
    {
        var cell = new Vector2[Bins];
        var open = new bool[Bins];
        Ring(p, field, BlockRadius, false, cell, open);
        float playerTime = BlockRadius / Mathf.Max(0.1f, PlayerSpeed) + InterceptSlack;

        int ways = 0, cut = 0;
        for (int b = 0; b < Bins; b++)
        {
            if (!open[b]) continue;
            ways++;
            bool any = false;
            foreach (Member m in all)
            {
                Vector2 me = m.e.Position;
                float dist = Vector2.Distance(me, cell[b]);
                bool covered = m.e is RedRound round
                    ? dist <= round.shootRange && (round.throughWalls || EnemyPathfinder.Clear(me, cell[b], 0.1f))
                    : dist * 1.15f / Mathf.Max(0.1f, m.e.moveSpeed) <= playerTime;
                if (!covered) continue;
                covers[m.e] = (covers.TryGetValue(m.e, out int n) ? n : 0) + 1;
                any = true;
            }
            if (any) cut++;
        }
        return ways == 0 ? 1f : (float)cut / ways;
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

    // The Red Squares: a circle of Block Radius round the player, one slot per square, evenly spaced,
    // one slot straight ahead of the player's run. Outside the circle a square does not chase: it
    // walks to its slot, placed round where the player WILL be by the time it gets there. Inside
    // the circle it goes for the player itself
    static float baseAngle;
    static readonly HashSet<RedEnemy> attacking = new HashSet<RedEnemy>();
    static bool haveBase;

    static void PlanSquares(Vector2 p, EnemyPathfinder.Field field, List<Member> squares)
    {
        if (squares.Count == 0) return;

        // The circle's turn: a slot ahead of the run (it cuts off where the player is going); while
        // the player stands still it keeps the last one
        if (velocity.magnitude > 1f) { baseAngle = Angle(velocity); haveBase = true; }
        else if (!haveBase)
        {
            Vector2 mid = Vector2.zero;
            foreach (Member m in squares) mid += m.e.Position;
            baseAngle = Angle(mid / squares.Count - p) + Mathf.PI;   // the side away from them
            haveBase = true;
        }

        // Within Greedy Radius: for the player, a little ahead of their run. Once on the attack it
        // keeps at it until the player is Greedy Radius + Let Go clear of it, so it doesn't step back
        // the moment the player comes near, or flip to and fro at the edge
        var outside = new List<Member>();
        foreach (Member m in squares)
        {
            float dist = Vector2.Distance(m.e.Position, p);
            float reach = attacking.Contains(m.e) ? GreedyRadius + LetGo : GreedyRadius;
            if (dist > reach) { attacking.Remove(m.e); outside.Add(m); continue; }
            attacking.Add(m.e);
            float t = Mathf.Min(dist / Mathf.Max(0.1f, m.e.moveSpeed), 1f);
            orders[m.e] = new Order { goal = field.Snap(p + velocity * t), role = Role.Closer };
        }
        int n = outside.Count;
        if (n == 0) return;

        // Outside: slots in the order they already stand round the player (so no two cross), turned
        // whichever way puts them nearest their slots overall
        outside.Sort((a, b) => Around(a.e.Position, p, baseAngle).CompareTo(Around(b.e.Position, p, baseAngle)));
        int bestTurn = 0; float bestCost = float.PositiveInfinity;
        for (int turn = 0; turn < n; turn++)
        {
            float cost = 0f;
            for (int i = 0; i < n; i++)
                cost += (outside[i].e.Position - (p + Dir(SlotAngle((i + turn) % n, n)) * BlockRadius)).sqrMagnitude;
            if (cost < bestCost) { bestCost = cost; bestTurn = turn; }
        }

        for (int i = 0; i < n; i++)
        {
            Member m = outside[i];
            Vector2 offset = Dir(SlotAngle((i + bestTurn) % n, n)) * BlockRadius;
            orders[m.e] = new Order { goal = PredictedSlot(p, offset, m, field), role = Role.Blocker };
        }
    }

    static float SlotAngle(int k, int n) => baseAngle + 2f * Mathf.PI * k / n;
    static Vector2 Dir(float a) => new Vector2(Mathf.Cos(a), Mathf.Sin(a));

    // How far round the circle (0..2 pi, from the slot ahead of the run) `at` stands
    static float Around(Vector2 at, Vector2 c, float from) => Mathf.Repeat(Angle(at - c) - from, 2f * Mathf.PI);

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
