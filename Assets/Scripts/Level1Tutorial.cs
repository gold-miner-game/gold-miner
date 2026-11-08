using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Owner: Zhan (Level 1)
// Level 1's first visit, in the map tutorial's style: the level goes dark except the one spot
// that matters, with an arrow and one short line beside it. No boxes, no Enter.
//   1. The pig. "You're the pig."
//   2. Every gold and the diamond at once, each with its own arrow. Eating gold raises the money.
//   3. The digest timer: it takes time, and they cannot eat again until it ends.
//   4. Where they stand: stopping makes the rope crawl. Time to think.
//   5. Walk onto the rope. "Press space to cut the rope." Then they can eat it.
//   6. Both money numbers: get more than the miner.
//   7. The life bar over the pig: at 0, they die.
// The evil miner stays hidden here. The same level from the map keeps it.
public class Level1Tutorial : MonoBehaviour
{
    [Tooltip("Gold the first lesson puts down (Gold_Small)")]
    public GameObject practiceGold;
    [Tooltip("Diamond the rope lesson hangs on the hook")]
    public GameObject practiceDiamond;
    [Tooltip("Also teach when the level is played straight from its scene in the editor")]
    public bool playWithoutFlow = true;

    [Range(0f, 1f)] public float darkness = 0.75f;
    public Color markColor = new Color(1f, 0.85f, 0.2f);

    const float Reach = 0.4f;

    PlayerCharacter player;
    MrHydeController hyde;
    MinerAI miner;

    SpriteRenderer dark;
    SpriteMask hole;
    SpriteRenderer ring;
    Transform arrow;
    TextMeshPro label;
    bool shown;

    class Callout
    {
        public SpriteMask hole;
        public SpriteRenderer ring;
        public Transform arrow;
        public TextMeshPro label;
        public Vector2 at;
        public float radius;
        public string text;
        public bool used;
    }

    readonly List<Callout> callouts = new List<Callout>();
    readonly List<Rect> taken = new List<Rect>();

    public static bool Running { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Running = false; }

    void Start()
    {
        bool teach = LevelLoader.ScriptedTutorial ?? playWithoutFlow;
        if (teach) StartCoroutine(Run());
    }

    // The clock waits for the lessons: the race gets its full time
    void Update()
    {
        GameManager gm = GameManager.Instance;
        if (Running && gm != null && gm.IsPlaying) gm.ConfigureTimer(gm.roundTime, gm.UseRoundTimer);
        if (shown) Bob();
    }

    void OnDestroy()
    {
        if (Time.timeScale == 0f || Time.timeScale < 0.2f) Time.timeScale = 1f;
        TutorialUIDark.Show(0f, null);
        Running = false;
    }

    IEnumerator Run()
    {
        while ((player = FindFirstObjectByType<PlayerCharacter>()) == null || (miner = GoldMiner()) == null
               || GameManager.Instance == null)
            yield return null;
        hyde = player as MrHydeController;
        Running = true;
        darkness = 0.82f;
        HideHunter();
        miner.autoShoot = false;
        miner.hook.hitsPig = false;
        BuildVisuals();
        Vector2 origin = miner.hookOrigin.position;

        // 1. Who you are. One second, everything else dark.
        yield return Show(new[] { (Vector2)player.transform.position }, 0.9f, "You're the pig.", 1f);
        Time.timeScale = 1f;
        Prompted();

        // 2. Every gold and the diamond, each with its own arrow. They eat a gold first.
        yield return UntilGoldEaten();
        Acted("eat_gold", true);

        // The pink number just went up. Time stays frozen, so the digest they just started is still on screen.
        yield return ShowMoney("Your money goes up.", 2f, "PlayerMoney");
        yield return ShowMoney("Digest takes time.", 2f, "DigestBlock");

        // 3. Right where they are: the rope crawls, so stopping is time to think.
        yield return SlowRope(origin);

        // 4. Walk to the spot first. The key is said only once they are standing on it.
        Treasure prize = TheDiamond() ?? NearestGold(player.transform.position);
        if (prize != null)
        {
            bool wasDiamond = prize.type == TreasureType.Diamond;
            Vector2 spot = OnThePath(origin, prize.transform.position);
            Prompted();
            while (Vector2.Distance(player.transform.position, spot) > Reach)
            {
                Point(spot, 0.6f, "Go here.", true);
                yield return null;
            }
            Acted("go_here", true);
            while (miner.hook.IsBusy) yield return null;
            miner.hook.PullBack(origin, prize, miner.treasureColor, 0f);
            Prompted();
            int stolen = player.TreasureStolen;
            while (miner.hook.IsBusy && player.TreasureStolen == stolen)
            {
                Point(player.transform.position, 0.85f, "Press space to cut the rope.", true);
                if (hyde == null || !hyde.IsBlocking) miner.hook.Hold(0.05f);
                yield return null;
            }
            Acted("cut", player.TreasureStolen != stolen);
            Prompted();
            int eaten = player.Eaten;
            float giveUp = Time.unscaledTime + 6f;
            string eatLine = wasDiamond ? "Now you can eat the diamond." : "Now you can eat it.";
            while (player.Eaten == eaten && prize != null && Time.unscaledTime < giveUp)
            {
                Point(prize.transform.position, 0.55f, eatLine, true);
                yield return null;
            }
            Acted("eat_drop", player.Eaten != eaten);
        }

        while (miner.hook.IsBusy) yield return null;
        yield return ShowMoney("Get more money than the miner.", 2f, "PlayerMoney", "MinerMoney");
        yield return Show(new[] { LifeBarAt() }, 0.55f, "At 0, you die.", 2f);

        GameManager gm = GameManager.Instance;
        // Gold and the diamond eaten while learning stay in the total. The race continues from there.
        gm.ConfigureTimer(gm.roundTime, gm.UseRoundTimer);
        miner.autoShoot = true;
        Time.timeScale = 1f;
        Running = false;
        Hide();
    }

    // Every gold and the diamond lit at once. Words only on the biggest gold and the diamond.
    IEnumerator UntilGoldEaten()
    {
        int before = player.Eaten;
        var items = new List<(Vector2 at, float radius, string text)>();
        while (player.Eaten == before)
        {
            Time.timeScale = 1f;
            int biggest = 0;
            foreach (Treasure treasure in Treasure.Active)
            {
                if (treasure == null || treasure.IsTaken || !treasure.gameObject.activeInHierarchy) continue;
                if (treasure.type == TreasureType.Diamond) continue;
                if (treasure.value > biggest) biggest = treasure.value;
            }
            items.Clear();
            foreach (Treasure treasure in Treasure.Active)
            {
                if (treasure == null || treasure.IsTaken || !treasure.gameObject.activeInHierarchy) continue;
                bool diamond = treasure.type == TreasureType.Diamond;
                string text = diamond ? "Eat the diamond."
                    : treasure.value == biggest && biggest > 0 ? "Eat the gold." : "";
                items.Add((treasure.transform.position, diamond ? 0.7f : 0.6f, text));
            }
            if (items.Count > 0) Mark(items, true);
            yield return null;
        }
        Hide();
    }

    float promptAt;

    // A prompt that asks for an action is on screen from now
    void Prompted() { promptAt = Time.unscaledTime; }

    // The player did what the prompt asked (done false: the step gave up). One analytics row,
    // value = seconds since the prompt appeared
    void Acted(string step, bool done)
    {
        PlayAnalytics.TutorialAction(step, Time.unscaledTime - promptAt, done);
    }

    // The red bar over the pig. Same spot PlayerLifeBar draws.
    Vector2 LifeBarAt()
    {
        var bar = player.GetComponent<PlayerLifeBar>();
        Collider2D body = player.GetComponent<Collider2D>();
        float lift = bar != null ? bar.lift : 0.22f;
        Vector2 at = body != null
            ? new Vector2(body.bounds.center.x, body.bounds.max.y)
            : (Vector2)player.transform.position + Vector2.up * 0.5f;
        return at + Vector2.up * lift;
    }

    // The HUD's money, not the arena. The rest of the screen stays dark.
    IEnumerator ShowMoney(string text, float seconds, params string[] targets)
    {
        Hide();
        if (dark != null) dark.enabled = true;
        PrepareMoney(targets);
        float until = Time.unscaledTime + seconds;
        while (Time.unscaledTime < until)
        {
            Time.timeScale = 0f;
            TutorialUIDark.Show(darkness, moneyShown, false);
            if (moneyLine != null)
            {
                moneyLine.gameObject.SetActive(true);
                moneyLine.text = text;
                PlaceMoneyLine();
            }
            yield return null;
        }
        ClearMoney();
        Time.timeScale = 1f;
    }

    readonly List<TutorialUIHole> moneyHoles = new List<TutorialUIHole>();
    readonly List<TutorialUIHole> moneyShown = new List<TutorialUIHole>();
    TextMeshProUGUI moneyLine;

    void PrepareMoney(string[] targets)
    {
        moneyShown.Clear();
        if (targets != null)
            foreach (string name in targets)
            {
                TutorialUIHole holeFor = null;
                foreach (TutorialUIHole have in moneyHoles)
                    if (have.target == name) holeFor = have;
                if (holeFor == null)
                {
                    var go = new GameObject("MoneyHole");
                    go.transform.SetParent(transform, false);
                    holeFor = go.AddComponent<TutorialUIHole>();
                    holeFor.target = name;
                    holeFor.padding = 18f;
                    moneyHoles.Add(holeFor);
                }
                moneyShown.Add(holeFor);
            }
        if (moneyLine != null) return;
        var canvasGo = new GameObject("MoneyLine", typeof(RectTransform), typeof(Canvas));
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 520;
        moneyLine = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        moneyLine.transform.SetParent(canvasGo.transform, false);
        moneyLine.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        moneyLine.fontSize = 28f;
        moneyLine.alignment = TextAlignmentOptions.Center;
        moneyLine.color = Color.white;
        moneyLine.textWrappingMode = TextWrappingModes.Normal;
        moneyLine.rectTransform.sizeDelta = new Vector2(640f, 80f);
    }

    void PlaceMoneyLine()
    {
        float midX = 0f;
        float bottom = float.MaxValue;
        int count = 0;
        Camera cam = null;
        foreach (TutorialUIHole lit in moneyShown)
        {
            RectTransform money = lit.Find();
            if (money == null) continue;
            Canvas owner = money.GetComponentInParent<Canvas>();
            cam = owner != null && owner.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? owner.rootCanvas.worldCamera : null;
            var corners = new Vector3[4];
            money.GetWorldCorners(corners);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            midX += (a.x + b.x) * 0.5f;
            bottom = Mathf.Min(bottom, Mathf.Min(a.y, b.y));
            count++;
        }
        if (count == 0) return;
        RectTransform line = moneyLine.rectTransform;
        RectTransform parent = (RectTransform)line.parent;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, new Vector2(midX / count, bottom - 36f), null, out Vector2 local);
        line.anchoredPosition = local;
    }

    void ClearMoney()
    {
        TutorialUIDark.Show(0f, null);
        if (moneyLine != null) moneyLine.gameObject.SetActive(false);
        if (dark != null && !shown) dark.enabled = false;
    }

    // Dark, frozen, one line, for a few real seconds.
    IEnumerator Show(Vector2[] spots, float radius, string text, float seconds)
    {
        float until = Time.unscaledTime + seconds;
        while (Time.unscaledTime < until)
        {
            Time.timeScale = 0f;
            Highlight(spots, radius, text);
            yield return null;
        }
    }

    // The player stays put and watches one rope crawl. Then they can move again.
    IEnumerator SlowRope(Vector2 origin)
    {
        TimeMovesWithPlayer pace = FindFirstObjectByType<TimeMovesWithPlayer>();
        float savedSpeed = player.moveSpeed;
        if (pace != null) pace.enabled = false;
        player.moveSpeed = 0f;
        FireNow(origin + Vector2.down * 3.2f, false);
        float until = Time.unscaledTime + 2.5f;
        while (Time.unscaledTime < until)
        {
            Time.timeScale = 0.07f;
            Point(player.transform.position, 0.9f, "When you stop, the miner slows down too.", true);
            yield return null;
        }
        player.moveSpeed = savedSpeed;
        Time.timeScale = 1f;
        if (pace != null) pace.enabled = true;
        Hide();
    }

    // Grabs on the same frame. telegraphMin is read before the first yield, so 0 means no windup.
    void FireNow(Vector2 target, bool atPlayer, float windup = 0f)
    {
        float min = miner.telegraphMin;
        float max = miner.telegraphMax;
        miner.telegraphMin = windup;
        miner.telegraphMax = windup;
        StartCoroutine(miner.FireScripted(target, atPlayer));
        miner.telegraphMin = min;
        miner.telegraphMax = max;
    }

    // A gold already lying in the level, the nearest one the player can walk to
    static Treasure NearestGold(Vector2 from)
    {
        Treasure best = null;
        float bestDistance = float.MaxValue;
        foreach (Treasure t in Treasure.Active)
        {
            if (t.type == TreasureType.Diamond || t.IsTaken || !t.gameObject.activeInHierarchy) continue;
            float d = Vector2.Distance(from, t.transform.position);
            if (d >= bestDistance) continue;
            best = t;
            bestDistance = d;
        }
        return best;
    }

    static Treasure TheDiamond()
    {
        foreach (Treasure t in Treasure.Active)
            if (t.type == TreasureType.Diamond && !t.IsTaken && t.gameObject.activeInHierarchy) return t;
        return null;
    }

    // On the miner's line to the diamond, just short of it, and not inside a rock
    static Vector2 OnThePath(Vector2 origin, Vector2 diamond)
    {
        Vector2 dir = diamond - origin;
        float length = dir.magnitude;
        if (length < 0.01f) return diamond;
        dir /= length;
        for (float back = 1.4f; back < length - 0.4f; back += 0.35f)
        {
            Vector2 spot = diamond - dir * back;
            if (!InsideRock(spot)) return spot;
        }
        return origin + dir * Mathf.Min(2f, length * 0.5f);
    }

    static bool InsideRock(Vector2 spot)
    {
        foreach (Collider2D hit in Physics2D.OverlapCircleAll(spot, 0.35f))
            if (hit.GetComponent<Rock>() != null) return true;
        return false;
    }

    static void HideHunter()
    {
        foreach (MinerAI m in FindObjectsByType<MinerAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!m.huntPigOnly) continue;
            GameObject root = m.transform.parent != null ? m.transform.parent.gameObject : m.gameObject;
            root.SetActive(false);
        }
    }

    static MinerAI GoldMiner()
    {
        foreach (MinerAI m in FindObjectsByType<MinerAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (!m.huntPigOnly && m.hook != null && m.hookOrigin != null && m.hook.gameObject.activeInHierarchy)
                return m;
        return null;
    }

    void Point(Vector2 at, float radius, string text, bool darken)
    {
        Mark(new (Vector2, float, string)[] { (at, radius, text) }, darken);
    }

    // One line on the first spot. Any further spots are only lit.
    void Highlight(Vector2[] spots, float radius, string text)
    {
        if (spots == null || spots.Length == 0) return;
        var items = new (Vector2 at, float radius, string text)[spots.Length];
        for (int i = 0; i < spots.Length; i++)
            items[i] = (spots[i], radius, i == 0 ? text : "");
        Mark(items, true);
    }

    // A hole, a ring, and (when it has words) an arrow and a line, on every spot at once.
    void Mark(IList<(Vector2 at, float radius, string text)> items, bool darken)
    {
        shown = true;
        dark.enabled = darken;
        while (callouts.Count < items.Count) callouts.Add(MakeCallout());
        for (int i = 0; i < callouts.Count; i++)
        {
            Callout mark = callouts[i];
            bool on = i < items.Count;
            mark.used = on;
            mark.hole.enabled = on;
            mark.ring.enabled = on;
            if (!on)
            {
                mark.arrow.gameObject.SetActive(false);
                mark.label.gameObject.SetActive(false);
                continue;
            }
            mark.at = items[i].at;
            mark.radius = items[i].radius;
            mark.text = items[i].text ?? "";
            mark.hole.transform.position = mark.at;
            mark.hole.transform.localScale = Vector3.one * mark.radius * 2f;
            mark.ring.transform.position = mark.at;
            mark.ring.transform.localScale = Vector3.one * mark.radius * 2.2f;
            bool words = mark.text.Length > 0;
            mark.arrow.gameObject.SetActive(words);
            mark.label.gameObject.SetActive(words);
            if (words) mark.label.text = mark.text;
        }
        PlaceCallouts();
    }

    Callout MakeCallout()
    {
        var holeGo = new GameObject("TutorialHole");
        holeGo.transform.SetParent(transform, false);
        SpriteMask mask = holeGo.AddComponent<SpriteMask>();
        mask.sprite = hole.sprite;

        var ringGo = new GameObject("TutorialRing");
        ringGo.transform.SetParent(transform, false);
        SpriteRenderer extra = ringGo.AddComponent<SpriteRenderer>();
        extra.sprite = ring.sprite;
        extra.color = markColor;
        extra.sortingOrder = 110;

        var arrowGo = new GameObject("TutorialArrow");
        arrowGo.transform.SetParent(transform, false);
        SpriteRenderer arrowRenderer = arrowGo.AddComponent<SpriteRenderer>();
        arrowRenderer.sprite = arrow.GetComponent<SpriteRenderer>().sprite;
        arrowRenderer.color = markColor;
        arrowRenderer.sortingOrder = 111;
        arrowGo.transform.localScale = Vector3.one * 0.9f;

        var labelGo = new GameObject("TutorialLabel");
        labelGo.transform.SetParent(transform, false);
        TextMeshPro words = labelGo.AddComponent<TextMeshPro>();
        words.font = label.font;
        words.fontSize = label.fontSize;
        words.alignment = TextAlignmentOptions.Center;
        words.color = Color.white;
        words.enableWordWrapping = true;
        words.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        words.rectTransform.sizeDelta = new Vector2(MaxTextWidth, 1f);
        words.GetComponent<MeshRenderer>().sortingOrder = 112;

        return new Callout { hole = mask, ring = extra, arrow = arrowGo.transform, label = words };
    }

    void Hide()
    {
        shown = false;
        if (dark != null) dark.enabled = false;
        foreach (Callout mark in callouts)
        {
            mark.used = false;
            if (mark.hole != null) mark.hole.enabled = false;
            if (mark.ring != null) mark.ring.enabled = false;
            if (mark.arrow != null) mark.arrow.gameObject.SetActive(false);
            if (mark.label != null) mark.label.gameObject.SetActive(false);
        }
        ClearMoney();
    }

    const float MaxTextWidth = 4f;

    // Each line sits beside its own circle, off the pig, the gold, the other lines, and the screen edge.
    void PlaceCallouts()
    {
        taken.Clear();
        var order = new List<Callout>();
        foreach (Callout mark in callouts)
            if (mark.used && !string.IsNullOrEmpty(mark.text)) order.Add(mark);
        order.Sort((a, b) => a.at.x.CompareTo(b.at.x));
        foreach (Callout mark in order)
        {
            PlaceOne(mark);
            taken.Add(TextBox(mark.label.transform.position, mark.label.rectTransform.sizeDelta));
        }
    }

    void PlaceOne(Callout mark)
    {
        TextMeshPro words = mark.label;
        words.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        Vector2 textSize = words.GetPreferredValues(words.text, MaxTextWidth, 0f);
        textSize.x = Mathf.Clamp(textSize.x + 0.2f, 0.6f, MaxTextWidth);
        textSize.y = Mathf.Max(0.45f, textSize.y);
        words.rectTransform.sizeDelta = textSize;

        float radius = mark.radius;
        Rect view = CameraView();
        float reach = radius + 0.55f + textSize.x * 0.5f;
        Vector2 left = mark.at + Vector2.left * reach;
        Vector2 right = mark.at + Vector2.right * reach;
        float leftCost = SideCost(left, view, radius, textSize, mark.at);
        float rightCost = SideCost(right, view, radius, textSize, mark.at);
        Vector2 side = rightCost < leftCost ? right : left;
        if (Mathf.Abs(leftCost - rightCost) < 1f && player != null)
            side = player.transform.position.x > mark.at.x ? left : right;
        side = KeepClear(side, view, radius, textSize, mark.at);
        words.transform.position = side;

        Vector2 toCircle = mark.at - side;
        if (toCircle.sqrMagnitude < 0.0001f) toCircle = Vector2.left;
        float gap = Mathf.Clamp(textSize.x * 0.5f + 0.35f, 0.35f, Mathf.Max(0.35f, toCircle.magnitude - radius - 0.2f));
        Vector2 at = side + toCircle.normalized * gap;
        mark.arrow.position = at;
        mark.arrow.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(toCircle.y, toCircle.x) * Mathf.Rad2Deg);
    }

    // Off screen, on the ring, on the pig, on another line, or on a piece of gold all cost. Lower is better.
    float SideCost(Vector2 center, Rect view, float radius, Vector2 textSize, Vector2 point)
    {
        Rect box = TextBox(center, textSize);
        const float edge = 0.3f;
        float cost = 0f;
        if (box.xMin < view.xMin + edge) cost += (view.xMin + edge - box.xMin) * 8f;
        if (box.xMax > view.xMax - edge) cost += (box.xMax - (view.xMax - edge)) * 8f;
        if (box.yMin < view.yMin + edge) cost += (view.yMin + edge - box.yMin) * 8f;
        if (box.yMax > view.yMax - edge) cost += (box.yMax - (view.yMax - edge)) * 8f;
        if (Hits(box, point, radius + 0.2f)) cost += 50f;
        if (player != null && Hits(box, player.transform.position, 0.8f)) cost += 30f;
        foreach (Rect block in taken)
            if (box.Overlaps(block)) cost += 80f;
        foreach (Callout other in callouts)
        {
            if (!other.used || (other.at - point).sqrMagnitude < 0.01f) continue;
            if (Hits(box, other.at, other.radius + 0.15f)) cost += 25f;
        }
        foreach (Treasure treasure in Treasure.Active)
        {
            if (treasure == null || !treasure.gameObject.activeInHierarchy) continue;
            if (Vector2.Distance(treasure.transform.position, point) < radius + 0.25f) continue;
            if (Hits(box, treasure.transform.position, 0.5f)) cost += 15f;
        }
        return cost;
    }

    // Stay inside the camera, and slide up or down only as far as it takes to clear the ring, the pig and other lines.
    Vector2 KeepClear(Vector2 center, Rect view, float radius, Vector2 textSize, Vector2 point)
    {
        const float edge = 0.3f;
        center.x = Mathf.Clamp(center.x, view.xMin + edge + textSize.x * 0.5f, view.xMax - edge - textSize.x * 0.5f);
        center.y = Mathf.Clamp(center.y, view.yMin + edge + textSize.y * 0.5f, view.yMax - edge - textSize.y * 0.5f);
        for (int i = 0; i < 8; i++)
        {
            Rect box = TextBox(center, textSize);
            bool blocked = Hits(box, point, radius + 0.25f) || OverlapsTaken(box);
            if (!blocked && player != null && Vector2.Distance(player.transform.position, point) > 0.35f)
                blocked = Hits(box, player.transform.position, 0.75f);
            if (!blocked) break;
            float roomUp = view.yMax - edge - textSize.y * 0.5f - center.y;
            float roomDown = center.y - (view.yMin + edge + textSize.y * 0.5f);
            center.y += roomUp >= roomDown ? 0.3f : -0.3f;
            center.y = Mathf.Clamp(center.y, view.yMin + edge + textSize.y * 0.5f, view.yMax - edge - textSize.y * 0.5f);
        }
        return center;
    }

    bool OverlapsTaken(Rect box)
    {
        foreach (Rect block in taken)
            if (box.Overlaps(block)) return true;
        return false;
    }

    static Rect CameraView()
    {
        Camera cam = Camera.main;
        if (cam == null) return new Rect(-8.5f, -4.6f, 17f, 9.2f);
        float halfH = cam.orthographicSize;
        float halfW = halfH * cam.aspect;
        Vector3 c = cam.transform.position;
        return new Rect(c.x - halfW, c.y - halfH, halfW * 2f, halfH * 2f);
    }

    static Rect TextBox(Vector2 center, Vector2 size)
    {
        return new Rect(center.x - size.x * 0.5f, center.y - size.y * 0.5f, size.x, size.y);
    }

    static bool Hits(Rect box, Vector2 center, float radius)
    {
        float x = Mathf.Clamp(center.x, box.xMin, box.xMax);
        float y = Mathf.Clamp(center.y, box.yMin, box.yMax);
        return (new Vector2(x, y) - center).sqrMagnitude < radius * radius;
    }

    void Bob()
    {
        PlaceCallouts();
        float w = 0.18f * Mathf.Sin(Time.unscaledTime * 6f);
        foreach (Callout mark in callouts)
        {
            if (!mark.used || mark.arrow == null || !mark.arrow.gameObject.activeSelf) continue;
            Vector2 dir = mark.at - (Vector2)mark.arrow.position;
            if (dir.sqrMagnitude < 0.0001f) continue;
            mark.arrow.position += (Vector3)(dir.normalized * w);
        }
    }

    void BuildVisuals()
    {
        var darkGo = new GameObject("TutorialDark");
        darkGo.transform.SetParent(transform, false);
        darkGo.transform.localScale = new Vector3(60f, 40f, 1f);
        dark = darkGo.AddComponent<SpriteRenderer>();
        dark.sprite = MakeSprite(4, (x, y) => 1f);
        dark.color = new Color(0f, 0f, 0f, darkness);
        dark.sortingOrder = 100;
        dark.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
        dark.enabled = false;

        var holeGo = new GameObject("TutorialHole");
        holeGo.transform.SetParent(transform, false);
        hole = holeGo.AddComponent<SpriteMask>();
        hole.sprite = MakeSprite(64, (x, y) => x * x + y * y <= 1f ? 1f : 0f);

        var ringGo = new GameObject("TutorialRing");
        ringGo.transform.SetParent(transform, false);
        ring = ringGo.AddComponent<SpriteRenderer>();
        ring.sprite = MakeSprite(64, (x, y) =>
        {
            float r = Mathf.Sqrt(x * x + y * y);
            return r <= 1f && r >= 0.86f ? 1f : 0f;
        });
        ring.color = markColor;
        ring.sortingOrder = 110;
        ring.enabled = false;

        var arrowGo = new GameObject("TutorialArrow");
        arrowGo.transform.SetParent(transform, false);
        var arrowRenderer = arrowGo.AddComponent<SpriteRenderer>();
        arrowRenderer.sprite = MakeSprite(64, (x, y) =>
        {
            float tip = (x + 1f) * 0.5f;
            return x > -0.9f && Mathf.Abs(y) <= Mathf.Lerp(0.42f, 0.02f, tip) ? 1f : 0f;
        });
        arrowRenderer.color = markColor;
        arrowRenderer.sortingOrder = 111;
        arrow = arrowGo.transform;
        arrow.localScale = Vector3.one * 0.9f;
        arrowGo.SetActive(false);

        var labelGo = new GameObject("TutorialLabel");
        labelGo.transform.SetParent(transform, false);
        label = labelGo.AddComponent<TextMeshPro>();
        label.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        label.fontSize = 3f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.enableWordWrapping = true;
        label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        label.rectTransform.sizeDelta = new Vector2(MaxTextWidth, 1f);
        label.GetComponent<MeshRenderer>().sortingOrder = 112;
        labelGo.SetActive(false);
        callouts.Add(new Callout { hole = hole, ring = ring, arrow = arrow, label = label });
    }

    static Sprite MakeSprite(int size, System.Func<float, float, float> alpha)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha(u, v)));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
