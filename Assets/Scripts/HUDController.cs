using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Owner: D (Hook & UI)
public class HUDController : MonoBehaviour
{
    public TMP_Text playerMoneyText;
    public TMP_Text minerMoneyText;
    public TMP_Text timerText;
    public Button mapButton;        // the Pause button, just left of the timer; HUDBuilder wires it, or Start makes one
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

    [Header("Pause popup")]
    public Color popupCard = new Color(0.99f, 0.99f, 0.99f);
    public Color popupTitle = new Color(0.07f, 0.07f, 0.07f);
    public Color popupMessage = new Color(0.4f, 0.4f, 0.4f);
    public Color popupButton = new Color(0.953f, 0.953f, 0.953f);

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
    private GameObject pausePopup;
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
        EnsureMapButton();

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
        PlaceMapButton();
    }

    // A Map button just left of the clock. Only when this round was opened from the map: editor
    // N/P play of the Game scene has no node to return to. Leaving mid-round does not clear the
    // node; leaving after a win does, so the clear is not lost to the auto-return timer.
    void EnsureMapButton()
    {
        if (timerText == null) return;

        Canvas canvas = timerText.canvas;
        if (canvas != null && canvas.GetComponent<GraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<GraphicRaycaster>();
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var events = new GameObject("EventSystem");
            events.AddComponent<EventSystem>();
            events.AddComponent<StandaloneInputModule>();
        }

        if (mapButton == null) mapButton = CreateMapButton(timerText);
        // A HUD saved before the pause popup still says "Exit"
        TMP_Text buttonLabel = mapButton.GetComponentInChildren<TMP_Text>(true);
        if (buttonLabel != null) buttonLabel.text = "Pause";
        mapButton.onClick.AddListener(OpenPause);
        PlaceMapButton();
    }

    public static Button CreateMapButton(TMP_Text timer)
    {
        var go = new GameObject("MapButton", typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(timer.rectTransform.parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(92f, 42f);

        Image image = go.GetComponent<Image>();
        image.sprite = FlatSprite();
        image.color = Color.white;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.20f, 0.27f, 0.38f, 1f);
        colors.highlightedColor = new Color(0.30f, 0.38f, 0.52f, 1f);
        colors.pressedColor = new Color(0.14f, 0.18f, 0.28f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.SetParent(rect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
        label.font = timer.font;
        label.text = "Pause";
        label.fontSize = 22f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.86f, 0.90f, 0.96f, 1f);
        label.raycastTarget = false;
        return button;
    }

    void PlaceMapButton()
    {
        if (mapButton == null || timerText == null) return;

        bool show = flow != null && flow.CurrentNode != null;
        if (mapButton.gameObject.activeSelf != show) mapButton.gameObject.SetActive(show);
        if (!show) return;

        RectTransform timerRect = timerText.rectTransform;
        RectTransform rect = mapButton.GetComponent<RectTransform>();
        float digits = Mathf.Max(72f, timerText.preferredWidth);
        float scale = Mathf.Max(1f, timerRect.localScale.x);
        rect.anchoredPosition = new Vector2(timerRect.anchoredPosition.x - digits * scale - 14f, timerRect.anchoredPosition.y);
    }

    public bool IsPaused => pausePopup != null && pausePopup.activeSelf;

    // Pause freezes the round (time scale 0: the clock, hooks and the character all stop) and asks
    // "Quit Game?". Cancel picks up where it left off; Quit goes back to the map
    void OpenPause()
    {
        if (IsPaused) return;
        if (pausePopup == null) BuildPausePopup();
        pausePopup.transform.SetAsLastSibling();
        pausePopup.SetActive(true);
        Time.timeScale = 0f;
    }

    void ResumeFromPause()
    {
        pausePopup.SetActive(false);
        Time.timeScale = 1f;
    }

    // Leaving mid-round does not clear the node; leaving after a win does, so the clear is not lost
    // to the auto-return timer
    void QuitToMap()
    {
        ResumeFromPause();
        if (flow == null || flow.CurrentNode == null) return;

        GameManager gm = GameManager.Instance;
        if (gm != null && !gm.IsPlaying && gm.PlayerWon) flow.CompleteCurrentNode();
        else flow.ReturnToMap();
    }

    // R (restart) and N / P reload the scene even while paused; don't carry the frozen time over
    void OnDestroy()
    {
        if (IsPaused) Time.timeScale = 1f;
    }

    // A dimmed screen (it also blocks clicks behind it) with a centered card. Built here rather than
    // by HUDBuilder, so the Game scene's saved HUD needs no rebuild
    void BuildPausePopup()
    {
        pausePopup = new GameObject("PausePopup", typeof(RectTransform), typeof(Image));
        RectTransform dim = (RectTransform)pausePopup.transform;
        dim.SetParent(timerText.canvas.transform, false);
        dim.anchorMin = Vector2.zero;
        dim.anchorMax = Vector2.one;
        dim.offsetMin = Vector2.zero;
        dim.offsetMax = Vector2.zero;
        pausePopup.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        RectTransform card = PopupBox(dim, "Card", popupCard, new Vector2(760f, 300f), Vector2.zero);
        PopupText(card, "Title", "Quit Game?", 36f, FontStyles.Bold, popupTitle, new Vector2(0f, 95f));
        PopupText(card, "Message", "Are you sure you want to quit?", 26f, FontStyles.Normal, popupMessage, new Vector2(0f, 35f));
        PopupButton(card, "Cancel", new Vector2(-182f, -81f), ResumeFromPause);
        PopupButton(card, "Quit", new Vector2(182f, -81f), QuitToMap);
        pausePopup.SetActive(false);
    }

    RectTransform PopupBox(Transform parent, string name, Color color, Vector2 size, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;

        Image image = go.GetComponent<Image>();
        image.sprite = RoundedSprite();
        image.type = Image.Type.Sliced;
        image.color = color;
        return rect;
    }

    void PopupText(RectTransform parent, string name, string text, float size, FontStyles style, Color color, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(parent.sizeDelta.x - 48f, size * 1.6f);
        rect.anchoredPosition = position;

        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
        label.font = timerText.font;
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.alignment = TextAlignmentOptions.Center;
        label.color = color;
        label.raycastTarget = false;
    }

    // A light gray button with a bold label that darkens a little on hover and press
    void PopupButton(RectTransform card, string text, Vector2 position, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rect = PopupBox(card, text + "Button", popupButton, new Vector2(348f, 90f), position);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = rect.GetComponent<Image>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.95f, 0.95f, 0.95f);
        colors.pressedColor = new Color(0.88f, 0.88f, 0.88f);
        colors.selectedColor = Color.white;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.onClick.AddListener(onClick);
        PopupText(rect, "Label", text, 30f, FontStyles.Bold, popupTitle, Vector2.zero);
    }

    static Sprite roundedSprite;

    // A white rounded square, sliced so a box of any size keeps 16 px corners
    static Sprite RoundedSprite()
    {
        if (roundedSprite != null) return roundedSprite;
        const int size = 64;
        const float radius = 16f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Distance outside the corner circle, 0 along the straight edges and inside
                float dx = Mathf.Max(0f, Mathf.Max(radius - (x + 0.5f), x + 0.5f - (size - radius)));
                float dy = Mathf.Max(0f, Mathf.Max(radius - (y + 0.5f), y + 0.5f - (size - radius)));
                float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        roundedSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        return roundedSprite;
    }

    static Sprite flatSprite;

    static Sprite FlatSprite()
    {
        if (flatSprite != null) return flatSprite;
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        flatSprite = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        return flatSprite;
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
            int held = skill.stock;
            if (skill.id == "swift" || skill.id == "ration" || skill.id == "shield" || skill.id == "brace")
            {
                if (skill.armed || skill.active || skill.secondsLeft > 0f) held++;
            }
            if (held <= 0) continue;
            SetValue(ItemRow(count++), ItemName(skill.id) + " x" + held, waitColor);
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
