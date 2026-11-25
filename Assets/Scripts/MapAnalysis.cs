using System.Collections.Generic;
using UnityEngine;

// Owner: Butian (roam mode)
// What the pack (EnemyPack) knows about the map before the round: worked out once from the wall
// rocks (EnemyPathfinder's grid), the way the Counter-Strike bot analyses its navigation mesh.
//
//   HIDING SPOTS   cells with walls round most of them (Cover: the share of 16 rays that hit a
//                  wall within Hide Ray), kept Hide Spacing apart, best cover first. View: how far
//                  an enemy there can see on average (the 16 rays, up to 10 units).
//   CHOKE POINTS   the narrowest cells of the passages: least room between walls among their
//                  neighbours, with a way through on two opposite sides, at most Choke Room of room.
//   GOLD PLANS     per piece of gold (worked out the first time the pack needs it), its APPROACHES:
//                  the ways in from Approach Radius out on foot (a wide-open stretch is cut into
//                  slices of at most 90 degrees). Each has its CHOKE (the narrowest cell on that way
//                  in, 1 unit to Approach Radius from the gold) and its AMBUSH spot: the best hiding
//                  spot within Ambush Range of the choke, on the gold's side of it, that can see the
//                  choke. A defender waits in cover there and hits whoever comes through.
//
// Play mode, Scene view: hiding spots as small blue squares, chokes as yellow rings (their size the
// room there), and each planned approach as a line from its ambush spot to its choke.
public static class MapAnalysis
{
    public struct Spot { public Vector2 at; public float cover; public float view; }
    public struct Choke { public Vector2 at; public float room; }

    public class Approach
    {
        public Vector2 entry;    // where the way in starts, Approach Radius out
        public Vector2 choke;    // its narrowest point
        public float room;       // how much room there is at the choke
        public float along;      // how far the choke is from the gold on foot
        public Vector2 ambush;   // where a defender waits for it
    }

    public class GoldPlan
    {
        public Vector2 gold;
        public readonly List<Approach> approaches = new List<Approach>();
    }

    public static float HideCover = 0.5f;
    public static float HideRay = 3f;
    public static float HideSpacing = 1.5f;
    public static float ChokeRoom = 1.25f;
    public static float ApproachRadius = 6f;
    public static float AmbushRange = 3f;

    const float Walker = 0.4f;   // the walkers' size (EnemyPack's distance maps)
    const float Eye = 0.05f;     // what blocks a look: the wall rocks themselves
    const int Rays = 16;
    const int Bins = 24;

    public static readonly List<Spot> HidingSpots = new List<Spot>();
    public static readonly List<Choke> Chokes = new List<Choke>();
    static readonly Dictionary<Treasure, GoldPlan> plans = new Dictionary<Treasure, GoldPlan>();
    public static bool Built { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset()
    {
        HidingSpots.Clear(); Chokes.Clear(); plans.Clear(); Built = false;
    }

    public static void Configure(LevelData level)
    {
        Reset();
        if (level == null) return;
        HideCover = level.Param("map_hide_cover", HideCover);
        HideRay = level.Param("map_hide_ray", HideRay);
        HideSpacing = level.Param("map_hide_spacing", HideSpacing);
        ChokeRoom = level.Param("map_choke_room", ChokeRoom);
        ApproachRadius = level.Param("map_approach_radius", ApproachRadius);
        AmbushRange = level.Param("map_ambush_range", AmbushRange);
    }

    // --- The whole map: hiding spots and choke points --------------------------------------------------

    public static void Build()
    {
        HidingSpots.Clear(); Chokes.Clear(); plans.Clear();
        Built = true;
        if (!EnemyPathfinder.HasSector) return;
        bool[,] walk = EnemyPathfinder.Open(Walker);
        bool[,] see = EnemyPathfinder.Open(Eye);
        float[,] room = EnemyPathfinder.Room(Walker);
        int cols = walk.GetLength(0), rows = walk.GetLength(1);

        var hide = new List<Spot>();
        var choke = new List<Choke>();
        for (int x = 0; x < cols; x++)
            for (int y = 0; y < rows; y++)
            {
                if (!walk[x, y]) continue;
                Vector2 at = EnemyPathfinder.CellCenter(new Vector2Int(x, y));

                // Hiding spot: walls close round most of it
                if (room[x, y] <= 1.01f)
                {
                    int hits = 0; float seen = 0f;
                    for (int r = 0; r < Rays; r++)
                    {
                        float a = r * 2f * Mathf.PI / Rays;
                        var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        float free = Ray(see, at, dir, 10f);
                        if (free < HideRay) hits++;
                        seen += free;
                    }
                    float cover = (float)hits / Rays;
                    if (cover >= HideCover) hide.Add(new Spot { at = at, cover = cover, view = seen / Rays });
                }

                // Choke point: the narrowest cell round it, with a way through on two opposite sides
                if (room[x, y] <= ChokeRoom && LocalLeast(walk, room, x, y) && Passage(walk, x, y))
                    choke.Add(new Choke { at = at, room = room[x, y] });
            }

        hide.Sort((a, b) => b.cover != a.cover ? b.cover.CompareTo(a.cover) : b.view.CompareTo(a.view));
        foreach (Spot s in hide) if (Apart(s.at, HidingSpots, HideSpacing)) HidingSpots.Add(s);
        choke.Sort((a, b) => a.room.CompareTo(b.room));
        foreach (Choke c in choke)
        {
            bool apart = true;
            foreach (Choke k in Chokes) if ((k.at - c.at).sqrMagnitude < 4f) { apart = false; break; }
            if (apart) Chokes.Add(c);
        }
        Debug.Log("MapAnalysis: " + HidingSpots.Count + " hiding spots, " + Chokes.Count + " choke points");
    }

    static bool LocalLeast(bool[,] walk, float[,] room, int x, int y)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int nx = x + dx, ny = y + dy;
                if ((dx == 0 && dy == 0) || !Inside(walk, nx, ny) || !walk[nx, ny]) continue;
                if (room[nx, ny] < room[x, y]) return false;
            }
        return true;
    }

    static readonly Vector2Int[] Opposites = { new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(1, -1) };

    static bool Passage(bool[,] walk, int x, int y)
    {
        foreach (Vector2Int d in Opposites)
        {
            int ax = x + d.x, ay = y + d.y, bx = x - d.x, by = y - d.y;
            if (Inside(walk, ax, ay) && Inside(walk, bx, by) && walk[ax, ay] && walk[bx, by]) return true;
        }
        return false;
    }

    static bool Inside(bool[,] grid, int x, int y) => x >= 0 && y >= 0 && x < grid.GetLength(0) && y < grid.GetLength(1);

    static bool Apart(Vector2 at, List<Spot> kept, float spacing)
    {
        foreach (Spot s in kept) if ((s.at - at).sqrMagnitude < spacing * spacing) return false;
        return true;
    }

    // How far a look goes from `from` along `dir` before a wall (up to `max`)
    static float Ray(bool[,] see, Vector2 from, Vector2 dir, float max)
    {
        for (float t = 0.25f; t <= max; t += 0.25f)
        {
            Vector2Int c = EnemyPathfinder.CellAt(from + dir * t);
            if (!see[c.x, c.y]) return t;
        }
        return max;
    }

    // How far a look goes from `from` along `dir` before a wall rock (up to `max`)
    public static float WallDistance(Vector2 from, Vector2 dir, float max)
    {
        if (!EnemyPathfinder.HasSector) return max;
        return Ray(EnemyPathfinder.Open(Eye), from, dir.normalized, max);
    }

    // Nothing but open ground between `a` and `b` (on the grid)
    public static bool SeeLine(Vector2 a, Vector2 b)
    {
        if (!EnemyPathfinder.HasSector) return true;
        bool[,] see = EnemyPathfinder.Open(Eye);
        float len = Vector2.Distance(a, b);
        if (len < 0.01f) return true;
        return Ray(see, a, (b - a) / len, len) >= len;
    }

    // --- One piece of gold: its ways in, their chokes and ambush spots ---------------------------------

    public static GoldPlan PlanFor(Treasure t)
    {
        if (t == null) return null;
        if (!Built) Build();
        if (plans.TryGetValue(t, out GoldPlan plan)) return plan;
        plan = MakePlan(t.transform.position);
        plans[t] = plan;
        return plan;
    }

    static GoldPlan MakePlan(Vector2 gold)
    {
        var plan = new GoldPlan { gold = gold };
        var field = EnemyPathfinder.DistanceField(gold, Walker);
        if (field == null) return plan;
        float[,] room = EnemyPathfinder.Room(Walker);

        // The ring of cells Approach Radius out on foot, per 15-degree slice
        var cell = new Vector2[Bins];
        var has = new bool[Bins];
        var best = new float[Bins];
        for (int b = 0; b < Bins; b++) best[b] = float.PositiveInfinity;
        for (int x = 0; x < field.Cols; x++)
            for (int y = 0; y < field.Rows; y++)
            {
                var c = new Vector2Int(x, y);
                float off = Mathf.Abs(field.At(c) - ApproachRadius);
                if (off > 0.5f) continue;
                Vector2 at = field.Center(c);
                int b = BinOf(at - gold);
                if (off >= best[b]) continue;
                best[b] = off; cell[b] = at; has[b] = true;
            }

        // Runs of neighbouring open slices are the ways in; a wide one is cut into slices of <= 90 degrees
        foreach (List<int> run in Runs(has))
        {
            int pieces = Mathf.CeilToInt(run.Count / 6f);
            for (int k = 0; k < pieces; k++)
            {
                int from = k * run.Count / pieces, to = (k + 1) * run.Count / pieces;
                int mid = run[(from + to - 1) / 2];
                Approach a = MakeApproach(field, room, gold, cell[mid]);
                if (a != null) plan.approaches.Add(a);
            }
        }
        return plan;
    }

    static Approach MakeApproach(EnemyPathfinder.Field field, float[,] room, Vector2 gold, Vector2 entry)
    {
        // The way in: downhill on the gold's walking-distance map, from the entry to the gold
        var way = new List<Vector2Int>();
        Vector2Int c = field.CellOf(field.Snap(entry));
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
                    if (field.dist[x, y] < lowest) { lowest = field.dist[x, y]; next = new Vector2Int(x, y); }
                }
            if (next == c) break;
            c = next;
        }

        // Its choke: the least room on the way, 1 unit to Approach Radius from the gold (nearer the gold on a tie)
        int pick = -1; float score = float.PositiveInfinity;
        for (int i = 0; i < way.Count; i++)
        {
            float d = field.At(way[i]);
            if (d < 1f || d > ApproachRadius) continue;
            float s = room[way[i].x, way[i].y] + 0.05f * d;
            if (s < score) { score = s; pick = i; }
        }
        if (pick < 0) return null;
        var a = new Approach
        {
            entry = entry,
            choke = field.Center(way[pick]),
            room = room[way[pick].x, way[pick].y],
            along = field.At(way[pick]),
        };

        // Its ambush: the best hiding spot near the choke, no further out than it, that can see it
        a.ambush = a.choke;
        float bestScore = float.NegativeInfinity;
        foreach (Spot s in HidingSpots)
        {
            float dist = Vector2.Distance(s.at, a.choke);
            if (dist > AmbushRange || field.At(s.at) > a.along + 0.5f || !SeeLine(s.at, a.choke)) continue;
            float value = 2f * s.cover + 0.1f * s.view - 0.3f * dist;
            if (value > bestScore) { bestScore = value; a.ambush = s.at; }
        }
        return a;
    }

    static int BinOf(Vector2 v)
    {
        int b = Mathf.FloorToInt((Mathf.Atan2(v.y, v.x) + Mathf.PI) / (2f * Mathf.PI) * Bins);
        return ((b % Bins) + Bins) % Bins;
    }

    // Runs of neighbouring true slices, round the ring (wrapping past the end)
    static List<List<int>> Runs(bool[] has)
    {
        var runs = new List<List<int>>();
        int start = -1;
        for (int b = 0; b < Bins; b++) if (!has[b]) { start = b; break; }
        if (start < 0)
        {
            var whole = new List<int>();
            for (int b = 0; b < Bins; b++) whole.Add(b);
            runs.Add(whole);
            return runs;
        }
        List<int> current = null;
        for (int k = 1; k <= Bins; k++)
        {
            int b = (start + k) % Bins;
            if (has[b]) { if (current == null) { current = new List<int>(); runs.Add(current); } current.Add(b); }
            else current = null;
        }
        return runs;
    }

    // --- Scene view ---------------------------------------------------------------------------------------

    public static void DrawGizmos()
    {
        if (!Built) return;
        Gizmos.color = new Color(0.3f, 0.55f, 1f, 0.8f);
        foreach (Spot s in HidingSpots) Gizmos.DrawCube(s.at, Vector3.one * 0.18f);
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.8f);
        foreach (Choke c in Chokes) Gizmos.DrawWireSphere(c.at, Mathf.Max(0.15f, c.room));
        foreach (var kv in plans)
        {
            if (kv.Key == null || kv.Key.IsTaken) continue;
            foreach (Approach a in kv.Value.approaches)
            {
                Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.5f);
                Gizmos.DrawLine(a.choke, kv.Value.gold);
                Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.9f);
                Gizmos.DrawLine(a.ambush, a.choke);
                Gizmos.DrawWireCube(a.ambush, Vector3.one * 0.35f);
            }
        }
    }
}
