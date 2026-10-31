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
// "block" counts hooks bounced off the pig, "steal" counts treasure knocked off a hook. A stage's
// items are spawned when it starts, and again whenever none are left. Clearing the last stage wins.
// Positions are Unity world units. JsonUtility can't read the nested arrays, hence the small parser.
public class LevelData
{
    public struct Item
    {
        public string type;   // prefab name: Gold_Small, Gold_Medium, Gold_Large, Diamond, Rock, Life
        public Vector2 position;
        public float scale;   // 0 = leave the prefab's scale (grid rocks use tile_size)
    }

    public class Stage
    {
        public string guide = "";
        public string goal = "";
        public int count;
        public float pigSpeed = -1f;   // < 0: the level's pig_speed
        public readonly List<Item> items = new List<Item>();
    }

    // One placed thing in a grid maze (pig, gold, miner, gate, one-way).
    public class MazeEntity
    {
        public string id = "";
        public string type = "";
        public string hook = "";      // Red = attack pig, Yellow = grab treasure
        public string dir = "";       // OneWay: Left / Right / Up / Down
        public string target = "";    // Gate / LevelPortal → WorldMap node id (battle_01)
        public string requires = "";  // LevelPortal: previous battle id that reveals this stone
        public int unlockCost;        // LevelPortal: MapMoney threshold (not spent)
        public bool autoFire;
        public float hookSpeed;
        public float windup;
        public int gridX;
        public int gridY;
        public Vector2 worldPos;
    }

    // A trigger rectangle in a grid maze (hint, miner zone, gate check, QTEs).
    public class MazeTrigger
    {
        public string id = "";
        public string type = "";
        public string text = "";
        public float param;
        public string[] targets = new string[0];
        public Vector2 worldCenter;
        public Vector2 worldSize;
    }

    public Vector2 pigStart = new Vector2(0f, -1f);
    public readonly List<Vector2> hooks = new List<Vector2>();
    public readonly List<Item> items = new List<Item>();
    public readonly Dictionary<string, double> parameters = new Dictionary<string, double>();
    public readonly Dictionary<string, object> info = new Dictionary<string, object>();   // the "level" summary
    public string guide = "";
    public readonly List<Stage> stages = new List<Stage>();
    public readonly List<MazeEntity> mazeEntities = new List<MazeEntity>();
    public readonly List<MazeTrigger> mazeTriggers = new List<MazeTrigger>();
    public readonly List<string> creditSources = new List<string>();
    public bool IsMaze;
    public float TileSize = 1f;
    public int GridRows;
    public int EntryCredit;

    public static LevelData Parse(string json)
    {
        var root = LevelJson.Parse(json) as Dictionary<string, object>;
        if (root == null) throw new System.FormatException("A level file must be a JSON object");

        var level = new LevelData();
        if (root.TryGetValue("params", out object ps)) level.AddParameters(ps);
        if (root.TryGetValue("skills", out object skills)) level.AddParameters(skills);
        if (root.TryGetValue("guide", out object guide) && guide is string text) level.guide = text;
        if (root.TryGetValue("pigSpeed", out object pigSpeed) && pigSpeed is double spd)
            level.parameters["pig_speed"] = spd;
        if (root.TryGetValue("tileSize", out object tileSize) && tileSize is double ts && !level.Has("tile_size"))
            level.parameters["tile_size"] = ts;

        // Grid maze levels (Penny tutorial): '#' = Rock, entities list places pig / gold / miners.
        List<object> gridLines = null;
        bool fromGrid = root.TryGetValue("grid", out object gridObj) && gridObj is List<object>;
        if (fromGrid) gridLines = (List<object>)gridObj;
        if (fromGrid) ReadGrid(level, gridLines, root);

        if (root.TryGetValue("pig_start", out object pig)) level.pigStart = ToVector(pig);
        // No "hooks" key: one default hook. An empty list: no miner at all (the eat tutorial).
        // Grid levels already filled hooks from Miner entities, so leave them alone.
        if (root.TryGetValue("hooks", out object hooks))
            foreach (object h in (List<object>)hooks) level.hooks.Add(ToVector(h));
        else if (!fromGrid) level.hooks.Add(new Vector2(0f, 3.4f));
        if (root.TryGetValue("items", out object items)) ReadItems(items, level.items);
        if (root.TryGetValue("stages", out object stages))
            foreach (object s in (List<object>)stages) level.stages.Add(ReadStage((Dictionary<string, object>)s));
        if (root.TryGetValue("level", out object info) && info is Dictionary<string, object> dict)
            foreach (var kv in dict) level.info[kv.Key] = kv.Value;
        return level;
    }

    // ASCII maze: row 0 is the top. World origin is the centre of the grid so it sits in the arena.
    static void ReadGrid(LevelData level, List<object> gridLines, Dictionary<string, object> root)
    {
        if (gridLines == null) return;
        level.IsMaze = true;
        float tile = level.Param("tile_size", 1f);
        level.TileSize = tile;
        int rows = gridLines.Count;
        level.GridRows = rows;
        int cols = 0;
        for (int r = 0; r < rows; r++)
            if (gridLines[r] is string line) cols = Mathf.Max(cols, line.Length);

        float originX = cols * tile * 0.5f;
        float originY = rows * tile * 0.5f;
        level.parameters["arena_w"] = cols * tile;
        level.parameters["arena_h"] = rows * tile;

        for (int row = 0; row < rows; row++)
        {
            if (!(gridLines[row] is string line)) continue;
            for (int col = 0; col < line.Length; col++)
            {
                if (line[col] != '#') continue;
                level.items.Add(new Item
                {
                    type = "Rock",
                    position = GridToWorld(col, row, rows, tile, originX, originY),
                    scale = tile,
                });
            }
        }

        if (root.TryGetValue("creditSources", out object credits) && credits is List<object> creditList)
            foreach (object c in creditList)
                if (c is string id) level.creditSources.Add(id);

        if (root.TryGetValue("entities", out object ents) && ents is List<object> list)
        {
            foreach (object o in list)
            {
                if (!(o is Dictionary<string, object> e)) continue;
                var ent = ReadMazeEntity(e, rows, tile, originX, originY);
                level.mazeEntities.Add(ent);
                switch (ent.type)
                {
                    case "Pig":
                        level.pigStart = ent.worldPos;
                        break;
                    case "Gold":
                        level.items.Add(new Item { type = "Gold_Medium", position = ent.worldPos });
                        break;
                    case "Diamond":
                        level.items.Add(new Item { type = "Diamond", position = ent.worldPos });
                        break;
                    case "Miner":
                        level.hooks.Add(ent.worldPos);
                        break;
                }
            }
        }

        level.EntryCredit = ComputeEntryCredit(level);

        if (root.TryGetValue("triggers", out object trigs) && trigs is List<object> triggerList)
            foreach (object o in triggerList)
                if (o is Dictionary<string, object> t)
                    level.mazeTriggers.Add(ReadMazeTrigger(t, rows, tile, originX, originY));
    }

    static MazeEntity ReadMazeEntity(Dictionary<string, object> e, int rows, float tile, float originX, float originY)
    {
        var ent = new MazeEntity();
        if (e.TryGetValue("id", out object id) && id is string ids) ent.id = ids;
        if (e.TryGetValue("type", out object type) && type is string ts) ent.type = ts;
        if (e.TryGetValue("hook", out object hook) && hook is string hs) ent.hook = hs;
        if (e.TryGetValue("dir", out object dir) && dir is string ds) ent.dir = ds;
        if (e.TryGetValue("target", out object target) && target is string tgs) ent.target = tgs;
        if (e.TryGetValue("requires", out object req) && req is string rs) ent.requires = rs;
        if (e.TryGetValue("unlockCost", out object uc) && uc is double ud) ent.unlockCost = (int)ud;
        if (e.TryGetValue("autoFire", out object af) && af is bool ab) ent.autoFire = ab;
        if (e.TryGetValue("hookSpeed", out object speed) && speed is double sd) ent.hookSpeed = (float)sd;
        if (e.TryGetValue("windup", out object wind) && wind is double wd) ent.windup = (float)wd;
        ent.gridX = e.TryGetValue("x", out object xv) && xv is double xd ? (int)xd : 0;
        ent.gridY = e.TryGetValue("y", out object yv) && yv is double yd ? (int)yd : 0;
        ent.worldPos = GridToWorld(ent.gridX, ent.gridY, rows, tile, originX, originY);
        return ent;
    }

    static MazeTrigger ReadMazeTrigger(Dictionary<string, object> t, int rows, float tile, float originX, float originY)
    {
        var trig = new MazeTrigger();
        if (t.TryGetValue("id", out object id) && id is string ids) trig.id = ids;
        if (t.TryGetValue("type", out object type) && type is string ts) trig.type = ts;
        if (t.TryGetValue("text", out object text) && text is string tx) trig.text = tx;
        if (t.TryGetValue("param", out object param) && param is double pd) trig.param = (float)pd;
        int x = t.TryGetValue("x", out object xv) && xv is double xd ? (int)xd : 0;
        int y = t.TryGetValue("y", out object yv) && yv is double yd ? (int)yd : 0;
        int w = t.TryGetValue("w", out object wv) && wv is double wd ? (int)wd : 1;
        int h = t.TryGetValue("h", out object hv) && hv is double hd ? (int)hd : 1;
        trig.worldCenter = GridToWorld(x + (w - 1) * 0.5f, y + (h - 1) * 0.5f, rows, tile, originX, originY);
        trig.worldSize = new Vector2(w, h) * tile;
        if (t.TryGetValue("targets", out object targets) && targets is List<object> list)
        {
            trig.targets = new string[list.Count];
            for (int i = 0; i < list.Count; i++)
                trig.targets[i] = list[i] is string s ? s : "";
        }
        return trig;
    }

    // G1/G2/G3 are medium gold ($100), D2 is a diamond ($600) — matches the prefab values.
    static int ComputeEntryCredit(LevelData level)
    {
        var typeById = new Dictionary<string, string>();
        foreach (MazeEntity e in level.mazeEntities) typeById[e.id] = e.type;
        int sum = 0;
        foreach (string id in level.creditSources)
        {
            if (!typeById.TryGetValue(id, out string type)) continue;
            if (type == "Gold") sum += 100;
            else if (type == "Diamond") sum += 600;
        }
        return sum;
    }

    static Vector2 GridToWorld(float col, float row, int rows, float tile, float originX, float originY)
    {
        float x = (col + 0.5f) * tile - originX;
        float y = ((rows - 1 - row) + 0.5f) * tile - originY;
        return new Vector2(x, y);
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
                scale = it.TryGetValue("scale", out object sc) ? ToFloat(sc) : 0f,
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
