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
// Positions are Unity world units. JsonUtility can't read the nested arrays, hence the small parser.
public class LevelData
{
    public struct Item
    {
        public string type;   // prefab name: Gold_Small, Gold_Medium, Gold_Large, Diamond, Rock, Life
        public Vector2 position;
    }

    public Vector2 pigStart = new Vector2(0f, -1f);
    public readonly List<Vector2> hooks = new List<Vector2>();
    public readonly List<Item> items = new List<Item>();
    public readonly Dictionary<string, double> parameters = new Dictionary<string, double>();
    public readonly Dictionary<string, object> info = new Dictionary<string, object>();   // the "level" summary

    public static LevelData Parse(string json)
    {
        var root = LevelJson.Parse(json) as Dictionary<string, object>;
        if (root == null) throw new System.FormatException("A level file must be a JSON object");

        var level = new LevelData();
        if (root.TryGetValue("pig_start", out object pig)) level.pigStart = ToVector(pig);
        if (root.TryGetValue("hooks", out object hooks))
            foreach (object h in (List<object>)hooks) level.hooks.Add(ToVector(h));
        if (level.hooks.Count == 0) level.hooks.Add(new Vector2(0f, 3.4f));
        if (root.TryGetValue("items", out object items))
        {
            foreach (object o in (List<object>)items)
            {
                var it = (Dictionary<string, object>)o;
                level.items.Add(new Item
                {
                    type = (string)it["type"],
                    position = new Vector2(ToFloat(it["x"]), ToFloat(it["y"])),
                });
            }
        }
        if (root.TryGetValue("params", out object ps)) level.AddParameters(ps);
        if (root.TryGetValue("skills", out object skills)) level.AddParameters(skills);
        if (root.TryGetValue("level", out object info) && info is Dictionary<string, object> dict)
            foreach (var kv in dict) level.info[kv.Key] = kv.Value;
        return level;
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
