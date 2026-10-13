using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Owner: D (Hook & UI)
public class HUDController : MonoBehaviour
{
    public TMP_Text playerMoneyText;
    public TMP_Text minerMoneyText;
    public TMP_Text timerText;
    public TMP_Text livesText;      // optional

    public GameObject endPanel;
    public TMP_Text resultText;
    public TMP_Text detailText;

    [Header("Timer")]
    public float timerWarningTime = 10f;
    public Color timerWarningColor = Color.red;
    public float timerPulseTime = 5f;     // pulse once per second below this
    public float timerPulseScale = 1.2f;

    [Header("Money")]
    public Color moneyFlashColor = new Color(1f, 0.85f, 0.2f);  // gold
    public float moneyFlashScale = 1.25f;
    public float moneyFlashTime = 0.3f;
    public Vector2 popupOffset = new Vector2(0f, -30f);  // "+$600" appears here, relative to the label
    public float popupRise = 40f;
    public float popupTime = 0.8f;
    public float trailingAlpha = 0.7f;    // the side that is behind is dimmed

    [Header("Lives")]
    public Color lifeLostColor = Color.red;
    public Color lifeGainedColor = Color.green;

    [Header("Bars (skills side by side at the top centre, money side by side at the bottom centre)")]
    public Vector2 barSize = new Vector2(240f, 18f);
    public float barSpacing = 12f;        // gap between two bars in a row
    public float skillBarsTop = 20f;      // pixels from the top of the screen
    public float moneyBarsBottom = 90f;   // pixels from the bottom (clear of LevelLoader's label lines)
    public Color barBack = new Color(0f, 0f, 0f, 0.55f);
    public Color playerMoneyBar = new Color(1f, 0.55f, 0.75f);    // pink
    public Color minerMoneyBar = new Color(0.6f, 0.6f, 0.6f);     // gray
    public Color digestBarFill = new Color(0.65f, 0.9f, 0.55f);   // matches PigController.digestTint
    public Color digestBarReady = new Color(1f, 0.85f, 0.2f);
    public Color blockBarActive = new Color(0.45f, 0.75f, 1f);    // matches PigController.blockTint
    public Color blockBarCooldown = new Color(0.6f, 0.6f, 0.6f);

    private Label playerMoney;
    private Label minerMoney;
    private Label lives;
    private Color timerBaseColor;
    private Vector3 timerBaseScale;
    private bool roundHasLives;
    private PigController pig;
    private GUIStyle barStyle;

    // Last values seen, to detect changes (not set until the first Update)
    private bool hasLast;
    private int lastPlayerMoney;
    private int lastMinerMoney;
    private int lastLives;

    private readonly List<Popup> popups = new List<Popup>();

    void Start()
    {
        if (endPanel != null) endPanel.SetActive(false);
        if (timerText != null)
        {
            timerBaseColor = timerText.color;
            timerBaseScale = timerText.transform.localScale;
        }

        playerMoney = new Label(playerMoneyText);
        minerMoney = new Label(minerMoneyText);
        lives = new Label(livesText);
        pig = FindFirstObjectByType<PigController>();

        // A round "has Lives" if it starts with some or a Life pickup is in the scene
        GameManager gm = GameManager.Instance;
        roundHasLives = (gm != null && gm.startingLives > 0)
            || FindObjectsByType<LifePickup>(FindObjectsSortMode.None).Length > 0;
    }

    void Update()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) return;

        DetectChanges(gm);
        UpdateMoney(gm);
        UpdateTimer(gm);
        UpdateLives(gm);
        UpdatePopups();
        UpdateEndPanel(gm);
    }

    // Flash labels and spawn popups when a value changes (never on the first frame)
    void DetectChanges(GameManager gm)
    {
        if (hasLast)
        {
            if (gm.PlayerMoney != lastPlayerMoney)
            {
                playerMoney.Flash(moneyFlashColor, moneyFlashTime);
                SpawnPopup(playerMoneyText, gm.PlayerMoney - lastPlayerMoney);
            }
            if (gm.MinerMoney != lastMinerMoney)
            {
                minerMoney.Flash(moneyFlashColor, moneyFlashTime);
                SpawnPopup(minerMoneyText, gm.MinerMoney - lastMinerMoney);
            }
            if (gm.Lives != lastLives)
            {
                lives.Flash(gm.Lives < lastLives ? lifeLostColor : lifeGainedColor, moneyFlashTime);
            }
        }

        hasLast = true;
        lastPlayerMoney = gm.PlayerMoney;
        lastMinerMoney = gm.MinerMoney;
        lastLives = gm.Lives;
    }

    void UpdateMoney(GameManager gm)
    {
        // The leader is bold, the side behind is dimmed; a tie shows both normally
        playerMoney.bold = gm.PlayerMoney > gm.MinerMoney;
        minerMoney.bold = gm.MinerMoney > gm.PlayerMoney;
        playerMoney.alpha = gm.PlayerMoney < gm.MinerMoney ? trailingAlpha : 1f;
        minerMoney.alpha = gm.MinerMoney < gm.PlayerMoney ? trailingAlpha : 1f;

        if (playerMoneyText != null) playerMoneyText.text = "Player: $" + gm.PlayerMoney.ToString("N0");
        if (minerMoneyText != null) minerMoneyText.text = "Miner: $" + gm.MinerMoney.ToString("N0");

        playerMoney.Apply(moneyFlashScale);
        minerMoney.Apply(moneyFlashScale);
    }

    void UpdateTimer(GameManager gm)
    {
        if (timerText == null) return;

        // Judge by the number on screen, so "0:10" is never red
        int seconds = Mathf.CeilToInt(gm.TimeLeft);
        timerText.text = (seconds / 60) + ":" + (seconds % 60).ToString("00");
        timerText.color = seconds < timerWarningTime ? timerWarningColor : timerBaseColor;

        // Heartbeat: pop right after each second ticks over, settle within ~0.3 s
        float pulse = 0f;
        if (gm.IsPlaying && seconds <= timerPulseTime)
            pulse = Mathf.Clamp01(1f - (seconds - gm.TimeLeft) / 0.3f);
        timerText.transform.localScale = timerBaseScale * Mathf.Lerp(1f, timerPulseScale, pulse);
    }

    void UpdateLives(GameManager gm)
    {
        if (livesText == null) return;

        if (gm.Lives > 0) roundHasLives = true;   // e.g. a Life spawned at runtime
        livesText.gameObject.SetActive(roundHasLives);
        livesText.text = "Life: " + gm.Lives;
        lives.Apply(moneyFlashScale);
    }

    void UpdateEndPanel(GameManager gm)
    {
        if (gm.IsPlaying || endPanel == null || endPanel.activeSelf) return;

        endPanel.SetActive(true);
        if (resultText != null) resultText.text = gm.PlayerWon ? "YOU WIN" : "YOU LOSE";
        if (detailText == null) return;

        string detail = gm.EndReason
            + "\nPlayer $" + gm.PlayerMoney.ToString("N0") + "  vs  Miner $" + gm.MinerMoney.ToString("N0");
        // GameManager counts a tie as a loss; say so, or it looks like a bug
        if (gm.PlayerMoney == gm.MinerMoney) detail += "\nTie - the miner wins ties";
        detailText.text = detail + "\nPress R to restart";
    }

    void OnGUI()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null || !gm.IsPlaying) return;

        DrawMoneyBars(gm);
        if (pig == null) return;

        // Only the skills this level uses; one bar alone is centred
        int skills = (pig.UsesDigest ? 1 : 0) + (pig.canBlock ? 1 : 0);
        int slot = 0;
        if (pig.UsesDigest) DrawDigestBar(BarInRow(slot++, skills, skillBarsTop));
        if (pig.canBlock) DrawBlockBar(BarInRow(slot, skills, skillBarsTop));
    }

    // The i-th of `count` bars in a centred row whose top edge is `y` pixels down the screen
    Rect BarInRow(int i, int count, float y)
    {
        float rowWidth = count * barSize.x + (count - 1) * barSpacing;
        float x = (Screen.width - rowWidth) / 2f + i * (barSize.x + barSpacing);
        return new Rect(x, y, barSize.x, barSize.y);
    }

    // Each side's share of all the money in the round: what both have plus the treasure still
    // out there. A side past half has won, whatever happens to the rest
    void DrawMoneyBars(GameManager gm)
    {
        int left = 0;
        foreach (Treasure t in Treasure.Active) left += t.value;
        foreach (Treasure t in Treasure.OnHooks) left += t.value;
        float pot = gm.PlayerMoney + gm.MinerMoney + left;

        float y = Screen.height - moneyBarsBottom;
        DrawBar(BarInRow(0, 2, y), pot > 0f ? gm.PlayerMoney / pot : 0f, playerMoneyBar,
                "You $" + gm.PlayerMoney.ToString("N0"));
        DrawBar(BarInRow(1, 2, y), pot > 0f ? gm.MinerMoney / pot : 0f, minerMoneyBar,
                "Miner $" + gm.MinerMoney.ToString("N0"));
    }

    // After eating, the bar fills back up over the digest time; full means the pig can eat again
    void DrawDigestBar(Rect bar)
    {
        float left = pig.DigestLeft;
        if (left > 0f)
            DrawBar(bar, Fraction(left, pig.DigestLength, true), digestBarFill, "Digesting " + left.ToString("0.0") + "s");
        else
            DrawBar(bar, 1f, digestBarReady, "Ready to eat");
    }

    // Blocking: the bar drains over the block. Cooldown: it fills back up. Full: Space blocks again
    void DrawBlockBar(Rect bar)
    {
        float blockLeft = pig.BlockLeft;
        float readyIn = pig.BlockReadyIn;
        if (blockLeft > 0f)
            DrawBar(bar, Fraction(blockLeft, pig.blockDuration, false), blockBarActive, "Blocking!");
        else if (readyIn > 0f)
            DrawBar(bar, Fraction(readyIn, pig.blockCooldown, true), blockBarCooldown, "Block in " + readyIn.ToString("0.0") + "s");
        else
            DrawBar(bar, 1f, blockBarActive, "[Space] Block ready");
    }

    // Share of the bar to fill with `left` of `total` seconds remaining
    static float Fraction(float left, float total, bool refilling)
    {
        float t = total > 0f ? Mathf.Clamp01(left / total) : 0f;
        return refilling ? 1f - t : t;
    }

    void DrawBar(Rect back, float filled, Color fill, string text)
    {
        Color old = GUI.color;
        GUI.color = barBack;
        GUI.DrawTexture(back, Texture2D.whiteTexture);
        GUI.color = fill;
        GUI.DrawTexture(new Rect(back.x, back.y, back.width * filled, back.height), Texture2D.whiteTexture);
        GUI.color = old;

        if (barStyle == null)
        {
            barStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            barStyle.normal.textColor = Color.black;
        }
        GUI.Label(back, text, barStyle);
    }

    // "+$600" next to a money label: a copy of the label, so it shares its font and size
    void SpawnPopup(TMP_Text source, int delta)
    {
        if (source == null || delta == 0) return;

        TMP_Text text = Instantiate(source, source.transform.parent);
        text.name = source.name + "Popup";
        text.text = (delta > 0 ? "+$" : "-$") + Mathf.Abs(delta).ToString("N0");
        text.color = moneyFlashColor;
        text.transform.localScale = Vector3.one;

        Vector2 start = source.rectTransform.anchoredPosition + popupOffset;
        text.rectTransform.anchoredPosition = start;
        popups.Add(new Popup { text = text, start = start });
    }

    // Rise and fade, then remove
    void UpdatePopups()
    {
        for (int i = popups.Count - 1; i >= 0; i--)
        {
            Popup p = popups[i];
            p.age += Time.deltaTime;
            float t = popupTime > 0f ? p.age / popupTime : 1f;

            if (t >= 1f || p.text == null)
            {
                if (p.text != null) Destroy(p.text.gameObject);
                popups.RemoveAt(i);
                continue;
            }

            p.text.rectTransform.anchoredPosition = p.start + Vector2.up * (popupRise * t);
            Color c = moneyFlashColor;
            c.a *= 1f - t;
            p.text.color = c;
        }
    }

    class Popup
    {
        public TMP_Text text;
        public Vector2 start;
        public float age;
    }

    // A HUD label that can flash (color + scale), be bold, or be dimmed
    class Label
    {
        public bool bold;
        public float alpha = 1f;

        private readonly TMP_Text text;
        private readonly Color baseColor;
        private readonly Vector3 baseScale;
        private readonly FontStyles baseStyle;
        private Color flashColor;
        private float flashTime;
        private float flashLeft;

        public Label(TMP_Text text)
        {
            this.text = text;
            if (text == null) return;
            baseColor = text.color;
            baseScale = text.transform.localScale;
            baseStyle = text.fontStyle;
        }

        public void Flash(Color color, float time)
        {
            flashColor = color;
            flashTime = time;
            flashLeft = time;
        }

        public void Apply(float flashScale)
        {
            if (text == null) return;

            flashLeft = Mathf.Max(0f, flashLeft - Time.deltaTime);
            float t = flashTime > 0f ? flashLeft / flashTime : 0f;   // 1 = just changed, 0 = done

            Color resting = baseColor;
            resting.a *= alpha;
            text.color = Color.Lerp(resting, flashColor, t);
            text.transform.localScale = baseScale * Mathf.Lerp(1f, flashScale, t);
            text.fontStyle = bold ? baseStyle | FontStyles.Bold : baseStyle;
        }
    }
}
