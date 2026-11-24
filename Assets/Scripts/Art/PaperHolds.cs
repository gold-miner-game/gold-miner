using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Sample after frost reads true geometry; draw AFTER Puppet, undo BEFORE its restore.
// Never write a physics root: only collider/body-free child subtrees and detached art proxies.
[DefaultExecutionOrder(1500)]
public class PaperHolds : MonoBehaviour
{
    sealed class Visual
    {
        public Transform t;
        public Vector3 truth;
        public bool moved;
    }
    sealed class Held
    {
        public Transform t;
        public Treasure treasure;
        public bool isTreasure, direct, hidden, moved;
        public Vector3 pose, delta, truth;
        public Color color;
        public SpriteRenderer source, proxy;
        public UnityEngine.Rendering.SortingGroup group;   // characters draw as one unit (PlayerCharacter adds it)
        public readonly List<Visual> visuals = new List<Visual>();
        public int seen, childCount, visualCount;
    }

    static PaperHolds instance;
    readonly List<Held> held = new List<Held>();
    readonly Dictionary<Transform, Held> byTransform = new Dictionary<Transform, Held>();
    int poseFrame = int.MinValue, scanTick;
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        SceneManager.sceneLoaded -= OnScene;
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("Paper Holds");
        DontDestroyOnLoad(go);
        go.AddComponent<FrostIgnore2D>();
        instance = go.AddComponent<PaperHolds>();
        go.AddComponent<PaperHoldsRestore>();
        go.AddComponent<PaperHoldsDraw>();
        SceneManager.sceneLoaded += OnScene;
    }

    static void OnScene(Scene s, LoadSceneMode m) { if (instance != null) instance.Forget(); }
    void OnDisable() { Forget(); }
    void OnDestroy() { SceneManager.sceneLoaded -= OnScene; if (instance == this) instance = null; }

    void Forget()
    {
        Restore();
        foreach (Held h in held) if (h.proxy != null) Destroy(h.proxy.gameObject);
        held.Clear();
        byTransform.Clear();
        poseFrame = int.MinValue;
        nextScan = 0f;
    }

    static bool Safe(Transform t) => t.GetComponentInChildren<Collider2D>(true) == null
        && t.GetComponentInChildren<Rigidbody2D>(true) == null;

    // Discovery (including proxies/lists) allocates only on this half-second scan.
    void Scan()
    {
        nextScan = Time.unscaledTime + 0.5f;
        scanTick++;
        foreach (PlayerCharacter p in FindObjectsByType<PlayerCharacter>(FindObjectsSortMode.None))
        {
            Track(p.transform, false, null);
            GameObject bar = GameObject.Find(p.name + " Lives");
            if (bar != null) Track(bar.transform, true, null);
        }
        foreach (Transform t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.parent == null && t.name.Contains("Meowmeow")) Track(t, false, null);
        foreach (Treasure t in Treasure.OnHooks) if (t != null) Track(t.transform, false, t);
        for (int i = held.Count - 1; i >= 0; i--)
        {
            Held h = held[i];
            if (h.t != null && h.seen == scanTick) continue;
            // Unity's destroyed keys still have their managed identity; no allocating prune pass.
            byTransform.Remove(h.t);
            if (h.proxy != null) Destroy(h.proxy.gameObject);
            held.RemoveAt(i);
        }
    }

    void Track(Transform t, bool direct, Treasure treasure)
    {
        if (!byTransform.TryGetValue(t, out Held h))
        {
            h = new Held { t = t, pose = t.position, treasure = treasure, isTreasure = treasure != null };
            byTransform.Add(t, h);
            held.Add(h);
        }
        h.seen = scanTick;
        h.direct = direct && t.GetComponentInParent<Rigidbody2D>(true) == null
            && t.GetComponentInParent<Collider2D>(true) == null && Safe(t);
        h.visuals.Clear();
        h.childCount = -1;
        h.visualCount = 0;
        for (int i = 0; i < t.childCount + 16; i++) h.visuals.Add(new Visual());
        if (!h.direct)
        {
            RefreshChildren(h);
            h.source = t.GetComponent<SpriteRenderer>();
            h.group = t.GetComponent<UnityEngine.Rendering.SortingGroup>();
            if (h.source != null && h.proxy == null)
            {
                var go = new GameObject("Held sprite");
                go.transform.SetParent(transform, false);
                h.proxy = go.AddComponent<SpriteRenderer>();
                h.proxy.enabled = false;
            }
        }
    }

    // No allocation for children generated between scans (notably frost overlays).
    static void RefreshChildren(Held h)
    {
        if (h.childCount == h.t.childCount) return;
        h.childCount = h.t.childCount;
        h.visualCount = 0;
        for (int i = 0; i < h.t.childCount && h.visualCount < h.visuals.Count; i++)
        {
            Transform child = h.t.GetChild(i);
            if (!Safe(child)) continue;
            h.visuals[h.visualCount++].t = child;
        }
    }

    bool Visible(Held h) => h.t != null && h.t.gameObject.activeInHierarchy
        && (!h.isTreasure || (h.treasure != null && Treasure.OnHooks.Contains(h.treasure)));

    void LateUpdate()
    {
        if (!StopMotion.Enabled) { poseFrame = int.MinValue; return; }
        if (Time.unscaledTime >= nextScan) Scan();
        int frame = StopMotion.Frame;   // the shared beat (real-rate while running): positions step with the eyes and rock
        bool newPose = frame != poseFrame;
        poseFrame = frame;
        foreach (Held h in held)
        {
            h.delta = Vector3.zero;
            if (!Visible(h)) continue;
            Vector3 truth = h.t.position;
            if (newPose || (truth - h.pose).sqrMagnitude > 4f) h.pose = truth;
            h.delta = h.pose - truth;
        }
    }

    // Called at 32002, after roll/puppet poses and treasure sprite swaps at 2000.
    void Draw()
    {
        if (!StopMotion.Enabled) return;
        foreach (Held h in held)
        {
            if (!Visible(h)) continue;
            if (h.direct)
            {
                if (!Safe(h.t) || h.t.GetComponentInParent<Rigidbody2D>(true) != null
                    || h.t.GetComponentInParent<Collider2D>(true) != null) continue;
                h.truth = h.t.localPosition;
                h.t.position += h.delta;
                h.moved = true;
            }
            else
            {
                RefreshChildren(h);
                for (int i = 0; i < h.visualCount; i++)
                {
                    Visual v = h.visuals[i];
                    if (v.t == null || v.t.parent != h.t || !Safe(v.t)) continue;
                    v.truth = v.t.localPosition;
                    v.t.localPosition += h.t.InverseTransformVector(h.delta);
                    v.moved = true;
                }
                if (h.source == null || h.proxy == null) continue;
                SpriteRenderer s = h.source, p = h.proxy;
                h.color = s.color;
                p.sprite = s.sprite; p.flipX = s.flipX; p.flipY = s.flipY;
                p.color = h.color; p.sharedMaterial = s.sharedMaterial;
                // Inside a sorting group the root sprite is drawn with the whole character as one unit; the
                // stand-in lives outside it, so it takes the group's place and sits a hair farther from the
                // camera (ties sort by distance here), i.e. just behind the character's own parts, as before.
                if (h.group != null) { p.sortingLayerID = h.group.sortingLayerID; p.sortingOrder = h.group.sortingOrder; }
                else { p.sortingLayerID = s.sortingLayerID; p.sortingOrder = s.sortingOrder; }
                p.maskInteraction = s.maskInteraction; p.drawMode = s.drawMode; p.size = s.size;
                p.enabled = s.enabled && !s.forceRenderingOff;
                p.transform.SetPositionAndRotation(h.t.position + h.delta + (h.group != null ? new Vector3(0f, 0f, 0.01f) : Vector3.zero), h.t.rotation);
                p.transform.localScale = h.t.lossyScale;
                // Alpha only: renderer remains eligible for frost next frame after Restore.
                s.color = new Color(h.color.r, h.color.g, h.color.b, 0f);
                h.hidden = true;
            }
        }
    }

    internal void Restore()
    {
        foreach (Held h in held)
        {
            if (h.moved && h.t != null && Safe(h.t)) h.t.localPosition = h.truth;
            h.moved = false;
            foreach (Visual v in h.visuals)
            {
                if (v.moved && v.t != null && Safe(v.t)) v.t.localPosition = v.truth;
                v.moved = false;
            }
            if (h.hidden && h.source != null) h.source.color = h.color;
            h.hidden = false;
            if (h.proxy != null) h.proxy.enabled = false;
        }
    }

    // Shadows run before Draw: child casters have not yet been offset, and root alpha is still real.
    internal static Vector3 DisplayOffset(Transform caster)
    {
        if (instance == null || !StopMotion.Enabled) return Vector3.zero;
        for (Transform t = caster; t != null; t = t.parent)
            if (instance.byTransform.TryGetValue(t, out Held h)) return h.delta;
        return Vector3.zero;
    }
    internal static void RestoreNow() { if (instance != null) instance.Restore(); }
    internal static void DrawNow() { if (instance != null) instance.Draw(); }
}
