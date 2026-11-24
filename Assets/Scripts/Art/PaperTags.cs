using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// Art: world-space name labels (the world map's "Tutorial", "Level 1", "Store"... and the floor's area
// names; objects named "Label...") sit on a small felt tag instead of floating on the floor. The tag is a sliced felt strip
// (Resources/Art/Slices/FeltStrip) sized to the words, drawn just under them.
//   - Read-only: the label's text, colour, alpha and visibility are left alone; the tag copies its
//     visibility and alpha every frame, so locked (dimmer) and revealing (fading in) labels still read.
//   - Never parented under game objects: tags live under their own root and follow their label.
//   - Only short one-line labels; no art: nothing changes.
public class PaperTags : MonoBehaviour
{
    const int MaxChars = 24;
    static readonly Vector2 Pad = new Vector2(0.32f, 0.14f);   // world units around the words
    static readonly Color Felt = new Color(0.28f, 0.22f, 0.2f, 0.9f);

    static PaperTags instance;
    Sprite felt;
    readonly Dictionary<int, (TextMeshPro label, SpriteRenderer tag)> tags = new Dictionary<int, (TextMeshPro, SpriteRenderer)>();
    readonly List<int> gone = new List<int>();
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("Paper Tags");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PaperTags>();
        SceneManager.sceneLoaded += (s, m) => { if (instance != null) instance.nextScan = 0f; };
    }

    void Start() { felt = ArtLibrary.Get("Slices/FeltStrip"); }

    void LateUpdate()
    {
        if (felt == null) return;
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 0.5f;
            foreach (TextMeshPro label in FindObjectsByType<TextMeshPro>(FindObjectsSortMode.None))
                if (!tags.ContainsKey(label.GetInstanceID())) Add(label);
        }

        foreach (var pair in tags)
        {
            TextMeshPro label = pair.Value.label;
            SpriteRenderer tag = pair.Value.tag;
            if (label == null) { gone.Add(pair.Key); continue; }
            if (tag != null) Pose(label, tag);   // null tag: a label this layer leaves alone
        }
        foreach (int id in gone)
        {
            if (tags.TryGetValue(id, out var t) && t.tag != null) Destroy(t.tag.gameObject);
            tags.Remove(id);
        }
        gone.Clear();
    }

    void Add(TextMeshPro label)
    {
        string words = label.text;
        // Only the map's name labels (MapHub.AddLabel's "Label <words>", HubPortal's "Label" child)
        bool mapLabel = label.gameObject.name.StartsWith("Label");
        if (!mapLabel || string.IsNullOrEmpty(words) || words.Length > MaxChars || words.Contains("\n")) { tags[label.GetInstanceID()] = (label, null); return; }
        var go = new GameObject("Tag " + words);
        go.transform.SetParent(transform, false);
        var tag = go.AddComponent<SpriteRenderer>();
        tag.sprite = felt;
        tag.drawMode = SpriteDrawMode.Sliced;
        tag.color = Felt;
        tags[label.GetInstanceID()] = (label, tag);
        Pose(label, tag);
    }

    static void Pose(TextMeshPro label, SpriteRenderer tag)
    {
        bool shown = label.enabled && label.gameObject.activeInHierarchy && label.color.a > 0.01f && !string.IsNullOrEmpty(label.text);
        tag.enabled = shown;
        if (!shown) return;
        Bounds local = label.textBounds;
        if (local.size.x <= 0f) { tag.enabled = false; return; }
        Transform t = label.transform;
        Vector3 scale = t.lossyScale;
        tag.transform.SetPositionAndRotation(t.TransformPoint(local.center), t.rotation);
        tag.size = new Vector2(local.size.x * Mathf.Abs(scale.x) + Pad.x, local.size.y * Mathf.Abs(scale.y) + Pad.y);
        tag.sortingLayerID = label.sortingLayerID;
        tag.sortingOrder = label.sortingOrder - 1;
        tag.color = new Color(Felt.r, Felt.g, Felt.b, Felt.a * Mathf.Clamp01(label.color.a / 0.6f));
    }
}
