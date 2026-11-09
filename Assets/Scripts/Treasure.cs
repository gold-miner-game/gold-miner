using System.Collections.Generic;
using UnityEngine;

public enum TreasureType { SmallGold, MediumGold, LargeGold, Diamond }

// Owner: E (Treasure & Docs)
public class Treasure : MonoBehaviour
{
    // Every treasure still in the arena. MinerAI and GameManager read this.
    public static readonly List<Treasure> Active = new List<Treasure>();
    // Treasure riding a hook home: no longer Active, but not paid out yet
    public static readonly HashSet<Treasure> OnHooks = new HashSet<Treasure>();
    // The player's character ate this treasure (mouth, tail or tongue). The world map's one-time gold listens
    public static event System.Action<Treasure> Eaten;

    public TreasureType type = TreasureType.SmallGold;
    public int value = 50;

    public bool IsTaken { get; private set; }

    void Awake()
    {
        if (type != TreasureType.Diamond) return;
        SpriteRenderer look = GetComponent<SpriteRenderer>();
        if (look == null) return;
        look.sprite = GemSprite();
        look.color = new Color(0.45f, 0.78f, 1f);
    }

    void OnEnable()  { if (!IsTaken) Active.Add(this); }
    void OnDisable() { Active.Remove(this); }
    void OnDestroy() { OnHooks.Remove(this); }

    // The player's character touches the treasure. While it is digesting it can't eat; Stay retries
    // every physics step, so it eats as soon as the digest ends if it is still touching
    void OnTriggerEnter2D(Collider2D other) { Touch(other); }
    void OnTriggerStay2D(Collider2D other)  { Touch(other); }
    void OnTriggerExit2D(Collider2D other)
    {
        if (other.GetComponent<PlayerCharacter>() != null) PlayAnalytics.DigestRelease(this);
    }

    void Touch(Collider2D other)
    {
        PlayerCharacter player = other.GetComponent<PlayerCharacter>();
        if (player != null && !IsTaken) PlayAnalytics.DigestContact(this, player);
        TryEat(player);
    }

    // Also called by the squirrel's tail. Returns true if the character ate it
    public bool TryEat(PlayerCharacter player)
    {
        if (IsTaken) return false;
        if (player == null || !player.CanEat) return false;
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying) return false;

        IsTaken = true;
        Active.Remove(this);
        // Money first so maze hub can snapshot the correct total when marking this id eaten.
        if (GameManager.Instance != null) GameManager.Instance.AddPlayerMoney(value);
        player.OnTreasureEaten();
        player.DigestTreasure(this, value);
        PlayAnalytics.FirstGold(type);
        PlayAnalytics.ItemEaten(this);
        Eaten?.Invoke(this);
        Destroy(gameObject);
        return true;
    }

    // The hook reaches the treasure: the sprite rides the hook back. The miner is paid only when
    // it arrives (DeliverToMiner), so a blocking Mr. Hyde can still knock it loose on the way
    public void GrabByHook(Transform hook)
    {
        if (IsTaken) return;
        IsTaken = true;
        Active.Remove(this);
        OnHooks.Add(this);

        SetColliderEnabled(false);
        transform.SetParent(hook, true);
        transform.position = hook.position;
    }

    // The frog's tongue brought it into the mouth: the player eats it (money and digest)
    public void DeliverToPlayer(PlayerCharacter player)
    {
        OnHooks.Remove(this);
        if (player != null) player.Digest(value);
        if (GameManager.Instance != null) GameManager.Instance.AddPlayerMoney(value);
        PlayAnalytics.FirstGold(type);
        PlayAnalytics.ItemEaten(this);
        Eaten?.Invoke(this);
        Destroy(gameObject);
    }

    // The hook is home: pay the miner and remove the treasure
    public void DeliverToMiner()
    {
        OnHooks.Remove(this);
        if (GameManager.Instance != null) GameManager.Instance.AddMinerMoney(value);
        Destroy(gameObject);
    }

    // Knocked off the hook (Mr. Hyde blocked it): back in the arena where it is now, free to take again
    public void DropFromHook()
    {
        OnHooks.Remove(this);
        transform.SetParent(null, true);
        IsTaken = false;
        Active.Add(this);
        SetColliderEnabled(true);
    }

    void SetColliderEnabled(bool on)
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = on;
    }

    // A cut gem: flat top, wide middle, pointed bottom. Not a rhombus.
    static Sprite gem;

    static Sprite GemSprite()
    {
        if (gem != null) return gem;
        const int w = 128, h = 112;
        Vector2 tl = new Vector2(0.26f, 0.92f), tr = new Vector2(0.74f, 0.92f);
        Vector2 gl = new Vector2(0.06f, 0.58f), gr = new Vector2(0.94f, 0.58f);
        Vector2 mid = new Vector2(0.50f, 0.58f), bot = new Vector2(0.50f, 0.06f);
        (Vector2 a, Vector2 b, Vector2 c)[] facets =
        {
            (gl, bot, mid),
            (gr, mid, bot),
            (tl, gl, mid),
            (tr, mid, gr),
            (tl, mid, tr),
        };
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / w, (y + 0.5f) / h);
                bool filled = false;
                foreach (var facet in facets)
                    if (Inside(p, facet.a, facet.b, facet.c)) filled = true;
                tex.SetPixel(x, y, filled ? Color.white : Color.clear);
            }
        }
        tex.Apply();
        gem = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 120f);
        return gem;
    }

    static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
        float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
        float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
        bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
        bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(neg && pos);
    }
}
