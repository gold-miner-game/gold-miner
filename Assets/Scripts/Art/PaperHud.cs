using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// Art: HUD numbers are paper labels that get stamped. When money, lives or a counter changes, its
// label pops: big, a bit less, settled (three poses on twos, StopMotion). Clocks (texts with ':')
// tick every second and are left alone. Only the label's scale moves, around its own pivot, and it
// always returns to the exact scale it had, so layouts never shift.
public class PaperHud : MonoBehaviour
{
    static readonly float[] Stamp = { 1.28f, 1.12f, 1f };

    class Label
    {
        public TMP_Text text;
        public string last;
        public Vector3 rest;
        public int poppedAt = -100;
        public int restoredFor = -100;   // the pop whose rest scale was put back
    }

    static PaperHud instance;
    readonly List<Label> labels = new List<Label>();
    readonly HashSet<int> known = new HashSet<int>();
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("Paper Hud");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PaperHud>();
        SceneManager.sceneLoaded += (s, m) => { if (instance != null) { instance.labels.Clear(); instance.known.Clear(); instance.nextScan = 0f; } };
    }

    static bool Counts(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Contains(":")) return false;
        foreach (char c in s) if (char.IsDigit(c)) return true;
        return false;
    }

    void LateUpdate()
    {
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 0.5f;
            foreach (TMP_Text t in FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
            {
                if (t.canvas == null || !known.Add(t.GetInstanceID())) continue;
                labels.Add(new Label { text = t, last = t.text, rest = t.rectTransform.localScale });
            }
        }
        int frame = Mathf.FloorToInt(Time.unscaledTime * StopMotion.Twos);
        for (int i = labels.Count - 1; i >= 0; i--)
        {
            Label l = labels[i];
            if (l.text == null) { labels.RemoveAt(i); continue; }
            string now = l.text.text;
            if (now != l.last)
            {
                if (Counts(now) && Counts(l.last)) l.poppedAt = frame;
                l.last = now;
            }
            int pose = frame - l.poppedAt;
            if (pose < 0) continue;
            if (pose >= Stamp.Length)
            {
                // settled: put the rest scale back once, even if a long frame skipped the last pose
                if (l.restoredFor != l.poppedAt) { l.text.rectTransform.localScale = l.rest; l.restoredFor = l.poppedAt; }
                continue;
            }
            l.text.rectTransform.localScale = l.rest * Stamp[pose];
        }
    }
}
