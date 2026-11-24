using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Art: rocks and walls as flat 2D cut-paper stones: two or three torn paper layers seen from straight
// above, like the set's other cut-outs (Resources/Art/Objects/Stone1..9, painted with Meshy using the
// cover and the character sheet as style references, cut with slice_sheet.py). Each has registered boil
// frames (Stone*_b1/_b2, boil_frames.py), so PaperShadows re-lays its paper surface on threes like the
// gold: a stop-motion stone, never a still image.
// The hexagon (Assets/Sprites/HexagonFlatTop) was only a placeholder for level design; every rock still
// drawn with it gets a stone, picked and mirrored from its position so a layout always looks the same.
//   - Art only: loose rocks keep their fitted cut-out; sealed walls get independent paper stones
//     packed by path distance. Nothing here changes a Rock field, collider or gameplay transform.
//     Stones/shadows live under PaperRocks' own FrostIgnore2D root; covered rocks hide via alpha 0.
//   - Same meaning: the stones are light grey, so the renderer tint (green = rhino-proof, map area
//     colours) still colours them. Shards of a broken rock reuse its sprite, so they are stone too.
//     Packed stones hold their boil and blend 60% ice tint while any covered rock is frozen.
//   - Walls keep a small paper gap: loose drawings fit the nearest centre; sealed walls use path packing.
//     Walls only get the chunky stones (the long oval and slab, Stone4 and Stone8, are kept for loose rocks).
public class PaperRocks : MonoBehaviour
{
    const string Placeholder = "HexagonFlatTop";
    const float DenseSpacing = 0.48f, FillerMinimum = 0.40f, Gap = 0.04f;
    static readonly string[] Chunky = { "Stone1", "Stone2", "Stone3", "Stone5", "Stone6", "Stone7", "Stone9" };
    static readonly string[] Any = { "Stone1", "Stone2", "Stone3", "Stone4", "Stone5", "Stone6", "Stone7", "Stone8", "Stone9" };
    static readonly Vector3 ShadowOffset = new Vector3(0.05f, -0.075f, 0f);
    static PaperRocks instance;
    Sprite[] chunky, any;
    float nextScan;
    readonly Dictionary<int, WallScene> walls = new Dictionary<int, WallScene>();

    sealed class WallScene
    {
        public readonly List<Chain> chains = new List<Chain>();
    }
    sealed class Node
    {
        public Rock rock;
        public SpriteRenderer look;
        public Vector3 at;
        public float width;
        public int id;
        public bool active;
        public readonly List<Node> neighbours = new List<Node>();
    }
    sealed class Path
    {
        public readonly List<Node> nodes = new List<Node>();
        public bool closed;
    }
    sealed class Chain
    {
        public readonly List<Node> nodes = new List<Node>();
        public readonly List<Path> paths = new List<Path>();
        public readonly List<Drawing> drawings = new List<Drawing>();
        public GameObject root;
    }
    sealed class Drawing
    {
        public SpriteRenderer stone, shadow;
        public Sprite[] frames;
        public Node[] covers;
        public int seed;
        public Color normalTint;
        public bool frozen;
        public int lastFrozenBoilFrame = int.MinValue;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("Paper Rocks");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PaperRocks>();
        SceneManager.sceneLoaded += instance.SceneLoaded;
        SceneManager.sceneUnloaded += instance.SceneUnloaded;
    }
    void SceneLoaded(Scene scene, LoadSceneMode mode) { nextScan = 0f; }
    void SceneUnloaded(Scene scene)
    {
        if (!walls.TryGetValue(scene.handle, out WallScene wall)) return;
        foreach (Chain chain in wall.chains) Retire(chain);
        walls.Remove(scene.handle);
    }
    static Sprite[] Load(string[] names)
    {
        var sprites = new Sprite[names.Length];
        for (int i = 0; i < names.Length; i++)
            if ((sprites[i] = ArtLibrary.Get("Objects/" + names[i])) == null) return null;
        return sprites;
    }
    void Start()
    {
        chunky = Load(Chunky);
        any = Load(Any);
        if (chunky == null || any == null) enabled = false;
    }
    void LateUpdate()
    {
        // All frame-path collections and boil frames are cached. Only discovery/rebuilds on the
        // half-second clock allocate. Query covered renderers once per boil tick: the Renderer
        // overload is a dictionary lookup, and the independent art root ignores automatic frost.
        int boilFrame = StopMotion.BoilFrame;
        foreach (WallScene wall in walls.Values)
            foreach (Chain chain in wall.chains)
                foreach (Drawing drawing in chain.drawings)
                {
                    if (drawing.lastFrozenBoilFrame != boilFrame)
                    {
                        drawing.lastFrozenBoilFrame = boilFrame;
                        bool frozen = false;
                        foreach (Node node in drawing.covers)
                            if (node.rock != null && node.look != null && FrostField2D.IsFrozen(node.look)) { frozen = true; break; }
                        if (drawing.frozen != frozen)
                        {
                            drawing.frozen = frozen;
                            // Restore the stored tint on thaw; never compound ice or tint the shadow.
                            drawing.stone.color = frozen ? Color.Lerp(drawing.normalTint, new Color(0.72f, 0.9f, 1f, 1f), 0.6f) : drawing.normalTint;
                        }
                    }
                    if (drawing.frozen || drawing.frames == null || drawing.frames.Length == 0) continue;
                    Sprite frame = drawing.frames[(boilFrame * 2 + drawing.seed) % drawing.frames.Length];
                    if (drawing.stone.sprite == frame) continue;
                    drawing.stone.sprite = drawing.shadow.sprite = frame;
                }
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 0.5f;
        foreach (WallScene wall in walls.Values)
            foreach (Chain chain in wall.chains)
            {
                bool changed = false;
                foreach (Node node in chain.nodes)
                {
                    bool active = node.rock != null && node.look != null && node.rock.gameObject.activeInHierarchy;
                    if (node.active != active) { node.active = active; changed = true; }
                }
                if (changed) Repack(chain);
            }

        Rock[] rocks = FindObjectsByType<Rock>(FindObjectsSortMode.None);
        System.Array.Sort(rocks, CompareRocks);
        var batches = new Dictionary<int, List<Node>>();
        foreach (Rock rock in rocks)
        {
            SpriteRenderer look = rock.GetComponent<SpriteRenderer>();
            if (look == null || look.sprite == null || look.sprite.name.Replace(" ", "") != Placeholder) continue;
            Vector2 p = rock.transform.position;
            // Preserve the old individual hash, pool, flip and fitted ScaledBoil exactly.
            int rawHash = Mathf.RoundToInt(p.x * 7.13f) * 73856093 ^ Mathf.RoundToInt(p.y * 5.71f) * 19349663;
            int hash = rawHash == int.MinValue ? 0 : Mathf.Abs(rawHash);
            float scale = Mathf.Abs(rock.transform.lossyScale.x);
            // Measured from rock positions, not a physics query: walls built and scaled this frame have not
            // synced their colliders yet, and a missed neighbour drew the hub's stones full size, overlapping.
            float nearest = NearestCentre(rock, rocks);
            bool hasNeighbour = nearest <= 1.05f;
            Sprite[] pool = rock.isWall || hasNeighbour ? chunky : any;
            float factor = scale > 0f && nearest <= 1.2f * scale ? Mathf.Min(1f, 0.96f * nearest / scale) : 1f;
            // Green (MapHub.GreenRock) marks the unrevealed levels' set: one stone shape, one size, no flip,
            // so they read as a family (hidden portals use the same in PaperPortalStones).
            bool green = rock.RhinoProof;
            // The hub is the Evil Meowmeow's built trap: its rocks look picked, every one the same size. Its grid
            // spacing is uniform, so the spacing fit above already gives one size everywhere (the hexagons
            // overlapped by design, so a size taken from the rock's own scale would overlap).
            // Green tiles (unrevealed levels) share one shape on top of that.
            look.sprite = ArtLibrary.ScaledBoil(green ? chunky[0] : pool[hash % pool.Length], factor);
            look.flipX = !green && (hash / 7 & 1) == 1;
            int scene = rock.gameObject.scene.handle;
            // Classify each placeholder once: conversion itself is the marker. No retained loose
            // records or growing retry list in scenes without walls. Later spawns stay individual
            // after the first eligible batch; scenes built over several scans intentionally commit
            // to the first scan containing eligible placeholders.
            if (walls.ContainsKey(scene) || !rock.isWall || scale <= 0f ||
                IsHub(rock) ||   // the hub's multi-row honeycomb walls: path packing assumes single-file walls
                rock.GetComponent<MapRockDamage>() != null || NearestCentre(rock, rocks) >= DenseSpacing) continue;
            if (!batches.TryGetValue(scene, out List<Node> batch)) batches.Add(scene, batch = new List<Node>());
            batch.Add(new Node { rock = rock, look = look, at = rock.transform.position, width = scale, active = true, id = batch.Count });
        }
        foreach (var batch in batches)
        {
            var wall = new WallScene();
            walls.Add(batch.Key, wall);
            BuildChains(wall, batch.Value);
        }
    }

    static int CompareRocks(Rock a, Rock b)
    {
        int order = a.transform.position.x.CompareTo(b.transform.position.x);
        if (order != 0) return order;
        order = a.transform.position.y.CompareTo(b.transform.position.y);
        return order != 0 ? order : a.GetInstanceID().CompareTo(b.GetInstanceID());
    }
    static int CompareNodes(Node a, Node b) { return a.id.CompareTo(b.id); }
    static ulong Edge(Node a, Node b)
    {
        return ((ulong)(uint)Mathf.Min(a.id, b.id) << 32) | (uint)Mathf.Max(a.id, b.id);
    }
    void BuildChains(WallScene wall, List<Node> nodes)
    {
        // Input is x/y sorted; neighbour lists inherit that order, including equal-distance ties.
        for (int i = 0; i < nodes.Count; i++)
            for (int j = i + 1; j < nodes.Count; j++)
                if (((Vector2)(nodes[i].at - nodes[j].at)).sqrMagnitude < DenseSpacing * DenseSpacing)
                { nodes[i].neighbours.Add(nodes[j]); nodes[j].neighbours.Add(nodes[i]); }
        var seen = new HashSet<Node>();
        foreach (Node first in nodes)
        {
            if (!seen.Add(first)) continue;
            var chain = new Chain();
            chain.nodes.Add(first);
            for (int i = 0; i < chain.nodes.Count; i++)
                foreach (Node neighbour in chain.nodes[i].neighbours)
                    if (seen.Add(neighbour)) chain.nodes.Add(neighbour);
            chain.nodes.Sort(CompareNodes);
            BuildPaths(chain);
            wall.chains.Add(chain);
            foreach (Node node in chain.nodes)
            {
                // Alpha only, once on admission. RGB and renderer eligibility carry gameplay and
                // frost meaning; PaperShadows automatically multiplies its own alpha by this one.
                Color tint = node.look.color; tint.a = 0f; node.look.color = tint;
            }
            Repack(chain);
        }
    }
    static void BuildPaths(Chain chain)
    {
        var used = new HashSet<ulong>();
        // An edge walk avoids exponential longest-path searches at junctions. Prefer an end,
        // then a junction, then the first loop vertex, always x/y sorted. Continue along the
        // nearest UNUSED edge (x/y tie); remaining branches become their own sub-paths.
        // Each edge is drawn once. Keeping these original paths makes a missing rock a cut,
        // rather than allowing a later nearest-neighbour search to paint across its hole.
        while (true)
        {
            Node start = null;
            for (int pass = 0; pass < 3 && start == null; pass++)
                foreach (Node node in chain.nodes)
                {
                    if ((pass == 0 && node.neighbours.Count != 1) || (pass == 1 && node.neighbours.Count == 2)) continue;
                    bool available = node.neighbours.Count == 0 && chain.paths.Count == 0;
                    foreach (Node other in node.neighbours) if (!used.Contains(Edge(node, other))) { available = true; break; }
                    if (available) { start = node; break; }
                }
            if (start == null) break;
            var path = new Path();
            path.nodes.Add(start);
            Node current = start;
            while (true)
            {
                Node next = null;
                float best = float.PositiveInfinity;
                foreach (Node other in current.neighbours)
                {
                    if (used.Contains(Edge(current, other))) continue;
                    float distance = ((Vector2)(current.at - other.at)).sqrMagnitude;
                    if (distance < best) { best = distance; next = other; }
                }
                if (next == null) break;
                used.Add(Edge(current, next));
                if (next == start) { path.closed = true; break; }
                path.nodes.Add(next);
                current = next;
            }
            chain.paths.Add(path);
        }
    }
    void Retire(Chain chain)
    {
        // Destroy is deferred: hide the owned root now so a rebuild cannot draw both generations.
        if (chain.root != null) { chain.root.SetActive(false); Destroy(chain.root); }
        chain.drawings.Clear();
    }
    void Repack(Chain chain)
    {
        Retire(chain);
        chain.root = new GameObject("Packed wall paper");
        chain.root.transform.SetParent(transform, false);
        chain.root.AddComponent<FrostIgnore2D>();
        foreach (Path path in chain.paths)
        {
            int missing = -1;
            for (int i = 0; i < path.nodes.Count; i++) if (!path.nodes[i].active) { missing = i; break; }
            if (missing < 0) { Pack(chain, path.nodes, path.closed); continue; }
            // Rotate a broken loop to a missing vertex before splitting, retaining the surviving
            // run across the original loop seam. Reactivation rebuilds the same original path.
            var run = new List<Node>();
            for (int step = 0; step < path.nodes.Count; step++)
            {
                int index = path.closed ? (missing + 1 + step) % path.nodes.Count : step;
                Node node = path.nodes[index];
                if (node.active) run.Add(node);
                else if (run.Count > 0) { Pack(chain, run, false); run.Clear(); }
            }
            if (run.Count > 0) Pack(chain, run, false);
        }
    }

    static uint PositionHash(Vector2 p)
    {
        unchecked
        {
            uint h = (uint)Mathf.RoundToInt(p.x * 1000f) * 73856093u ^
                (uint)Mathf.RoundToInt(p.y * 1000f) * 19349663u ^ 0x9e3779b9u;
            h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu;
            return h ^ (h >> 16);
        }
    }
    static float HashedDiameter(uint hash)
    {
        float t = ((hash >> 8) & 0xffffu) / 65535f;
        uint bucket = hash % 4u;
        if (bucket == 0u) return Mathf.Lerp(1.00f, 1.35f, t);
        return bucket < 3u ? Mathf.Lerp(0.60f, 0.95f, t) : Mathf.Lerp(FillerMinimum, 0.55f, t);
    }
    static Vector3 PointAt(List<Vector3> points, float[] arc, float distance)
    {
        for (int i = 1; i < points.Count; i++)
            if (distance <= arc[i])
                return Vector3.Lerp(points[i - 1], points[i], Mathf.InverseLerp(arc[i - 1], arc[i], distance));
        return points[points.Count - 1];
    }
    void Pack(Chain chain, List<Node> nodes, bool closed)
    {
        var points = new List<Vector3>();
        // A rock's old world width is its scale. Half-width end caps preserve endpoint coverage.
        // Do not extend junctions: their other paths already meet here. Cut endpoints get caps
        // too, leaving a real missing-rock gap. Isolated survivors get a single width-long path.
        if (nodes.Count == 1)
        {
            // Keep an isolated survivor aligned to its original wall, including vertical walls.
            Vector3 direction = nodes[0].neighbours.Count == 0 ? Vector3.right :
                (nodes[0].neighbours[0].at - nodes[0].at).normalized;
            points.Add(nodes[0].at - direction * nodes[0].width * 0.5f);
            points.Add(nodes[0].at + direction * nodes[0].width * 0.5f);
        }
        else
        {
            if (!closed && nodes[0].neighbours.Count <= 2)
                points.Add(nodes[0].at + (nodes[0].at - nodes[1].at).normalized * nodes[0].width * 0.5f);
            foreach (Node node in nodes) points.Add(node.at);
            if (closed) points.Add(nodes[0].at);
            else if (nodes[nodes.Count - 1].neighbours.Count <= 2)
                points.Add(nodes[nodes.Count - 1].at + (nodes[nodes.Count - 1].at - nodes[nodes.Count - 2].at).normalized * nodes[nodes.Count - 1].width * 0.5f);
        }
        var arc = new float[points.Count];
        for (int i = 1; i < points.Count; i++) arc[i] = arc[i - 1] + Vector2.Distance(points[i - 1], points[i]);
        float length = arc[arc.Length - 1];
        if (length <= 0.00001f) return;
        float usable = closed ? Mathf.Max(0f, length - Gap) : length;
        var diameters = new List<float>();
        float cursor = 0f;
        while (cursor < usable)
        {
            float remaining = usable - cursor;
            if (remaining < FillerMinimum && diameters.Count > 0)
            {
                // Distribute the tail equally, capped at 1.45. Include the already reserved gap:
                // the last stone's far edge must meet the end (or leave exactly the loop seam).
                float tail = remaining + Gap;
                for (int pass = 0; pass < diameters.Count && tail > 0.000001f; pass++)
                {
                    int count = 0;
                    foreach (float d in diameters) if (d < 1.45f) count++;
                    if (count == 0) break;
                    float share = tail / count;
                    for (int i = 0; i < diameters.Count; i++)
                    {
                        float grow = Mathf.Min(share, 1.45f - diameters[i]);
                        diameters[i] += grow; tail -= grow;
                    }
                }
                if (tail > 0.00001f)
                {
                    // Rare short paths cannot absorb a tail under the cap. Split the last
                    // diameter plus residue into two legal fillers instead of exceeding 1.45.
                    int last = diameters.Count - 1;
                    float total = diameters[last] + tail - Gap;
                    diameters[last] = total * 0.5f;
                    diameters.Add(total * 0.5f);
                }
                break;
            }
            float diameter = Mathf.Min(HashedDiameter(PositionHash(PointAt(points, arc, cursor))), remaining);
            diameters.Add(diameter);
            cursor += diameter;
            if (cursor >= usable) break;
            // If only a gap remains, absorb it too; don't strand an unpainted terminal sliver.
            if (usable - cursor <= Gap)
            {
                float tail = usable - cursor;
                int last = diameters.Count - 1;
                diameters[last] += tail;
                break;
            }
            cursor += Gap;
        }
        // Cells halfway between original centres define which rocks each arc interval covers.
        // Adjacent cells share boundaries; closed-loop cells wrap across the seam.
        var centres = new float[nodes.Count];
        if (nodes.Count == 1) centres[0] = length * 0.5f;
        else
        {
            centres[0] = closed || nodes[0].neighbours.Count > 2 ? 0f : nodes[0].width * 0.5f;
            for (int i = 1; i < nodes.Count; i++) centres[i] = centres[i - 1] + Vector2.Distance(nodes[i - 1].at, nodes[i].at);
        }
        cursor = 0f;
        foreach (float diameter in diameters)
        {
            float middle = cursor + diameter * 0.5f;
            var covers = new List<Node>();
            Node nearest = nodes[0];
            float best = float.PositiveInfinity;
            for (int i = 0; i < nodes.Count; i++)
            {
                float delta = Mathf.Abs(centres[i] - middle);
                if (closed) delta = Mathf.Min(delta, length - delta);
                if (delta < best) { best = delta; nearest = nodes[i]; }
                float left = i == 0 ? (closed ? -(length - centres[nodes.Count - 1]) * 0.5f : 0f) : (centres[i - 1] + centres[i]) * 0.5f;
                float right = i == nodes.Count - 1 ? (closed ? (centres[i] + length) * 0.5f : length) : (centres[i] + centres[i + 1]) * 0.5f;
                bool covered = right >= cursor && left <= cursor + diameter;
                if (closed) covered |= right + length >= cursor && left + length <= cursor + diameter;
                if (covered && !covers.Contains(nodes[i])) covers.Add(nodes[i]);
            }
            if (covers.Count == 0) covers.Add(nearest);
            CreateDrawing(chain, PointAt(points, arc, middle), diameter, nearest, covers);
            cursor += diameter + Gap;
        }
    }
    void CreateDrawing(Chain chain, Vector3 at, float diameter, Node nearest, List<Node> covers)
    {
        covers.Sort(CompareNodes);
        uint hash = PositionHash(at);
        Sprite sprite = chunky[hash % (uint)chunky.Length];
        Color tint = new Color(0f, 0f, 0f, 1f);
        foreach (Node node in covers) { Color c = node.look.color; tint.r += c.r; tint.g += c.g; tint.b += c.b; }
        tint.r /= covers.Count; tint.g /= covers.Count; tint.b /= covers.Count;
        var go = new GameObject("Wall stone");
        go.transform.SetParent(chain.root.transform, false);
        go.transform.position = at;
        float scale = diameter / sprite.bounds.size.x;
        go.transform.localScale = new Vector3(scale, scale, scale);
        var stone = go.AddComponent<SpriteRenderer>();
        stone.sprite = sprite; stone.color = tint; stone.flipX = (hash / 7u & 1u) == 1u;
        // Sorting/material follow the closest covered rock when a path crosses authored layers.
        stone.sharedMaterial = nearest.look.sharedMaterial;
        stone.sortingLayerID = nearest.look.sortingLayerID; stone.sortingOrder = nearest.look.sortingOrder;
        var shadowObject = new GameObject("Wall stone shadow");
        shadowObject.transform.SetParent(chain.root.transform, false);
        shadowObject.transform.position = at + ShadowOffset;
        shadowObject.transform.localScale = go.transform.localScale;
        var shadow = shadowObject.AddComponent<SpriteRenderer>();
        shadow.sprite = sprite; shadow.flipX = stone.flipX;
        shadow.sharedMaterial = stone.sharedMaterial;
        shadow.color = new Color(0.12f, 0.08f, 0.06f, 0.55f);
        shadow.sortingLayerID = stone.sortingLayerID; shadow.sortingOrder = stone.sortingOrder - 1;
        chain.drawings.Add(new Drawing { stone = stone, shadow = shadow, frames = ArtLibrary.BoilFrames(sprite),
            seed = (int)(hash & 0xffu), covers = covers.ToArray(), normalTint = tint });
    }
    // The world-map hub (MapHub is added by LevelLoader at runtime, possibly after the first scan, so the
    // scene itself is the reliable sign).
    static bool IsHub(Rock rock) => MapHub.Active != null || rock.gameObject.scene.name == "world_map";

    static float NearestCentre(Rock rock, Rock[] rocks)
    {
        float nearestSquared = float.PositiveInfinity;
        foreach (Rock other in rocks)
        {
            if (other == rock || other.gameObject.scene != rock.gameObject.scene) continue;
            Vector2 delta = other.transform.position - rock.transform.position;
            nearestSquared = Mathf.Min(nearestSquared, delta.sqrMagnitude);
        }
        return Mathf.Sqrt(nearestSquared);
    }
    void OnDestroy()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneUnloaded -= SceneUnloaded;
    }
}
