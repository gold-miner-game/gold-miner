using System.Collections.Generic;
using UnityEngine;

// Owner: Butian (attack mode)
// A* over a grid laid on the attack sector, so the enemies (RedEnemy) walk round the rock walls to
// the player instead of heading straight at them. The sector is cut into cells of Cell world units;
// a cell is shut when a wall rock (Rock.isWall) lies within the walker's clearance (its radius) of
// the cell's centre. The walls never move, so each clearance's grid is built once per round, the
// first time an enemy asks (LevelLoader sets the sector and clears the grids when a level starts).
public static class EnemyPathfinder
{
    public const float Cell = 0.5f;

    static Rect sector;
    static readonly Dictionary<int, bool[,]> grids = new Dictionary<int, bool[,]>();   // clearance (cm) -> open cells

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { grids.Clear(); sector = default; }

    // A new level: its sector, and forget the last level's walls
    public static void SetSector(Rect area)
    {
        sector = area;
        grids.Clear();
    }

    public static bool HasSector => sector.width > 0f && sector.height > 0f;

    static int Cols => Mathf.Max(1, Mathf.FloorToInt(sector.width / Cell));
    static int Rows => Mathf.Max(1, Mathf.FloorToInt(sector.height / Cell));

    static Vector2 Center(int x, int y) => sector.min + new Vector2((x + 0.5f) * Cell, (y + 0.5f) * Cell);

    static Vector2Int CellOf(Vector2 p) => new Vector2Int(
        Mathf.Clamp(Mathf.FloorToInt((p.x - sector.xMin) / Cell), 0, Cols - 1),
        Mathf.Clamp(Mathf.FloorToInt((p.y - sector.yMin) / Cell), 0, Rows - 1));

    static bool[,] Grid(float clearance)
    {
        int key = Mathf.RoundToInt(clearance * 100f);
        if (grids.TryGetValue(key, out bool[,] open)) return open;

        int cols = Cols, rows = Rows;
        open = new bool[cols, rows];
        float r = clearance + 0.05f;
        for (int x = 0; x < cols; x++)
            for (int y = 0; y < rows; y++)
            {
                Vector2 c = Center(x, y);
                // The sector's own edge counts as a wall
                bool free = c.x - r > sector.xMin && c.x + r < sector.xMax && c.y - r > sector.yMin && c.y + r < sector.yMax;
                if (free)
                    foreach (Collider2D col in Physics2D.OverlapCircleAll(c, r))
                    {
                        if (col.isTrigger) continue;
                        Rock rock = col.GetComponentInParent<Rock>();
                        if (rock != null && rock.isWall) { free = false; break; }
                    }
                open[x, y] = free;
            }
        grids[key] = open;
        return open;
    }

    // The nearest open cell to `c` (itself when open), searching outward ring by ring
    static bool NearestOpen(bool[,] open, Vector2Int c, out Vector2Int found)
    {
        int cols = open.GetLength(0), rows = open.GetLength(1);
        int maxRing = Mathf.Max(cols, rows);
        for (int ring = 0; ring <= maxRing; ring++)
            for (int dx = -ring; dx <= ring; dx++)
                for (int dy = -ring; dy <= ring; dy++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != ring) continue;
                    int x = c.x + dx, y = c.y + dy;
                    if (x < 0 || y < 0 || x >= cols || y >= rows || !open[x, y]) continue;
                    found = new Vector2Int(x, y);
                    return true;
                }
        found = c;
        return false;
    }

    static readonly Vector2Int[] Steps =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
        new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1),
    };

    // A* from `from` to `to` for a walker of radius `clearance`: fills `path` with the cell centres to
    // walk through (the last one is `to` itself). False when there is no way through.
    public static bool FindPath(Vector2 from, Vector2 to, float clearance, List<Vector2> path)
    {
        path.Clear();
        if (!HasSector) return false;
        bool[,] open = Grid(clearance);
        int cols = open.GetLength(0), rows = open.GetLength(1);
        if (!NearestOpen(open, CellOf(from), out Vector2Int start)) return false;
        if (!NearestOpen(open, CellOf(to), out Vector2Int goal)) return false;

        int count = cols * rows;
        var g = new float[count];
        var parent = new int[count];
        var closed = new bool[count];
        for (int i = 0; i < count; i++) { g[i] = float.PositiveInfinity; parent[i] = -1; }

        var heap = new MinHeap();
        int s = start.y * cols + start.x, goalIndex = goal.y * cols + goal.x;
        g[s] = 0f;
        heap.Push(s, Heuristic(start, goal));

        while (heap.Count > 0)
        {
            int current = heap.Pop();
            if (closed[current]) continue;
            closed[current] = true;
            if (current == goalIndex) break;

            int cx = current % cols, cy = current / cols;
            foreach (Vector2Int step in Steps)
            {
                int nx = cx + step.x, ny = cy + step.y;
                if (nx < 0 || ny < 0 || nx >= cols || ny >= rows || !open[nx, ny]) continue;
                // No cutting a corner past a shut cell
                if (step.x != 0 && step.y != 0 && (!open[cx + step.x, cy] || !open[cx, cy + step.y])) continue;
                int next = ny * cols + nx;
                if (closed[next]) continue;
                float cost = g[current] + (step.x != 0 && step.y != 0 ? 1.4142f : 1f);
                if (cost >= g[next]) continue;
                g[next] = cost;
                parent[next] = current;
                heap.Push(next, cost + Heuristic(new Vector2Int(nx, ny), goal));
            }
        }
        if (!closed[goalIndex]) return false;

        for (int i = goalIndex; i != -1 && i != s; i = parent[i]) path.Add(Center(i % cols, i / cols));
        path.Reverse();
        if (path.Count > 0) path[path.Count - 1] = goal == CellOf(to) ? to : path[path.Count - 1];
        else path.Add(goal == CellOf(to) ? to : Center(goal.x, goal.y));
        return true;
    }

    // Octile distance: the true length of an open 8-way grid walk
    static float Heuristic(Vector2Int a, Vector2Int b)
    {
        int dx = Mathf.Abs(a.x - b.x), dy = Mathf.Abs(a.y - b.y);
        return Mathf.Max(dx, dy) + 0.4142f * Mathf.Min(dx, dy);
    }

    // No wall rock across a walker of radius `clearance` going straight from `a` to `b`
    public static bool Clear(Vector2 a, Vector2 b, float clearance)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 0.001f) return true;
        foreach (RaycastHit2D hit in Physics2D.CircleCastAll(a, clearance, d / len, len))
        {
            if (hit.collider == null || hit.collider.isTrigger) continue;
            Rock rock = hit.collider.GetComponentInParent<Rock>();
            if (rock != null && rock.isWall) return false;
        }
        return true;
    }

    // A small binary min-heap of cell indexes by priority (stale entries are skipped by the caller)
    class MinHeap
    {
        readonly List<int> items = new List<int>();
        readonly List<float> keys = new List<float>();
        public int Count => items.Count;

        public void Push(int item, float key)
        {
            items.Add(item); keys.Add(key);
            int i = items.Count - 1;
            while (i > 0)
            {
                int up = (i - 1) / 2;
                if (keys[up] <= keys[i]) break;
                Swap(i, up);
                i = up;
            }
        }

        public int Pop()
        {
            int top = items[0];
            int last = items.Count - 1;
            Swap(0, last);
            items.RemoveAt(last); keys.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, best = i;
                if (l < items.Count && keys[l] < keys[best]) best = l;
                if (r < items.Count && keys[r] < keys[best]) best = r;
                if (best == i) break;
                Swap(i, best);
                i = best;
            }
            return top;
        }

        void Swap(int a, int b)
        {
            (items[a], items[b]) = (items[b], items[a]);
            (keys[a], keys[b]) = (keys[b], keys[a]);
        }
    }
}
