using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Art: the hook's claw as a crafted tin piece. Hook still draws its claw arms as two line renderers
// (the frost rules read those, and their reach is the real grab). This layer adds a riveted tin hub at
// the pivot (Resources/Art/Fx/ClawHub), the joint the arms hinge on. Nothing is drawn past the arms,
// so the claw never looks bigger than what it grabs. (Talons at the tips were tried and dropped: at
// play size they merged with the arms.)
// Read-only: position and visibility come from the arms every frame. The hub lives under its own root
// (never parented to the hook), carries FrostIgnore2D, and turns icy while the arms are frozen.
[DefaultExecutionOrder(31000)]   // after Hook has placed its arms this frame
public class PaperClaws : MonoBehaviour
{
    static readonly Color Ice = new Color(0.72f, 0.9f, 1f);
    static PaperClaws instance;

    class Claw
    {
        public Hook hook;
        public LineRenderer left, right;
        public SpriteRenderer hub;
    }

    readonly List<Claw> claws = new List<Claw>();
    Sprite hubArt;
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("Paper Claws");
        DontDestroyOnLoad(go);
        go.AddComponent<FrostIgnore2D>();
        instance = go.AddComponent<PaperClaws>();
        SceneManager.sceneLoaded += (s, m) => { if (instance != null) instance.nextScan = 0f; };
    }

    void Start()
    {
        Sprite hub = ArtLibrary.Get("Fx/ClawHub");
        if (hub == null) { enabled = false; return; }
        hubArt = Remake(hub, new Vector2(0.5f, 0.5f));
    }

    // 1 unit wide with its own pivot; scaled per hook below
    static Sprite Remake(Sprite art, Vector2 pivot)
    {
        Texture2D t = art.texture;
        return Sprite.Create(t, new Rect(0, 0, t.width, t.height), pivot, t.width, 0, SpriteMeshType.FullRect);
    }

    void LateUpdate()
    {
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 0.5f;
            foreach (Hook hook in FindObjectsByType<Hook>(FindObjectsSortMode.None))
                if (!claws.Exists(c => c.hook == hook)) Add(hook);
        }
        for (int i = claws.Count - 1; i >= 0; i--)
        {
            Claw c = claws[i];
            if (c.hook == null || c.left == null || c.right == null)
            {
                if (c.hub != null) Destroy(c.hub.gameObject);
                claws.RemoveAt(i);
                continue;
            }
            Pose(c);
        }
    }

    void Add(Hook hook)
    {
        Transform l = hook.transform.Find("ClawLeft"), r = hook.transform.Find("ClawRight");
        LineRenderer left = l != null ? l.GetComponent<LineRenderer>() : null;
        LineRenderer right = r != null ? r.GetComponent<LineRenderer>() : null;
        if (left == null || right == null) return;   // not drawn yet; the next scan tries again
        claws.Add(new Claw
        {
            hook = hook, left = left, right = right,
            hub = Piece("Claw Hub", hubArt),
        });
    }

    SpriteRenderer Piece(string pieceName, Sprite art)
    {
        var go = new GameObject(pieceName);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = art;
        sr.enabled = false;
        return sr;
    }

    void Pose(Claw c)
    {
        bool shown = c.left.enabled && c.left.gameObject.activeInHierarchy && c.left.positionCount >= 3;
        c.hub.enabled = shown;
        if (!shown) return;

        bool frozen = FrostField2D.IsFrozen(c.left) || FrostField2D.IsFrozen(c.right);
        Color tint = c.left.startColor;

        Vector3 pivot = c.left.GetPosition(0);
        c.hub.transform.SetPositionAndRotation(pivot, Quaternion.identity);
        c.hub.transform.localScale = Vector3.one * Mathf.Max(0.12f, c.hook.clawWidth * 2.6f);
        c.hub.color = frozen ? Ice : new Color(1f, 1f, 1f, tint.a);
        Order(c.hub, c.left, 2);
    }

    static void Order(SpriteRenderer piece, LineRenderer line, int above)
    {
        piece.sortingLayerID = line.sortingLayerID;
        piece.sortingOrder = Mathf.Min(short.MaxValue, line.sortingOrder + above);
    }
}
