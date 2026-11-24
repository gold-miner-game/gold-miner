using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Art: every hook now has someone pulling it. A felt Meowmeow miner (Resources/Art/Miner, painted with Meshy
// from the character sheet) peeks out of each burrow and acts out its hook, posed on twos:
//   - idle: peeking          - hook going out: leaning in, eyes wide
//   - reeling in: eyes squeezed, paws on the twine    - a red shot at you: narrow red glare
// The cat stays upright and leans toward where its hook went, so you can read who is hooking and at what.
// Like the miners of the classic gold-mining arcade games, it works a winch: each burrow is framed as a
// mine shaft (cut-paper timber planks) and a twine reel beside it turns a held pose at a time, paying out
// while the hook flies and cranking back while it reels in.
// Read-only: it follows each Hook's rope (its first point is the burrow) and the hook's own position and
// AimedAtPlayer flag. Drawn above the burrow and below the rope; never parented to the hook; no colliders.
public class PaperMiners : MonoBehaviour
{
    const float Size = 0.95f;  // world units across, a little wider than a burrow
    const float Peek = 0.22f;  // the cat sits on the rim, toward its hook: charcoal felt would vanish in the black burrow
    static PaperMiners instance;

    class Miner
    {
        public Hook hook;
        public LineRenderer rope;
        public SpriteRenderer look, frame, reel;
        public float reelAngle;
        public Vector2 home;          // the burrow: remembered while the hook rests in it
        public bool homeKnown;
        public float lastReach;
        public int pose, lastFrame = -1;
        public float facing = -90f;   // degrees; the cat's top points this way
    }

    readonly List<Miner> miners = new List<Miner>();
    Sprite idle, eager, pull, glare, frameArt, reelArt;
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        var go = new GameObject("Paper Miners");
        DontDestroyOnLoad(go);
        go.AddComponent<FrostIgnore2D>();
        instance = go.AddComponent<PaperMiners>();
        SceneManager.sceneLoaded += (s, m) => { if (instance != null) instance.nextScan = 0f; };
    }

    void Start()
    {
        idle = ArtLibrary.Get("Miner/MinerIdle"); eager = ArtLibrary.Get("Miner/MinerEager");
        pull = ArtLibrary.Get("Miner/MinerPull"); glare = ArtLibrary.Get("Miner/MinerGlare");
        if (idle == null || eager == null || pull == null || glare == null) enabled = false;
        frameArt = ArtLibrary.Get("Miner/ShaftFrame"); reelArt = ArtLibrary.Get("Miner/WinchReel");
    }

    void LateUpdate()
    {
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 0.5f;
            foreach (Hook hook in FindObjectsByType<Hook>(FindObjectsSortMode.None))
                if (!miners.Exists(m => m.hook == hook)) Add(hook);
        }
        for (int i = miners.Count - 1; i >= 0; i--)
        {
            Miner m = miners[i];
            if (m.hook == null || m.rope == null)
            {
                if (m.look != null) Destroy(m.look.gameObject);
                if (m.frame != null) Destroy(m.frame.gameObject);
                if (m.reel != null) Destroy(m.reel.gameObject);
                miners.RemoveAt(i);
                continue;
            }
            Pose(m);
        }
    }

    void Add(Hook hook)
    {
        Transform r = hook.transform.Find("Rope");
        LineRenderer rope = r != null ? r.GetComponent<LineRenderer>() : null;
        if (rope == null) return;   // not built yet; the next scan tries again
        miners.Add(new Miner { hook = hook, rope = rope, look = Piece("Miner", idle), frame = Piece("Mine Shaft", frameArt), reel = Piece("Winch Reel", reelArt) });
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

    void Pose(Miner m)
    {
        bool active = m.hook.isActiveAndEnabled;
        Vector2 at = m.hook.transform.position;
        if (!m.hook.IsBusy) { m.home = at; m.homeKnown = true; }
        else if (m.rope.enabled && m.rope.positionCount > 0) { m.home = m.rope.GetPosition(0); m.homeKnown = true; }
        m.look.enabled = active && m.homeKnown;
        if (m.frame != null) m.frame.enabled = m.look.enabled && frameArt != null;
        if (m.reel != null) m.reel.enabled = m.look.enabled && reelArt != null;
        if (!m.look.enabled) return;

        float reach = Vector2.Distance(at, m.home);
        int frame = StopMotion.Frame;
        if (frame != m.lastFrame)
        {
            m.lastFrame = frame;
            // pose: 0 idle, 1 eager (going out), 2 pulling (coming back), 3 glare (a red shot at the player)
            int pose = 0;
            if (m.hook.IsBusy)
            {
                if (m.hook.AimedAtPlayer && reach >= m.lastReach) pose = 3;
                else pose = reach >= m.lastReach ? 1 : 2;
                Vector2 d = at - m.home;
                if (d.sqrMagnitude > 0.01f) m.facing = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            }
            m.pose = pose;
            if (m.hook.IsBusy) m.reelAngle += reach >= m.lastReach ? -30f : 30f;   // pay out / crank back, a held pose per tick
            m.lastReach = reach;
        }
        m.look.sprite = m.pose == 1 ? eager : m.pose == 2 ? pull : m.pose == 3 ? glare : idle;

        // the art's top points up; turn it to face the hook's way. Lean the eager/glare poses a little out
        // of the burrow toward the shot, the pulling pose a little back in.
        Vector2 toward = new Vector2(Mathf.Cos(m.facing * Mathf.Deg2Rad), Mathf.Sin(m.facing * Mathf.Deg2Rad));
        float lean = Peek + (m.pose == 1 || m.pose == 3 ? 0.08f : m.pose == 2 ? -0.05f : 0f);
        // upright, as drawn (a cat turned sideways reads as a blob); it leans toward its hook instead
        m.look.transform.SetPositionAndRotation(m.home + toward * lean, Quaternion.identity);
        m.look.transform.localScale = Vector3.one * (Size / Mathf.Max(0.01f, m.look.sprite.bounds.size.x));
        m.look.sortingLayerID = m.rope.sortingLayerID;
        m.look.sortingOrder = m.rope.sortingOrder - 1;   // over the burrow, just under the rope and claw

        // the shaft's timber frame round the burrow, and the winch reel beside it (to the cat's left)
        if (m.frame != null && frameArt != null)
        {
            m.frame.transform.SetPositionAndRotation(m.home, Quaternion.identity);
            m.frame.transform.localScale = Vector3.one * (Size * 1.25f / frameArt.bounds.size.x);
            m.frame.sortingLayerID = m.rope.sortingLayerID;
            m.frame.sortingOrder = m.rope.sortingOrder - 2;
        }
        if (m.reel != null && reelArt != null)
        {
            Vector2 left = new Vector2(-toward.y, toward.x);
            m.reel.transform.SetPositionAndRotation(m.home + left * Size * 0.78f, Quaternion.Euler(0f, 0f, m.reelAngle));
            m.reel.transform.localScale = Vector3.one * (Size * 0.55f / reelArt.bounds.size.x);
            m.reel.sortingLayerID = m.rope.sortingLayerID;
            m.reel.sortingOrder = m.rope.sortingOrder - 1;
        }
    }
}
