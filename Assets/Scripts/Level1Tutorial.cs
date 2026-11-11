using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Owner: Zhan (Level 1)
// Level 1's first visit, in the map tutorial's style: the level goes dark except the one spot
// that matters, with a flashlight pool, a white arrow above it, and one short line. No boxes, no Enter.
//   1. Hook, pig, and the biggest gold, top to bottom. "You're the pig."
//   2. Walk down and eat that gold. The hook extends toward the pig the whole way.
//   3. The money, then the digest timer.
//   4. That same hook turns red and hits. One life, then the life bar: at 0, they die.
//   5. A red hook waits on the pig. Two quick Space presses block it, and the hook bounces off.
//   6. Walk onto the rope. Arriving shows "Press [J] to cut the rope." at once, and it stays up.
//   7. Both money numbers: get more than the miner. That line stays up for the rest of the level.
//   After every lesson, stopping shows "Stopping slows the hook." under the pig, with no circle.
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
    bool flashlight;
    bool slowHintLive;
    Canvas keysCanvas;
    GameObject wasdKeys;
    GameObject cutLine;

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

    void LateUpdate()
    {
        PositionWasd();
    }

    void OnDestroy()
    {
        UnlockMovement();
        if (Time.timeScale == 0f || Time.timeScale < 0.2f) Time.timeScale = 1f;
        TutorialUIDark.Show(0f, null);
        if (keysCanvas != null) Destroy(keysCanvas.gameObject);
        Running = false;
    }

    // After every lesson. Same path as the "Digesting…" label: small GUI text under the pig, no ring.
    void OnGUI()
    {
        if (!slowHintLive || player == null) return;
        if (player.MoveInput.sqrMagnitude > 0.0001f) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        Collider2D body = player.GetComponent<Collider2D>();
        Vector3 feet = body != null
            ? new Vector3(body.bounds.center.x, body.bounds.min.y, 0f)
            : player.transform.position + Vector3.down * 0.45f;
        Vector3 screen = cam.WorldToScreenPoint(feet);
        if (screen.z <= 0f) return;
        if (slowStyle == null)
        {
            slowStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            slowStyle.normal.textColor = Color.white;
        }
        ScreenFrame.BeginGUI();
        Vector2 at = ScreenFrame.ScreenToGui(screen);
        // Start under the body, and keep the line inside the frame so it cannot fall off the screen.
        float y = Mathf.Min(at.y + 8f, ScreenFrame.GuiHeight - 28f);
        var rect = new Rect(at.x - 140f, y, 280f, 22f);
        Color ink = slowStyle.normal.textColor;
        slowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.9f);
        GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), "Stopping slows the hook.", slowStyle);
        slowStyle.normal.textColor = ink;
        GUI.Label(rect, "Stopping slows the hook.", slowStyle);
    }

    GUIStyle slowStyle;

    IEnumerator Run()
    {
        while ((player = FindFirstObjectByType<PlayerCharacter>()) == null || (miner = GoldMiner()) == null
               || GameManager.Instance == null)
            yield return null;
        hyde = player as MrHydeController;
        Running = true;
        darkness = 0.82f;
        HideHunter();
        SetMinersArmed(false);
        miner.hook.hitsPig = false;
        BuildVisuals();
        BuildKeys();
        Vector2 origin = miner.hookOrigin.position;
        Treasure bigGold = LineUp(origin);

        // 1. Who you are. One second, everything else dark. WASD follows above the player's light.
        ShowWasd(true);
        yield return Show(new[] { (Vector2)player.transform.position }, 0.9f, "You're the pig.", 1f);
        Time.timeScale = 1f;
        Prompted();

        // 2. The big gold is below the pig. The hook lengthens toward them until they eat it.
        yield return UntilBigGoldEaten(origin, bigGold);
        Acted("eat_gold", true);
        ShowWasd(false);

        // The pink number just went up. Time stays frozen, so the digest they just started is still on screen.
        yield return ShowMoney("Your money goes up.", 2f, "PlayerMoney");
        yield return ShowMoney("Digest takes time.", 2f, "DigestBlock");

        // 3. The same hook, now red, runs into the pig. Then the life bar.
        yield return ForceHit(origin);
        yield return Show(new[] { LifeBarAt() }, 0.55f, "The hook takes a life. At 0, you die.", 2f);
        Time.timeScale = 1f;

        // 4. The pig's skill: two quick Space presses bounce the next hook.
        yield return TeachBlock(origin);

        // 5. Walk to the spot. Arriving opens the cut; cutting opens the eat. No pause between those.
        Treasure prize = TheDiamond() ?? NearestGold(player.transform.position);
        if (prize != null)
        {
            bool wasDiamond = prize.type == TreasureType.Diamond;
            Vector2 spot = OnThePath(origin, prize.transform.position);
            ShowWasd(true);
            Prompted();
            while (Vector2.Distance(player.transform.position, spot) > Reach)
            {
                Point(spot, 0.6f, "Go here.", true);
                yield return null;
            }
            Acted("go_here", true);
            miner.hook.PullBack(origin, prize, miner.treasureColor, 0f);
            ShowCutLine(true);
            Prompted();
            int stolen = player.TreasureStolen;
            while (miner.hook.IsBusy && player.TreasureStolen == stolen)
            {
                Point(player.transform.position, 0.85f, "", true);
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
            ShowWasd(false);
        }

        keepMoneyLine = true;
        yield return ShowMoney("Get more money than the miner to pass.", 2f, "PlayerMoney", "MinerMoney");

        GameManager gm = GameManager.Instance;
        // Gold and the diamond eaten while learning stay in the total. The race continues from there.
        gm.ConfigureTimer(gm.roundTime, gm.UseRoundTimer);
        miner.hook.hitsPig = true;
        miner.autoShoot = true;
        SetMinersArmed(true);
        Time.timeScale = 1f;
        Running = false;
        Hide();
        ShowWasd(false);
        slowHintLive = true;
    }

    bool savedGrab = true;
    bool savedPass;
    const float FollowGap = 1.55f;

    // Hook on top, the pig under it, the biggest gold under the pig.
    Treasure LineUp(Vector2 origin)
    {
        Vector2 pig = origin + Vector2.down * 2.8f;
        for (int i = 0; i < 5 && InsideRock(pig); i++) pig += Vector2.down * 0.4f;
        PlaceBody(player.gameObject, pig);

        Treasure big = BiggestGold();
        if (big == null) return null;
        Vector2 gold = pig + Vector2.down * 2.3f;
        for (int i = 0; i < 5 && InsideRock(gold); i++) gold += Vector2.down * 0.4f;
        PlaceBody(big.gameObject, gold);
        return big;
    }

    static Treasure BiggestGold()
    {
        Treasure big = null;
        int best = int.MinValue;
        foreach (Treasure treasure in Treasure.Active)
        {
            if (treasure == null || treasure.IsTaken || !treasure.gameObject.activeInHierarchy) continue;
            if (treasure.type == TreasureType.Diamond) continue;
            if (big != null && treasure.value <= best) continue;
            big = treasure;
            best = treasure.value;
        }
        return big;
    }

    // The big gold stays lit. The hook grows toward the pig and stops just short of them.
    IEnumerator UntilBigGoldEaten(Vector2 origin, Treasure gold)
    {
        savedGrab = miner.hook.grabsTreasure;
        savedPass = miner.hook.passRocks;
        miner.hook.hitsPig = false;
        miner.hook.grabsTreasure = false;
        miner.hook.passRocks = true;
        while (gold != null && !gold.IsTaken)
        {
            Time.timeScale = 1f;
            SteerHook(origin, false);
            Point(gold.transform.position, 0.7f, "Eat the gold.", true);
            yield return null;
        }
        if (miner.hook != null && miner.hook.IsBusy) miner.hook.Hold(0.3f);
        Hide();
    }

    // extra false: stay a short gap above the pig. extra true: the red shot is allowed to reach them.
    void SteerHook(Vector2 origin, bool strike)
    {
        Hook hook = miner.hook;
        if (hook == null) return;
        Vector2 body = BodyCenter();
        hook.KeepExtendingToward(origin, body, 2f, strike ? miner.playerColor : miner.treasureColor);
        if (!strike && Vector2.Distance(hook.transform.position, body) < FollowGap)
            hook.Hold(0.25f);
    }

    static void PlaceBody(GameObject go, Vector2 at)
    {
        go.transform.position = at;
        Rigidbody2D body = go.GetComponent<Rigidbody2D>();
        if (body == null) return;
        body.position = at;
        body.linearVelocity = Vector2.zero;
    }

    // The hook that followed the pig turns red and finishes the trip. The pig stays where they ate.
    IEnumerator ForceHit(Vector2 origin)
    {
        TimeMovesWithPlayer pace = FindFirstObjectByType<TimeMovesWithPlayer>();
        if (pace != null) pace.enabled = false;
        if (hyde != null) hyde.CancelBlock();
        LockMovement();
        Time.timeScale = 1f;

        Hook hook = miner.hook;
        int lives = GameManager.Instance.Lives;
        float savedExtend = hook.extendSpeed;
        hook.hitsPig = true;
        hook.grabsTreasure = false;
        hook.passRocks = true;
        hook.extendSpeed = 4f;
        hook.AimedAtPlayer = true;
        hook.ClearHold();

        bool applied = false;
        float safety = Time.unscaledTime + 4f;
        while (GameManager.Instance.Lives >= lives && Time.unscaledTime < safety)
        {
            Time.timeScale = 1f;
            HoldStill();
            if (hyde != null) hyde.CancelBlock();
            SteerHook(origin, true);
            Point(player.transform.position, 0.9f, "The hook takes a life.", true);
            if (!applied && Vector2.Distance(hook.transform.position, BodyCenter()) < 0.5f)
            {
                hook.ApplyHit(player);
                applied = true;
            }
            yield return null;
        }
        if (!applied && GameManager.Instance.Lives >= lives)
            hook.ApplyHit(player);

        float show = Time.unscaledTime + 0.8f;
        while (Time.unscaledTime < show)
        {
            Time.timeScale = 1f;
            HoldStill();
            hook.Hold(0.2f);
            Point(player.transform.position, 0.9f, "The hook takes a life.", true);
            yield return null;
        }
        hook.ClearHold();
        hook.SendHome();

        safety = Time.unscaledTime + 2.5f;
        while (hook.IsBusy && Time.unscaledTime < safety)
        {
            HoldStill();
            yield return null;
        }
        if (hook.IsBusy) hook.StopShot();
        // Stay harmless through the cut lesson. Run turns hits back on when the lessons end.
        hook.hitsPig = false;
        hook.grabsTreasure = savedGrab;
        hook.passRocks = savedPass;
        hook.extendSpeed = savedExtend;
        UnlockMovement();
        if (pace != null) pace.enabled = true;
        Time.timeScale = 1f;
        Hide();
    }

    // A red hook stops just short of the pig. Two quick Space presses block, and it bounces.
    // A miss does not cost a life. One Space cannot cut this rope, or the first tap would cut it.
    IEnumerator TeachBlock(Vector2 origin)
    {
        if (hyde == null || miner.hook == null) yield break;
        hyde.canBlock = true;
        if (PlayerCharacter.Record != null) PlayerCharacter.Record.setCanBlock(true);
        hyde.suppressCut = true;
        hyde.CancelBlock();

        TimeMovesWithPlayer pace = FindFirstObjectByType<TimeMovesWithPlayer>();
        if (pace != null) pace.enabled = false;
        LockMovement();
        Time.timeScale = 1f;

        Hook hook = miner.hook;
        float savedExtend = hook.extendSpeed;
        hook.hitsPig = false;
        hook.grabsTreasure = false;
        hook.passRocks = true;
        hook.extendSpeed = 7f;
        hook.AimedAtPlayer = true;

        int blocked = player.HooksBlocked;
        Prompted();
        while (player.HooksBlocked == blocked)
        {
            Time.timeScale = 1f;
            HoldStill();
            Vector2 body = BodyCenter();
            hook.KeepExtendingToward(origin, body, 1.2f, miner.playerColor);
            if (hyde.IsBlocking) hook.ClearHold();
            else if (Vector2.Distance(hook.transform.position, body) < FollowGap) hook.Hold(0.2f);
            Point(player.transform.position, 0.9f, "Press K to block.", true);
            yield return null;
        }
        Acted("block", true);

        float until = Time.unscaledTime + 1.2f;
        while (Time.unscaledTime < until)
        {
            Time.timeScale = 1f;
            HoldStill();
            Point(player.transform.position, 0.9f, "The hook bounces off.", true);
            yield return null;
        }
        hook.SendHome();
        float safety = Time.unscaledTime + 2.5f;
        while (hook.IsBusy && Time.unscaledTime < safety)
        {
            HoldStill();
            yield return null;
        }
        if (hook.IsBusy) hook.StopShot();
        hook.hitsPig = false;
        hook.grabsTreasure = savedGrab;
        hook.passRocks = savedPass;
        hook.extendSpeed = savedExtend;
        hyde.suppressCut = false;
        UnlockMovement();
        if (pace != null) pace.enabled = true;
        Time.timeScale = 1f;
        Hide();
    }

    Vector2 BodyCenter()
    {
        Collider2D body = player.GetComponent<Collider2D>();
        return body != null ? (Vector2)body.bounds.center : (Vector2)player.transform.position;
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
    bool keepMoneyLine;

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
        canvas.sortingOrder = 560;
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
        if (count == 0 && !keepMoneyLine) return;
        RectTransform line = moneyLine.rectTransform;
        if (keepMoneyLine)
        {
            // Just under the HUD top bar, and it stays there for the rest of the level.
            Rect frame = ScreenFrame.Pixels;
            float belowBar = (Screen.height - frame.yMax) + frame.height * HUDLayout.TopBarHeight + 12f;
            line.anchorMin = line.anchorMax = new Vector2(0.5f, 1f);
            line.pivot = new Vector2(0.5f, 1f);
            line.anchoredPosition = new Vector2(0f, -belowBar);
            line.sizeDelta = new Vector2(900f, 56f);
            return;
        }
        if (count == 0) return;
        RectTransform parent = (RectTransform)line.parent;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, new Vector2(midX / count, bottom - 36f), null, out Vector2 local);
        line.pivot = new Vector2(0.5f, 0.5f);
        line.anchoredPosition = local;
    }

    void ClearMoney()
    {
        TutorialUIDark.Show(0f, null);
        if (moneyLine != null)
        {
            if (keepMoneyLine)
            {
                moneyLine.gameObject.SetActive(true);
                PlaceMoneyLine();
            }
            else moneyLine.gameObject.SetActive(false);
        }
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

    bool movementLocked;
    float savedMoveSpeed;
    Rigidbody2D lockedBody;
    RigidbodyConstraints2D savedConstraints;

    void LockMovement()
    {
        if (movementLocked || player == null) return;
        savedMoveSpeed = player.moveSpeed;
        player.moveSpeed = 0f;
        lockedBody = player.GetComponent<Rigidbody2D>();
        if (lockedBody != null)
        {
            savedConstraints = lockedBody.constraints;
            lockedBody.linearVelocity = Vector2.zero;
            lockedBody.constraints = RigidbodyConstraints2D.FreezeAll;
        }
        movementLocked = true;
    }

    void HoldStill()
    {
        if (lockedBody == null) return;
        lockedBody.linearVelocity = Vector2.zero;
    }

    void UnlockMovement()
    {
        if (!movementLocked) return;
        if (player != null) player.moveSpeed = savedMoveSpeed;
        if (lockedBody != null)
        {
            lockedBody.constraints = savedConstraints;
            lockedBody.linearVelocity = Vector2.zero;
        }
        movementLocked = false;
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

    static void SetMinersArmed(bool armed)
    {
        foreach (MinerAI m in FindObjectsByType<MinerAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (!m.huntPigOnly) m.autoShoot = armed;
    }

    static MinerAI GoldMiner()
    {
        MinerAI best = null;
        float bestY = float.NegativeInfinity;
        foreach (MinerAI m in FindObjectsByType<MinerAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (m.huntPigOnly || m.hook == null || m.hookOrigin == null || !m.hook.gameObject.activeInHierarchy) continue;
            float y = m.hookOrigin.position.y;
            if (best != null && y <= bestY) continue;
            best = m;
            bestY = y;
        }
        return best;
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

    // A flashlight pool on every spot. Words get a white arrow above the pool and a line beside it.
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
            mark.hole.enabled = on && !flashlight;
            mark.ring.enabled = false;
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
            bool words = mark.text.Length > 0;
            mark.arrow.gameObject.SetActive(words);
            mark.label.gameObject.SetActive(words);
            if (words) mark.label.text = mark.text;
        }
        PushFlashlight();
        PlaceCallouts();
    }

    readonly Vector4[] flashlightSpots = new Vector4[8];

    // One soft pool per lit spot. The dark sprite's own color is the shadow; the shader cuts the light out of it.
    void PushFlashlight()
    {
        if (!flashlight || dark == null) return;
        int count = 0;
        foreach (Callout mark in callouts)
        {
            if (!mark.used || count >= flashlightSpots.Length) continue;
            flashlightSpots[count++] = new Vector4(mark.at.x, mark.at.y, mark.radius, 0f);
        }
        for (int i = count; i < flashlightSpots.Length; i++) flashlightSpots[i] = Vector4.zero;
        dark.material.SetFloat("_Count", count);
        dark.material.SetVectorArray("_Spots", flashlightSpots);
    }

    Callout MakeCallout()
    {
        var holeGo = new GameObject("TutorialHole");
        holeGo.transform.SetParent(transform, false);
        SpriteMask mask = holeGo.AddComponent<SpriteMask>();
        mask.sprite = hole.sprite;
        TutorialStep.LimitLightMask(mask);

        var ringGo = new GameObject("TutorialRing");
        ringGo.transform.SetParent(transform, false);
        SpriteRenderer extra = ringGo.AddComponent<SpriteRenderer>();
        extra.sprite = ring.sprite;
        extra.color = Color.white;
        extra.sortingOrder = 110;
        extra.enabled = false;

        var arrowGo = new GameObject("TutorialArrow");
        arrowGo.transform.SetParent(transform, false);
        SpriteRenderer arrowRenderer = arrowGo.AddComponent<SpriteRenderer>();
        arrowRenderer.sprite = arrow.GetComponent<SpriteRenderer>().sprite;
        arrowRenderer.color = Color.white;
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
        words.alignment = TextAlignmentOptions.MidlineLeft;
        words.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        Vector2 textSize = words.GetPreferredValues(words.text, MaxTextWidth, 0f);
        textSize.x = Mathf.Clamp(textSize.x + 0.2f, 0.6f, MaxTextWidth);
        textSize.y = Mathf.Max(0.45f, textSize.y);
        words.rectTransform.sizeDelta = textSize;

        // White arrow just above the pool, pointing down. The line sits to its right.
        const float arrowScale = 1.05f;
        mark.arrow.localScale = Vector3.one * arrowScale;
        mark.arrow.rotation = Quaternion.identity;
        Vector2 arrowAt = mark.at + Vector2.up * (mark.radius + 0.55f);
        mark.arrow.position = arrowAt;

        Rect view = CameraView();
        Vector2 side = arrowAt + new Vector2(0.42f + textSize.x * 0.5f, 0.16f);
        side = KeepClear(side, view, mark.radius, textSize, mark.at);
        words.transform.position = side;
    }

    // Stay inside the camera, and slide only as far as it takes to clear the light, the pig and other lines.
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
        dark.color = Color.white;
        dark.sortingOrder = 100;
        Shader shade = Resources.Load<Shader>("Shaders/TutorialFlashlight");
        flashlight = shade != null;
        if (flashlight)
        {
            var cover = new Material(shade);
            cover.SetFloat("_Darkness", darkness);
            dark.material = cover;
            dark.maskInteraction = SpriteMaskInteraction.None;
        }
        else dark.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;
        dark.enabled = false;

        var holeGo = new GameObject("TutorialHole");
        holeGo.transform.SetParent(transform, false);
        hole = holeGo.AddComponent<SpriteMask>();
        TutorialStep.LimitLightMask(hole);
        hole.sprite = MakeSprite(64, (x, y) => x * x + y * y <= 1f ? 1f : 0f);

        var ringGo = new GameObject("TutorialRing");
        ringGo.transform.SetParent(transform, false);
        ring = ringGo.AddComponent<SpriteRenderer>();
        ring.sprite = MakeSprite(64, (x, y) =>
        {
            float r = Mathf.Sqrt(x * x + y * y);
            return r <= 1f && r >= 0.86f ? 1f : 0f;
        });
        ring.color = Color.white;
        ring.sortingOrder = 110;
        ring.enabled = false;

        var arrowGo = new GameObject("TutorialArrow");
        arrowGo.transform.SetParent(transform, false);
        var arrowRenderer = arrowGo.AddComponent<SpriteRenderer>();
        arrowRenderer.sprite = MakeSprite(64, (x, y) =>
        {
            bool shaft = y > -0.05f && y < 0.95f && Mathf.Abs(x) < 0.16f;
            float t = Mathf.InverseLerp(-0.02f, -0.98f, y);
            bool head = y <= -0.02f && y > -0.98f && Mathf.Abs(x) < Mathf.Lerp(0.46f, 0.02f, t);
            return shaft || head ? 1f : 0f;
        });
        arrowRenderer.color = Color.white;
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

    // WASD follows the player's spotlight; the rope-cut instruction stays fixed.
    void BuildKeys()
    {
        var root = new GameObject("TutorialKeys", typeof(RectTransform), typeof(Canvas));
        keysCanvas = root.GetComponent<Canvas>();
        keysCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        keysCanvas.sortingOrder = 530;
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        wasdKeys = new GameObject("WASD", typeof(RectTransform));
        wasdKeys.transform.SetParent(root.transform, false);
        RectTransform pad = wasdKeys.GetComponent<RectTransform>();
        pad.anchorMin = pad.anchorMax = new Vector2(0.5f, 0.5f);
        pad.pivot = new Vector2(0.5f, 0f);
        pad.sizeDelta = new Vector2(202f, 132f);
        CanvasGroup keyOpacity = wasdKeys.AddComponent<CanvasGroup>();
        keyOpacity.alpha = 0.45f;
        keyOpacity.interactable = false;
        keyOpacity.blocksRaycasts = false;
        Key(pad, "W", new Vector2(70f, 70f));
        Key(pad, "A", new Vector2(0f, 0f));
        Key(pad, "S", new Vector2(70f, 0f));
        Key(pad, "D", new Vector2(140f, 0f));
        wasdKeys.SetActive(false);

        cutLine = new GameObject("CutLine", typeof(RectTransform));
        cutLine.transform.SetParent(root.transform, false);
        RectTransform line = cutLine.GetComponent<RectTransform>();
        line.anchorMin = line.anchorMax = new Vector2(1f, 0.5f);
        line.pivot = new Vector2(1f, 0.5f);
        line.anchoredPosition = new Vector2(-40f, 0f);
        line.sizeDelta = new Vector2(760f, 64f);
        Caption(line, "Press", new Vector2(-208f, 0f), new Vector2(160f, 52f), new Vector2(1f, 0.5f));
        var bar = new GameObject("Space", typeof(RectTransform), typeof(Image));
        bar.transform.SetParent(line, false);
        RectTransform barRect = bar.GetComponent<RectTransform>();
        barRect.anchorMin = barRect.anchorMax = new Vector2(0.5f, 0.5f);
        barRect.pivot = new Vector2(0.5f, 0.5f);
        barRect.anchoredPosition = new Vector2(-112f, 0f);
        barRect.sizeDelta = new Vector2(168f, 46f);
        Image cap = bar.GetComponent<Image>();
        cap.sprite = KeyCap();
        cap.color = Color.white;
        cap.raycastTarget = false;
        Caption(barRect, "J", Vector2.zero, new Vector2(156f, 40f), new Vector2(0.5f, 0.5f), 22f,
            new Color(0.16f, 0.13f, 0.1f));
        Caption(line, "to cut the rope.", new Vector2(-16f, 0f), new Vector2(380f, 52f), new Vector2(0f, 0.5f));
        cutLine.SetActive(false);
    }

    void Key(RectTransform pad, string letter, Vector2 at)
    {
        var key = new GameObject(letter, typeof(RectTransform), typeof(Image));
        key.transform.SetParent(pad, false);
        RectTransform rect = key.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.anchoredPosition = at;
        rect.sizeDelta = new Vector2(62f, 62f);
        Image face = key.GetComponent<Image>();
        face.sprite = KeyCap();
        face.color = Color.white;
        face.raycastTarget = false;
        Caption(rect, letter, Vector2.zero, new Vector2(62f, 62f), new Vector2(0.5f, 0.5f), 30f,
            new Color(0.16f, 0.13f, 0.1f));
    }

    void Caption(RectTransform parent, string text, Vector2 at, Vector2 size, Vector2 pivot, float fontSize = 34f, Color? color = null)
    {
        var words = new GameObject("Caption", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        words.transform.SetParent(parent, false);
        words.font = label != null ? label.font : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        words.fontSize = fontSize;
        words.alignment = pivot.x > 0.9f ? TextAlignmentOptions.MidlineRight
            : pivot.x < 0.1f ? TextAlignmentOptions.MidlineLeft
            : TextAlignmentOptions.Center;
        words.color = color ?? Color.white;
        words.text = text;
        words.raycastTarget = false;
        RectTransform rect = words.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = at;
    }

    void ShowWasd(bool on)
    {
        if (wasdKeys != null) wasdKeys.SetActive(on);
        if (on) PositionWasd();
    }

    void PositionWasd()
    {
        if (wasdKeys == null || !wasdKeys.activeInHierarchy || player == null || keysCanvas == null) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector2 at = player.transform.position;
        float lightTop = at.y + 0.9f;
        foreach (Callout mark in callouts)
            if (mark.used && (mark.at - at).sqrMagnitude <= mark.radius * mark.radius)
                lightTop = Mathf.Max(lightTop, mark.at.y + mark.radius);
        TutorialSpotlight spotlight = TutorialSpotlight.Active;
        if (spotlight != null && (spotlight.target == null || spotlight.target == player.transform))
            lightTop = Mathf.Max(lightTop, at.y + spotlight.radius);

        // Clear the light's arrow as well as its circular opening.
        Vector3 screen = cam.WorldToScreenPoint(new Vector3(at.x, lightTop + 1.1f, 0f));
        if (screen.z <= 0f) return;
        RectTransform pad = wasdKeys.GetComponent<RectTransform>();
        Rect view = cam.pixelRect;
        float width = pad.rect.width * keysCanvas.scaleFactor;
        float height = pad.rect.height * keysCanvas.scaleFactor;
        screen.x = Mathf.Clamp(screen.x, view.xMin + width * 0.5f + 12f, view.xMax - width * 0.5f - 12f);
        screen.y = Mathf.Clamp(screen.y, view.yMin + 12f, view.yMax - height - 12f);
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)keysCanvas.transform,
            screen, null, out Vector3 position)) pad.position = position;
    }

    void ShowCutLine(bool on)
    {
        if (cutLine != null) cutLine.SetActive(on);
    }

    static Sprite keyCap;

    static Sprite KeyCap()
    {
        if (keyCap != null) return keyCap;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        float radius = 12f;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float inset = 2f;
                float left = inset + radius, right = n - inset - radius, bottom = inset + radius, top = n - inset - radius;
                float cx = Mathf.Clamp(px, left, right), cy = Mathf.Clamp(py, bottom, top);
                float dist = Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy));
                float alpha = Mathf.Clamp01(radius - dist + 0.6f);
                float edge = Mathf.Clamp01(radius - dist);
                Color fill = edge < 1.4f ? new Color(0.72f, 0.72f, 0.7f, alpha) : new Color(0.97f, 0.97f, 0.95f, alpha);
                tex.SetPixel(x, y, fill);
            }
        }
        tex.Apply();
        keyCap = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        return keyCap;
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
