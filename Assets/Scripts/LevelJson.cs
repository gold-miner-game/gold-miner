using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

// A level file from tools/sim (gold_miner_heuristic.py levels), e.g.
//   { "pig_start": [0, -1], "hooks": [[0, 3.4], [5, -1.5]],
//     "items": [{"type": "Diamond", "x": 6.2, "y": 0.0}, ...],
//     "params": {"pig_speed": 5, "digest_time": 1.06, ...}, "level": {...},
//     "skills": {"block": true, "block_duration": 0.3, "block_cooldown": 1} }
// "skills" is optional and Unity-only (the simulator doesn't model it); it is read into the same
// parameter table as "params".
// "guide" is optional too: text shown on screen during the level (tutorial levels).
// "stages" (tutorials) is a list of steps played in order, each with its own guide and goal:
//   {"guide": "...", "goal": "block" | "steal", "count": 2, "pig_speed": 0, "items": [...]}
//   goal "none": the stage never ends by itself; something in the level scene wins it (SqueezePhase)
// "block" counts hooks bounced off the pig, "steal" counts treasure knocked off a hook. A stage's
// items are spawned when it starts, and again whenever none are left. Clearing the last stage wins.
// "grid" (the world-map hub, after Penny's maze on feature/penny-level-map) makes it a maze: a list of
// strings, row 0 at the top, one character per tile of "tile_size" world units:
//   # wall   R / Y / G red / yellow / green wall (the attack / survival / compete areas)
//   P player start   < > ^ v one-way tile (passable only
//   that way)   anything else floor
// plus "portals": [{"target": "battle_01", "x": col, "y": row, "hidden": true}], each a WorldMap
// node id. A hidden portal is a rock, coloured like the walls around it, until its node unlocks. And "labels":
// [{"text": "ATTACK", "x": col, "y": row}], words on the floor. A grid level has no hooks unless it
// lists some.
// Positions are Unity world units. JsonUtility can't read the nested arrays, hence the small parser.
public class LevelData
{
    public struct Item
    {
        public string type;   // prefab name: Gold_Small, Gold_Medium, Gold_Large, Diamond, Rock, Life
        public Vector2 position;
    }

    public class Stage
    {
        public string guide = "";
        public string goal = "";
        public int count;
        public float pigSpeed = -1f;   // < 0: the level's pig_speed
        public readonly List<Item> items = new List<Item>();
    }

    public Vector2 pigStart = new Vector2(0f, -1f);
    public readonly List<Vector2> hooks = new List<Vector2>();
    public readonly List<Item> items = new List<Item>();
    public readonly Dictionary<string, double> parameters = new Dictionary<string, double>();
    public readonly Dictionary<string, object> info = new Dictionary<string, object>();   // the "level" summary
    public string guide = "";
    public readonly List<Stage> stages = new List<Stage>();

    // Grid maze (the world-map hub)
    public struct Portal { public string target; public Vector2 position; public bool hidden; public char stone; }
    public struct ColoredWall { public Vector2 position; public char color; }   // R red, Y yellow, G green
    public struct Label { public string text; public Vector2 position; }
    public struct OneWay { public Vector2 position; public Vector2 dir; }
    public bool IsMaze;
    public float TileSize = 1f;
    public Vector2 MazeSize;   // world units
    public readonly List<Vector2> walls = new List<Vector2>();
    public readonly List<ColoredWall> coloredWalls = new List<ColoredWall>();
    public readonly List<OneWay> oneWays = new List<OneWay>();
    public readonly List<Portal> portals = new List<Portal>();
    public readonly List<Label> labels = new List<Label>();

    public static LevelData Parse(string json)
    {
        var root = LevelJson.Parse(json) as Dictionary<string, object>;
        if (root == null) throw new System.FormatException("A level file must be a JSON object");

        var level = new LevelData();
        if (root.TryGetValue("pig_start", out object pig)) level.pigStart = ToVector(pig);
        // No "hooks" key: one default hook. An empty list: no miner at all (the eat tutorial)
        if (root.TryGetValue("hooks", out object hooks))
            foreach (object h in (List<object>)hooks) level.hooks.Add(ToVector(h));
        else level.hooks.Add(new Vector2(0f, 3.4f));
        if (root.TryGetValue("items", out object items)) ReadItems(items, level.items);
        if (root.TryGetValue("params", out object ps)) level.AddParameters(ps);
        if (root.TryGetValue("skills", out object skills)) level.AddParameters(skills);
        if (root.TryGetValue("guide", out object guide) && guide is string text) level.guide = text;
        if (root.TryGetValue("grid", out object grid) && grid is List<object> rows)
        {
            ReadGrid(level, rows, root);
            if (!root.ContainsKey("hooks")) level.hooks.Clear();   // the hub has no miner
        }
        if (root.TryGetValue("stages", out object stages))
            foreach (object s in (List<object>)stages) level.stages.Add(ReadStage((Dictionary<string, object>)s));
        if (root.TryGetValue("level", out object info) && info is Dictionary<string, object> dict)
            foreach (var kv in dict) level.info[kv.Key] = kv.Value;
        return level;
    }

    // The grid is centred on the world origin, so it sits in the middle of the arena
    static void ReadGrid(LevelData level, List<object> rows, Dictionary<string, object> root)
    {
        float tile = level.Param("tile_size", 0.5f);
        int cols = 0;
        foreach (object r in rows) if (r is string line) cols = Mathf.Max(cols, line.Length);
        level.IsMaze = true;
        level.TileSize = tile;
        level.MazeSize = new Vector2(cols, rows.Count) * tile;

        for (int row = 0; row < rows.Count; row++)
        {
            if (!(rows[row] is string line)) continue;
            for (int col = 0; col < line.Length; col++)
            {
                Vector2 at = GridToWorld(level, col, row);
                switch (line[col])
                {
                    case '#': level.walls.Add(at); break;
                    case 'R': case 'Y': case 'G': level.coloredWalls.Add(new ColoredWall { position = at, color = line[col] }); break;
                    case 'P': level.pigStart = at; break;
                    case '<': level.oneWays.Add(new OneWay { position = at, dir = Vector2.left }); break;
                    case '>': level.oneWays.Add(new OneWay { position = at, dir = Vector2.right }); break;
                    case '^': level.oneWays.Add(new OneWay { position = at, dir = Vector2.up }); break;
                    case 'v': level.oneWays.Add(new OneWay { position = at, dir = Vector2.down }); break;
                }
            }
        }

        if (root.TryGetValue("portals", out object portals) && portals is List<object> list)
            foreach (object o in list)
            {
                if (!(o is Dictionary<string, object> p) || !(p.TryGetValue("target", out object t) && t is string target)) continue;
                int x = p.TryGetValue("x", out object xv) ? Mathf.RoundToInt(ToFloat(xv)) : 0;
                int y = p.TryGetValue("y", out object yv) ? Mathf.RoundToInt(ToFloat(yv)) : 0;
                level.portals.Add(new Portal
                {
                    target = target,
                    position = GridToWorld(level, x, y),
                    hidden = p.TryGetValue("hidden", out object h) && h is bool hb && hb,
                    stone = WallAround(rows, x, y),
                });
            }

        if (root.TryGetValue("labels", out object labels) && labels is List<object> words)
            foreach (object o in words)
            {
                if (!(o is Dictionary<string, object> w) || !(w.TryGetValue("text", out object t) && t is string text)) continue;
                float x = w.TryGetValue("x", out object xv) ? ToFloat(xv) : 0f;
                float y = w.TryGetValue("y", out object yv) ? ToFloat(yv) : 0f;
                level.labels.Add(new Label { text = text, position = GridToWorld(level, x, y) });
            }
    }

    // The coloured wall (R / Y / G) among the 8 tiles around (col, row), else '#'
    static char WallAround(List<object> rows, int col, int row)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int r = row + dy, x = col + dx;
                if ((dx != 0 || dy != 0) && r >= 0 && r < rows.Count && rows[r] is string line && x >= 0 && x < line.Length
                    && (line[x] == 'R' || line[x] == 'Y' || line[x] == 'G'))
                    return line[x];
            }
        return '#';
    }

    // Tile centre of (col, row); row 0 is the top
    public static Vector2 GridToWorld(LevelData level, float col, float row)
    {
        float tile = level.TileSize;
        return new Vector2((col + 0.5f) * tile - level.MazeSize.x * 0.5f,
                           level.MazeSize.y * 0.5f - (row + 0.5f) * tile);
    }

    static void ReadItems(object list, List<Item> into)
    {
        foreach (object o in (List<object>)list)
        {
            var it = (Dictionary<string, object>)o;
            into.Add(new Item
            {
                type = (string)it["type"],
                position = new Vector2(ToFloat(it["x"]), ToFloat(it["y"])),
            });
        }
    }

    static Stage ReadStage(Dictionary<string, object> s)
    {
        var stage = new Stage();
        if (s.TryGetValue("guide", out object guide) && guide is string text) stage.guide = text;
        if (s.TryGetValue("goal", out object goal) && goal is string g) stage.goal = g;
        if (s.TryGetValue("count", out object count) && count is double c) stage.count = (int)c;
        if (s.TryGetValue("pig_speed", out object speed) && speed is double v) stage.pigSpeed = (float)v;
        if (s.TryGetValue("items", out object items)) ReadItems(items, stage.items);
        return stage;
    }

    void AddParameters(object table)
    {
        foreach (var kv in (Dictionary<string, object>)table)
        {
            if (kv.Value is double d) parameters[kv.Key] = d;
            else if (kv.Value is bool b) parameters[kv.Key] = b ? 1.0 : 0.0;
        }
    }

    public float Param(string name, float fallback)
    {
        return parameters.TryGetValue(name, out double v) ? (float)v : fallback;
    }

    public bool Has(string name) => parameters.ContainsKey(name);

    static Vector2 ToVector(object o)
    {
        var list = (List<object>)o;
        return new Vector2(ToFloat(list[0]), ToFloat(list[1]));
    }

    static float ToFloat(object o) => (float)(double)o;
}

// Minimal JSON reader: objects -> Dictionary<string, object>, arrays -> List<object>,
// numbers -> double, plus string, bool and null.
public static class LevelJson
{
    public static object Parse(string text)
    {
        int i = 0;
        object value = ReadValue(text, ref i);
        SkipSpace(text, ref i);
        if (i != text.Length) throw Error(text, i, "unexpected text after the value");
        return value;
    }

    static object ReadValue(string s, ref int i)
    {
        SkipSpace(s, ref i);
        if (i >= s.Length) throw Error(s, i, "unexpected end");
        char c = s[i];
        if (c == '{') return ReadObject(s, ref i);
        if (c == '[') return ReadArray(s, ref i);
        if (c == '"') return ReadString(s, ref i);
        if (Match(s, ref i, "true")) return true;
        if (Match(s, ref i, "false")) return false;
        if (Match(s, ref i, "null")) return null;
        return ReadNumber(s, ref i);
    }

    static Dictionary<string, object> ReadObject(string s, ref int i)
    {
        var obj = new Dictionary<string, object>();
        i++;   // {
        SkipSpace(s, ref i);
        if (i < s.Length && s[i] == '}') { i++; return obj; }
        while (true)
        {
            SkipSpace(s, ref i);
            if (i >= s.Length || s[i] != '"') throw Error(s, i, "expected a key");
            string key = ReadString(s, ref i);
            SkipSpace(s, ref i);
            if (i >= s.Length || s[i] != ':') throw Error(s, i, "expected ':'");
            i++;
            obj[key] = ReadValue(s, ref i);
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == '}') { i++; return obj; }
            throw Error(s, i, "expected ',' or '}'");
        }
    }

    static List<object> ReadArray(string s, ref int i)
    {
        var list = new List<object>();
        i++;   // [
        SkipSpace(s, ref i);
        if (i < s.Length && s[i] == ']') { i++; return list; }
        while (true)
        {
            list.Add(ReadValue(s, ref i));
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == ']') { i++; return list; }
            throw Error(s, i, "expected ',' or ']'");
        }
    }

    static string ReadString(string s, ref int i)
    {
        var sb = new StringBuilder();
        i++;   // opening quote
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '"') return sb.ToString();
            if (c != '\\') { sb.Append(c); continue; }
            if (i >= s.Length) break;
            char e = s[i++];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'u':
                    sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber));
                    i += 4;
                    break;
                default: sb.Append(e); break;   // \" \\ \/
            }
        }
        throw Error(s, i, "unterminated string");
    }

    static double ReadNumber(string s, ref int i)
    {
        int start = i;
        while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
        if (start == i) throw Error(s, i, "unexpected character");
        return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    static bool Match(string s, ref int i, string word)
    {
        if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
        i += word.Length;
        return true;
    }

    static void SkipSpace(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    static System.FormatException Error(string s, int i, string what)
    {
        return new System.FormatException("Level JSON: " + what + " at character " + i);
    }
}
