using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

// Owner: D (Hook & UI)
public class HUDController : MonoBehaviour
{
    public TMP_Text playerMoneyText;
    public TMP_Text minerMoneyText;
    public TMP_Text timerText;
    public TMP_Text livesText;      // optional

    [Header("Top bar and sidebar (HUDBuilder wires these; all optional)")]
    public TMP_Text levelText;
    public GameObject lifeSection;   // hidden with the Life count when the round has no Lives
    public GameObject walletSection;
    public TMP_Text walletText;
    public GameObject skillSection;
    public TMP_Text skillText;
    public RectTransform skillBarFill;
    public TMP_Text digestText;
    public GameObject itemsSection;
    public TMP_Text itemRowTemplate;
    public TMP_Text itemKeyText;

    [Header("Sidebar colors")]
    public Color readyColor = new Color(0.55f, 0.9f, 0.6f);    // green
    public Color activeColor = new Color(0.45f, 0.65f, 1f);    // blue, like the block tint
    public Color waitColor = Color.white;
    public Color noneColor = new Color(0.68f, 0.74f, 0.85f);   // the section titles' gray

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

    private Label playerMoney;
    private Label minerMoney;
    private Label lives;
    private Color timerBaseColor;
    private Vector3 timerBaseScale;
    private bool roundHasLives;
    private LevelLoader levelLoader;
    private PlayerCharacter player;
    private readonly List<TMP_Text> itemRows = new List<TMP_Text>();
    private GameFlow flow;
    private string endDetail;   // the result lines, set once when the round ends   // found, not GameFlow.Instance: that would create one in Dev scenes

    // Last values seen, to detect changes (not set until the first Update)
    private bool hasLast;
    private int lastPlayerMoney;
    private int lastMinerMoney;
    private int lastLives;

    private readonly List<Popup> popups = new List<Popup>();

    void Start()
    {
        // The arena goes in the space the top bar and sidebar leave (HUDLayout)
        GameplayCamera.Ensure(Camera.main);

        if (endPanel != null) endPanel.SetActive(false);
        if (timerText != null)
        {
            timerBaseColor = timerText.color;
            timerBaseScale = timerText.transform.localScale;
        }

        levelLoader = FindFirstObjectByType<LevelLoader>();
        flow = FindFirstObjectByType<GameFlow>();

        playerMoney = new Label(playerMoneyText);
        minerMoney = new Label(minerMoneyText);
        lives = new Label(livesText);

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
        UpdateLevel();
        UpdateSidebar();
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

        if (playerMoneyText != null) playerMoneyText.text = "$" + gm.PlayerMoney.ToString("N0");
        if (minerMoneyText != null) minerMoneyText.text = "$" + gm.MinerMoney.ToString("N0");

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
        // The whole section when there is one (the sidebar), else just the label (an older HUD)
        GameObject shown = lifeSection != null ? lifeSection : livesText.gameObject;
        shown.SetActive(roundHasLives);
        livesText.text = lifeSection != null ? "x" + gm.Lives : "Life: " + gm.Lives;
        lives.Apply(moneyFlashScale);
    }

    // The map node's name ("LEVEL 4") when the map sent us here, else "LV 4/10" for editor N/P play;
    // hidden in scenes without a level loader (Dev scenes)
    void UpdateLevel()
    {
        if (levelText == null) return;

        MapNode node = flow != null ? flow.CurrentNode : null;
        string text = "";
        if (node != null) text = node.displayName.ToUpperInvariant();
        else if (levelLoader != null && levelLoader.LevelCount > 0)
            text = "LV " + LevelLoader.CurrentLevel + "/" + levelLoader.LevelCount;

        levelText.gameObject.SetActive(text != "");
        levelText.text = text;
    }

    // Wallet, skill, digest and items. Rows with nothing to show hide: the tutorial has no record
    // (no wallet, no items), and a level without a skill or digest hides those rows
    void UpdateSidebar()
    {
        // Champions are spawned at level start, so keep looking until one is there
        if (player == null) player = FindFirstObjectByType<PlayerCharacter>();
        CrossLevelStatus record = PlayerCharacter.Record;

        UpdateWallet(record);
        bool hasSkill = UpdateSkill();
        bool hasDigest = UpdateDigest();
        if (skillSection != null) skillSection.SetActive(hasSkill || hasDigest);
        UpdateItems(record);
        UpdateDividers();
    }

    void UpdateWallet(CrossLevelStatus record)
    {
        if (walletSection == null) return;
        walletSection.SetActive(record != null);
        if (record != null && walletText != null) walletText.text = "$" + record.getWallet().ToString("N0");
    }

    // "Active" while the skill runs, then the seconds until Space works again with the bar filling
    // back up, then "Ready". Returns whether the row is shown
    bool UpdateSkill()
    {
        if (skillText == null) return false;
        bool shown = player != null && player.SkillHint != "";
        SetRow(skillText, shown);
        if (skillBarFill != null) skillBarFill.parent.gameObject.SetActive(shown);
        if (!shown) return false;

        float readyIn = player.SkillReadyIn;
        if (player.SkillActive) SetValue(skillText, "Active", activeColor);
        else if (readyIn > 0f) SetValue(skillText, Seconds(readyIn), waitColor);
        else SetValue(skillText, "Ready", readyColor);

        float fill = readyIn > 0f && player.SkillWait > 0f ? 1f - readyIn / player.SkillWait : 1f;
        if (skillBarFill != null) skillBarFill.anchorMax = new Vector2(Mathf.Clamp01(fill), 1f);
        return true;
    }

    // Seconds left in the digest (in the digest's green tint), else "Ready". Returns whether shown
    bool UpdateDigest()
    {
        if (digestText == null) return false;
        bool shown = player != null && player.UsesDigest;
        SetRow(digestText, shown);
        if (!shown) return false;

        if (player.CanEat) SetValue(digestText, "Ready", readyColor);
        else SetValue(digestText, Seconds(player.DigestLeft), player.digestTint);
        return true;
    }

    // One row per store item the player owns ("Shield x2"), or "None"
    void UpdateItems(CrossLevelStatus record)
    {
        if (itemsSection == null || itemRowTemplate == null) return;
        itemsSection.SetActive(record != null);
        if (record == null) return;

        int count = 0;
        foreach (PlayerSkill skill in record.getSkills())
        {
            if (skill.stock <= 0) continue;
            SetValue(ItemRow(count++), ItemName(skill.id) + " x" + skill.stock, waitColor);
        }
        if (count == 0) SetValue(ItemRow(count++), "None", noneColor);
        for (int i = count; i < itemRows.Count; i++) itemRows[i].gameObject.SetActive(false);
    }

    // Rows are copies of the template, made once and reused
    TMP_Text ItemRow(int index)
    {
        while (itemRows.Count <= index)
        {
            TMP_Text row = Instantiate(itemRowTemplate, itemRowTemplate.transform.parent);
            row.name = "Item" + itemRows.Count;
            itemRows.Add(row);
        }
        itemRows[index].gameObject.SetActive(true);
        return itemRows[index];
    }

    // A divider shows only between two visible sections, so a hidden one never leaves two in a row
    void UpdateDividers()
    {
        if (lifeSection == null) return;
        Transform sidebar = lifeSection.transform.parent;
        GameObject pending = null;   // a divider under a visible section, waiting for one below it
        bool above = false;
        for (int i = 0; i < sidebar.childCount; i++)
        {
            GameObject child = sidebar.GetChild(i).gameObject;
            if (child.name == "Divider")
            {
                child.SetActive(false);
                if (above && pending == null) pending = child;
                continue;
            }
            if (!child.activeSelf) continue;
            if (pending != null) pending.SetActive(true);
            pending = null;
            above = true;
        }
    }

    // A sidebar value and its title, which HUDBuilder puts right above it
    static void SetRow(TMP_Text value, bool shown)
    {
        value.gameObject.SetActive(shown);
        int index = value.transform.GetSiblingIndex();
        if (index > 0) value.transform.parent.GetChild(index - 1).gameObject.SetActive(shown);
    }

    static void SetValue(TMP_Text text, string value, Color color)
    {
        text.text = value;
        text.color = color;
    }

    // "0.8s" under ten seconds, "12s" above
    static string Seconds(float seconds)
    {
        return seconds < 10f ? seconds.ToString("0.0", CultureInfo.InvariantCulture) + "s" : Mathf.CeilToInt(seconds) + "s";
    }

    // "shield" -> "Shield"
    static string ItemName(string id)
    {
        return string.IsNullOrEmpty(id) ? "?" : char.ToUpperInvariant(id[0]) + id.Substring(1);
    }

    void UpdateEndPanel(GameManager gm)
    {
        if (gm.IsPlaying || endPanel == null) return;

        if (!endPanel.activeSelf)
        {
            endPanel.SetActive(true);
            if (resultText != null) resultText.text = gm.PlayerWon ? "YOU WIN" : "YOU LOSE";

            endDetail = gm.EndReason;
            // Levels without money (the block tutorial) skip the score line
            if (gm.PlayerMoney + gm.MinerMoney > 0)
                endDetail += "\nPlayer $" + gm.PlayerMoney.ToString("N0") + "  vs  Miner $" + gm.MinerMoney.ToString("N0");
            // GameManager counts a tie as a loss; say so, or it looks like a bug
            if (!gm.PlayerWon && gm.PlayerMoney == gm.MinerMoney) endDetail += "\nTie - the miner wins ties";
        }

        // Every frame: the next-level countdown ticks
        if (detailText != null) detailText.text = endDetail + "\n" + NextStep(gm);
    }

    // The one place that says what happens next (LevelLoader no longer draws it under the arena)
    string NextStep(GameManager gm)
    {
        if (!gm.PlayerWon) return "Press R to retry";
        if (flow != null && flow.CurrentNode != null) return "Back to the map...";
        string hint = levelLoader != null ? levelLoader.NextStepHint : "";
        return hint != "" ? hint : "Press R to restart";
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
