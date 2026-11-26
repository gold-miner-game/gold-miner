using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Owner: D (Hook & UI)
public class HUDController : MonoBehaviour
{
    public HUDVariant variant;      // which HUDTheme HUDBuilder built this HUD from (A/B playtests)

    [Header("HUD Beta (top bar only; HUDBuilder wires these)")]
    public RectTransform topBar;     // Beta: the row of panels. Else the timer's parent is the top bar
    public GameObject moneyPanel;    // Beta: player money / timer / level gold progress
    public GameObject levelPanel;    // Beta: LEVEL n + Pause, hidden on the maze
    public GameObject coinsPanel;    // Beta: the wallet (coin + amount), shown only on the maze, in LevelPause's place
    public TMP_Text coinsText;
    public GameObject lifeRow;       // Beta: LIFE xN in PigStatus, in the SKILL / DIGEST rows' place when neither shows (the world map)
    public Color pillSkillInk = new Color(0.086f, 0.196f, 0.302f);   // READY / ACTIVE on the full skill pill
    public Color pillDigestInk = new Color(0.118f, 0.251f, 0.071f);  // CAN EAT on the full digest pill
    public Color pillWaitInk = Color.white;                          // the seconds while a pill refills

    public TMP_Text playerMoneyText;
    public TMP_Text minerMoneyText;
    public RectTransform playerBarFill;   // version B: the score bar's fills (null on version A)
    public RectTransform minerBarFill;
    public TMP_Text timerText;
    public Button mapButton;        // the Pause button, just left of the timer; HUDBuilder wires it, or Start makes one
    public TMP_Text livesText;      // optional

    [Header("Top bar and sidebar (HUDBuilder wires these; all optional)")]
    public TMP_Text levelText;
    public RectTransform sidebar;    // v2 HUD wires it; an older HUD finds it as the Life section's parent
    public GameObject statusSection; // v2 HUD: the Wallet + Life rows, hidden when both are
    public GameObject lifeSection;   // hidden with the Life count when the round has no Lives
    public GameObject walletSection;
    public TMP_Text walletText;
    public GameObject skillSection;
    public TMP_Text skillText;
    public RectTransform skillBarFill;
    public GameObject skillBlock;    // v2 HUD: the SKILL row + bar, hidden together (null on an older HUD)
    public TMP_Text digestText;
    public RectTransform digestBarFill;
    public GameObject digestBlock;
    public GameObject itemsSection;
    public TMP_Text itemRowTemplate;
    public TMP_Text itemKeyText;

    [Header("Sidebar colors")]
    public Color readyColor = new Color(0.55f, 0.9f, 0.6f);    // green
    public Color activeColor = new Color(0.45f, 0.65f, 1f);    // blue, like the block tint
    public Color waitColor = Color.white;
    public Color noneColor = new Color(0.68f, 0.74f, 0.85f);   // the section titles' gray
    public Color skillColor = new Color(0.45f, 0.65f, 1f);     // SKILL "Ready" (Figma color/accent/skill)
    public Color digestColor = new Color(0.65f, 0.9f, 0.55f);  // DIGEST value and bar when there is no character yet; else its digestTint

    // Pause and End popups: Figma Version3 "Pause (U2)" / "End - Win, Lose, Died (U1)" and the
    // HUD/PopupButton component. Colors come from the Beta theme (HUDTheme), not the scene, so the
    // saved HUD needs no rebuild
    enum PopupStyle { Primary, Secondary, Danger }
    static readonly Color PopupDanger = new Color(1f, 0.45f, 0.45f);
    static readonly Color PopupButtonFill = new Color(0.20f, 0.27f, 0.38f);   // color/bg/button
    static readonly Color PopupButtonInk = new Color(0.86f, 0.90f, 0.96f);    // color/text/button
    const float PopupButtonHeight = 72f;
    HUDTheme popupTheme;
    HUDTheme PopupTheme => popupTheme ??= HUDTheme.For(HUDVariant.Beta);

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
    public Color playerPopupColor = new Color(1f, 0.72f, 0.82f); // pig pink (color/accent/pig): the player's flash and "+$600"
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
    private Label lives;
    private Color timerBaseColor;
    private float timerHomeX;       // where the builder put the timer: the right edge when there is no Pause
    private Vector3 timerBaseScale;
    private bool roundHasLives;
    private LevelLoader levelLoader;
    private PlayerCharacter player;
    private readonly List<TMP_Text> itemRows = new List<TMP_Text>();
    private GameFlow flow;
    private GameObject pausePopup;
    RectTransform characterAvatar;
    RectTransform avatarFigure;
    GameObject avatarSource;
    private float timeScaleBeforePause = 1f;   // Pause restores it (a tutorial step or stand-still may have slowed time)
    private TMP_Text pauseInfo;       // "LEVEL 2 - 1:30 left", filled when Pause opens
    private TMP_Text pauseControls;   // the character's keys, filled when Pause opens
    private GameObject endCard;       // the result card, built once when the round ends
    private TMP_Text endRecord;       // a survival run's time and wave on the card (GameManager.NewRecordLine)
    private GameObject endActions;    // its buttons, hidden while a story or the skill summary plays
    private TMP_Text endFooter;       // "Returning to the map in 2..."
    private float returnAt = -1f;     // when GameFlow's own return timer should run out (unscaled time)
    private bool leavingToMap;

    // Last values seen, to detect changes (not set until the first Update)
    private bool hasLast;
    private int lastPlayerMoney;
    private int lastLives;

    private readonly List<Popup> popups = new List<Popup>();

    private bool mazeHudApplied;
    private TMP_Text mazeMoneyText;   // player's $ above LIFE in the sidebar (maze only)

    public const string LeftBackdropName = "LeftBackdrop";
    public const string BottomBackdropName = "BottomBackdrop";

    bool Beta => variant == HUDVariant.Beta;

    // Before any camera fits: Beta has no sidebar and a taller top bar
    void Awake()
    {
        HUDLayout.TopBarOnly = Beta;
    }

    void Start()
    {
        // The arena goes in the space the top bar and sidebar leave (HUDLayout)
        HUDLayout.TopBarOnly = Beta;
        GameplayCamera.Ensure(Camera.main);
        ApplyFrame();
        GameplayCamera fit = Camera.main != null ? Camera.main.GetComponent<GameplayCamera>() : null;
        if (fit != null) fit.Fit();   // a camera saved in the scene may have fitted before Awake

        if (endPanel != null) endPanel.SetActive(false);
        if (timerText != null)
        {
            timerBaseColor = timerText.color;
            timerBaseScale = timerText.transform.localScale;
            timerHomeX = timerText.rectTransform.anchoredPosition.x;
        }

        levelLoader = FindFirstObjectByType<LevelLoader>();
        flow = FindFirstObjectByType<GameFlow>();

        playerMoney = new Label(playerMoneyText);
        HideLegacyMinerScore();
        lives = new Label(livesText);
        EnsureMapButton();
        ApplyMazeHudIfNeeded();

        // A round "has Lives" if it starts with some or a Life pickup is in the scene
        GameManager gm = GameManager.Instance;
        roundHasLives = (gm != null && gm.startingLives > 0)
            || FindObjectsByType<LifePickup>(FindObjectsSortMode.None).Length > 0;
    }

    void Update()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) return;

        ApplyMazeHudIfNeeded();
        DetectChanges(gm);
        UpdateMoney(gm);
        UpdateMoneyBar(gm);
        UpdateTimer(gm);
        UpdateLives(gm);
        UpdateLevel();
        UpdateSidebar();
        UpdatePopups();
        UpdateEndPanel(gm);
        HandlePopupKeys(gm);
        UpdateRoundCues(gm);
    }

    // World-map maze: drop timer / $ vs $ / Pause / title so the grid is readable.
    // Reuse the LIFE sidebar block: show player $ above the LIFE xN row.
    void ApplyMazeHudIfNeeded()
    {
        if (mazeHudApplied) return;
        if (levelLoader == null) levelLoader = FindFirstObjectByType<LevelLoader>();
        bool maze = (levelLoader != null && levelLoader.IsMazeLevel) || MazeRuntime.Active != null;
        if (!maze) return;
        mazeHudApplied = true;

        // Beta: keep the top bar as in a level (PigStatus, Money, Coins) and drop only LevelPause.
        // The timer is blank on the hub (UpdateTimer)
        if (Beta)
        {
            if (levelPanel != null) levelPanel.SetActive(false);
            if (mapButton != null) mapButton.gameObject.SetActive(false);
            if (coinsPanel != null && coinsText != null)
            {
                // Coins keeps its Figma spot (left of where LevelPause was): the map's Quit button
                // sits at the right end of the bar
                coinsPanel.SetActive(true);
                mazeMoneyText = coinsText;
            }
            roundHasLives = true;
            ApplyFrame();
            GameplayCamera betaCam = FindFirstObjectByType<GameplayCamera>();
            if (betaCam != null) betaCam.Fit();
            return;
        }

        HUDLayout.HideTopBar = true;
        if (timerText != null)
        {
            Transform topBar = timerText.transform.parent;
            if (topBar != null) topBar.gameObject.SetActive(false);
        }
        if (mapButton != null) mapButton.gameObject.SetActive(false);

        roundHasLives = true;
        if (lifeSection != null) lifeSection.SetActive(true);
        else if (livesText != null) livesText.gameObject.SetActive(true);
        // Row sidebar: the WALLET row becomes MONEY. Older HUD: MONEY / $ above LIFE in its section
        if (StatRows && walletText != null)
        {
            mazeMoneyText = walletText;
            TMP_Text title = walletText.transform.parent.GetChild(0).GetComponent<TMP_Text>();
            if (title != null && title != walletText) title.text = "MONEY";
        }
        else EnsureMazeMoneyInLifeSection();

        // Keep SKILL / DIGEST cooldown rows. Only hide wallet/items (no store run on the maze).
        if (itemsSection != null) itemsSection.SetActive(false);
        if (walletSection != null) walletSection.SetActive(mazeMoneyText == walletText && walletText != null);

        ApplyFrame();
        GameplayCamera cam = FindFirstObjectByType<GameplayCamera>();
        if (cam != null) cam.Fit();
    }

    // Even HUDLayout.Gap spacing between the screen edge, the top bar, the sidebar and the arena.
    // Adds the left and bottom backdrops a HUD saved by an older HUDBuilder doesn't have
    void ApplyFrame()
    {
        RectTransform topBar = this.topBar != null ? this.topBar
            : timerText != null ? timerText.transform.parent as RectTransform : null;
        RectTransform sidebar = SidebarRect;
        RectTransform topBackdrop = transform.Find("TopBackdrop") as RectTransform;
        RectTransform sideBackdrop = transform.Find("SideBackdrop") as RectTransform;
        Color color = new Color(0.07f, 0.09f, 0.15f);
        Image topImage = topBackdrop != null ? topBackdrop.GetComponent<Image>() : null;
        if (topImage != null) color = topImage.color;
        HUDLayout.PlaceFrame(topBar, sidebar, topBackdrop, sideBackdrop,
            Backdrop(LeftBackdropName, color), Backdrop(BottomBackdropName, color));
    }

    // A flat band behind the panels, found by name or made once, kept first so it draws underneath
    RectTransform Backdrop(string name, Color color)
    {
        RectTransform rect = transform.Find(name) as RectTransform;
        if (rect != null) return rect;
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        rect = (RectTransform)go.transform;
        rect.SetParent(transform, false);
        rect.SetAsFirstSibling();
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    RectTransform SidebarRect => sidebar != null ? sidebar
        : lifeSection != null ? lifeSection.transform.parent as RectTransform : null;

    // StatusSection shows while either of its rows does, so its divider hides with it
    void UpdateStatusSection()
    {
        if (statusSection == null) return;
        bool any = (lifeSection != null && lifeSection.activeSelf) || (walletSection != null && walletSection.activeSelf);
        if (statusSection.activeSelf != any) statusSection.SetActive(any);
    }

    // HUD v2 sidebar: WALLET and LIFE are label / value rows (an older HUD stacks title over value)
    bool StatRows => lifeSection != null && lifeSection.GetComponent<HorizontalLayoutGroup>() != null;

    void EnsureMazeMoneyInLifeSection()
    {
        if (mazeMoneyText != null) return;
        if (lifeSection == null || livesText == null) return;

        // Title + value, same style as LIFE / xN, inserted at the top of that section.
        var titleGo = new GameObject("MoneyTitle", typeof(RectTransform));
        titleGo.transform.SetParent(lifeSection.transform, false);
        titleGo.transform.SetAsFirstSibling();
        var title = titleGo.AddComponent<TextMeshProUGUI>();
        title.text = "MONEY";
        title.alignment = TextAlignmentOptions.Center;
        title.fontSize = 18f;
        title.characterSpacing = 4f;
        title.color = noneColor;
        title.raycastTarget = false;
        if (livesText.font != null) title.font = livesText.font;

        var valueGo = new GameObject("MoneyValue", typeof(RectTransform));
        valueGo.transform.SetParent(lifeSection.transform, false);
        valueGo.transform.SetSiblingIndex(1);
        mazeMoneyText = valueGo.AddComponent<TextMeshProUGUI>();
        mazeMoneyText.text = "$0";
        mazeMoneyText.alignment = TextAlignmentOptions.Center;
        mazeMoneyText.fontSize = livesText.fontSize > 0f ? livesText.fontSize : 32f;
        mazeMoneyText.fontStyle = FontStyles.Bold;
        mazeMoneyText.color = Color.white;
        mazeMoneyText.raycastTarget = false;
        if (livesText.font != null) mazeMoneyText.font = livesText.font;
    }

    // Flash labels and spawn popups when a value changes (never on the first frame)
    void DetectChanges(GameManager gm)
    {
        if (hasLast)
        {
            if (gm.PlayerMoney != lastPlayerMoney)
            {
                playerMoney.Flash(playerPopupColor, moneyFlashTime);
                SpawnPopup(playerMoneyText, gm.PlayerMoney - lastPlayerMoney, playerPopupColor);
            }
            if (gm.Lives != lastLives)
            {
                lives.Flash(gm.Lives < lastLives ? lifeLostColor : lifeGainedColor, moneyFlashTime);
                if (gm.Lives < lastLives)
                {
                    FlashDamage();
                    GameplayCamera.Shake();   // F3: the view jolts with the red edge flash
                }
            }
            if (gm.IsPlaying && gm.UsesMoneyTarget && !targetCelebrated
                && lastPlayerMoney < gm.MoneyTarget && gm.PlayerMoney >= gm.MoneyTarget)
                CelebrateTarget();
        }

        hasLast = true;
        lastPlayerMoney = gm.PlayerMoney;
        lastLives = gm.Lives;
    }

    // Round cues (U6, U7): a banner that says the goal when the level starts and when it is reached,
    // the TARGET flag pulsing at that moment, and a red edge flash when a life is lost. Built at runtime
    // on the HUD canvas, so the saved scene needs no rebuild. They run on unscaled time (the round
    // slows when the player stands still) and hold while Pause is open
    const float BannerFade = 0.2f;
    const float BannerFadeOut = 0.4f;
    const float DamageFlashTime = 0.45f;
    const float DamageFlashAlpha = 0.8f;
    const float TargetPulseTime = 0.6f;
    const float GoalBannerWait = 0.8f;   // after the screen is free of tutorial steps and stories
    const float BannerGap = 10f;         // between the top bar and the banner's notch
    const float NotchHeight = 14f;

    private CanvasGroup banner;
    private TMP_Text bannerTitle;
    private TMP_Text bannerSub;
    private float bannerTime;
    private float bannerHold;
    private bool goalBannerShown;
    private bool targetCelebrated;
    private RawImage damageFlash;
    private float damageLeft;
    private float targetPulseLeft;
    private float cueQuietSince = -1f;   // unscaled: playing with no tutorial step or story on screen since then

    void UpdateRoundCues(GameManager gm)
    {
        float dt = IsPaused ? 0f : Time.unscaledDeltaTime;
        // A tutorial step (callout, spotlight) or a story owns the screen: one message at a time
        bool quiet = gm.IsPlaying && !IsPaused && !TutorialStep.AnyShowing && !TutorialDialogue.Playing;
        if (!quiet) cueQuietSince = -1f;
        else if (cueQuietSince < 0f) cueQuietSince = Time.unscaledTime;

        if (!goalBannerShown && GoalBannerAllowed(gm))
        {
            goalBannerShown = true;
            targetPulseLeft = TargetPulseTime;   // the flag the banner points at swells with it
            string pink = "#" + ColorUtility.ToHtmlStringRGB(PopupTheme.pig);
            ShowBanner("Collect <color=" + pink + ">$" + gm.MoneyTarget.ToString("N0") + "</color> to win",
                "Reach the TARGET flag before time runs out", 2.6f);
        }
        if (announceTitle != null)
        {
            ShowBanner(announceTitle, announceSub, announceHold);
            announceTitle = null;
        }
        AnimateBanner(gm, dt);
        AnimateDamageFlash(dt);
        AnimateTargetPulse(dt);
    }

    // Once per level, a moment after it starts (GameManager totals the level's gold in Start). Not on
    // the world map, in the tutorial, or while a story is being told. A level-start tutorial step
    // (level_01's "Press J to cut", 0.3 s in) shows first: the goal comes a moment after it is gone
    bool GoalBannerAllowed(GameManager gm)
    {
        if (!gm.IsPlaying || !gm.UsesMoneyTarget || gm.MoneyTarget <= 0 || timerText == null) return false;
        if (cueQuietSince < 0f || Time.unscaledTime - cueQuietSince < GoalBannerWait) return false;
        if (flow != null && (flow.InHub || flow.InTutorialRun)) return false;
        if (LevelLoader.Tutorial || MazeRuntime.Active != null) return false;
        if (levelLoader != null && levelLoader.IsMazeLevel) return false;
        return !TutorialDialogue.Playing;
    }

    // The moment the player's money reaches the target (DetectChanges): the flag pulses and the banner
    // says the round is won if it ends now
    void CelebrateTarget()
    {
        targetCelebrated = true;
        targetPulseLeft = TargetPulseTime;
        ShowBanner("Target reached!", "Keep eating: all the gold you collect goes to your wallet", 1.8f);
    }

    // The round's banner, from gameplay code (LevelLoader's survival waves): shown on the HUD's next update
    static string announceTitle, announceSub;
    static float announceHold;
    public static void Announce(string title, string sub, float hold = 2f)
    {
        announceTitle = title; announceSub = sub; announceHold = hold;
    }

    void ShowBanner(string title, string sub, float hold)
    {
        if (banner == null) BuildBanner();
        if (banner == null) return;
        bannerTitle.text = title;
        bannerSub.text = sub;
        bannerTime = 0f;
        bannerHold = hold;
        banner.alpha = 0f;
        PlaceBanner();
        banner.gameObject.SetActive(true);
        banner.transform.SetAsLastSibling();
        if (pausePopup != null) pausePopup.transform.SetAsLastSibling();
    }

    // Figma "Level start - Proposed 2": a slim color/bg/panel strip just under the top bar, centered
    // on the TARGET flag with a notch pointing up at it, title over a gray line. Round messages always
    // appear here, so they never cover the arena's middle where tutorial callouts and the player are
    void BuildBanner()
    {
        if (timerText == null || timerText.canvas == null) return;
        HUDTheme theme = PopupTheme;
        Color fill = theme.panel;
        fill.a = 0.94f;
        RectTransform card = PopupBox(timerText.canvas.transform, "RoundBanner", fill, new Vector2(640f, 84f),
            Vector2.zero);
        card.pivot = new Vector2(0.5f, 1f);
        card.GetComponent<Image>().raycastTarget = false;
        banner = card.gameObject.AddComponent<CanvasGroup>();
        banner.blocksRaycasts = false;
        banner.interactable = false;

        var notch = new GameObject("Notch", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        notch.transform.SetParent(card, false);
        notch.sprite = NotchSprite();
        notch.color = fill;
        notch.raycastTarget = false;
        notch.rectTransform.anchorMin = notch.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        notch.rectTransform.pivot = new Vector2(0.5f, 0f);
        notch.rectTransform.sizeDelta = new Vector2(28f, NotchHeight);
        notch.rectTransform.anchoredPosition = Vector2.zero;

        bannerTitle = PopupText(card, "Title", "", 32f, FontStyles.Bold, Color.white, new Vector2(0f, 12f));
        bannerTitle.richText = true;
        bannerSub = PopupText(card, "Sub", "", 18f, FontStyles.Normal, theme.title, new Vector2(0f, -24f));
        banner.gameObject.SetActive(false);
    }

    // Each time it shows (the window may have been resized): the notch just under the top bar, the
    // strip centered on the TARGET flag, kept inside the screen
    void PlaceBanner()
    {
        RectTransform card = (RectTransform)banner.transform;
        RectTransform canvasRect = card.parent as RectTransform;
        if (canvasRect == null) return;
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 1f);

        Rect area = canvasRect.rect;
        float barBottom = area.yMax - area.height * HUDLayout.TopBarHeight;
        RectTransform topBar = TopBarRect();
        if (topBar != null)
        {
            var corners = new Vector3[4];
            topBar.GetWorldCorners(corners);
            barBottom = canvasRect.InverseTransformPoint(corners[0]).y;
        }
        float x = 0f;
        if (targetMarker != null && targetMarker.gameObject.activeInHierarchy)
            x = canvasRect.InverseTransformPoint(targetMarker.position).x;
        float half = card.sizeDelta.x * 0.5f + 16f;
        x = Mathf.Clamp(x, area.xMin + half, area.xMax - half);
        // Anchored at the canvas center, so canvas-local points are anchored positions
        Vector2 center = area.center;
        card.anchoredPosition = new Vector2(x - center.x, barBottom - BannerGap - NotchHeight - center.y);
    }

    // The HUD's top bar (HUDBuilder names it TopBar; the timer sits somewhere inside it)
    RectTransform TopBarRect()
    {
        for (Transform t = timerText != null ? timerText.transform : null; t != null; t = t.parent)
            if (t.name == "TopBar") return t as RectTransform;
        return timerText != null ? timerText.transform.parent as RectTransform : null;
    }

    // An upward triangle, white so the Image color tints it. Named, so PaperUI leaves it flat
    static Sprite notchSprite;
    static Sprite NotchSprite()
    {
        if (notchSprite != null) return notchSprite;
        const int w = 32, h = 16;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
        {
            float halfWidth = w * 0.5f * (1f - (y + 0.5f) / h);
            for (int x = 0; x < w; x++)
            {
                float alpha = Mathf.Clamp01(halfWidth - Mathf.Abs(x + 0.5f - w * 0.5f) + 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        notchSprite = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0f), 100f);
        notchSprite.name = "BannerNotch";
        return notchSprite;
    }

    // Fade and grow in, hold, fade out. Gone at once when the round ends (the end card takes over)
    void AnimateBanner(GameManager gm, float dt)
    {
        if (banner == null || !banner.gameObject.activeSelf) return;
        if (!gm.IsPlaying) { banner.gameObject.SetActive(false); return; }
        // A tutorial step came up: the message waits unseen, then plays from its fade-in once it's gone
        if (TutorialStep.AnyShowing) { bannerTime = 0f; banner.alpha = 0f; return; }

        bannerTime += dt;
        float alpha;
        if (bannerTime < BannerFade) alpha = bannerTime / BannerFade;
        else if (bannerTime < BannerFade + bannerHold) alpha = 1f;
        else alpha = 1f - (bannerTime - BannerFade - bannerHold) / BannerFadeOut;
        if (alpha <= 0f) { banner.gameObject.SetActive(false); return; }

        banner.alpha = alpha;
        float grow = Mathf.Clamp01(bannerTime / BannerFade);
        banner.transform.localScale = Vector3.one * Mathf.Lerp(0.9f, 1f, grow);
    }

    // U7: the screen's edges flash red, then fade, whenever a life is lost (DetectChanges)
    void FlashDamage()
    {
        if (damageFlash == null)
        {
            if (timerText == null || timerText.canvas == null) return;
            var go = new GameObject("DamageFlash", typeof(RectTransform), typeof(RawImage));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(timerText.canvas.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            damageFlash = go.GetComponent<RawImage>();
            damageFlash.texture = DamageVignette();
            damageFlash.raycastTarget = false;
        }
        damageLeft = DamageFlashTime;
        damageFlash.gameObject.SetActive(true);
        damageFlash.transform.SetAsLastSibling();
        if (pausePopup != null) pausePopup.transform.SetAsLastSibling();
        AnimateDamageFlash(0f);
    }

    void AnimateDamageFlash(float dt)
    {
        if (damageFlash == null || !damageFlash.gameObject.activeSelf) return;
        damageLeft -= dt;
        if (damageLeft <= 0f) { damageFlash.gameObject.SetActive(false); return; }
        damageFlash.color = new Color(1f, 0.15f, 0.15f, DamageFlashAlpha * damageLeft / DamageFlashTime);
    }

    // The TARGET flag on the money bar swells once, then settles
    void AnimateTargetPulse(float dt)
    {
        if (targetMarker == null || targetPulseLeft <= 0f) return;
        targetPulseLeft = Mathf.Max(0f, targetPulseLeft - dt);
        float t = 1f - targetPulseLeft / TargetPulseTime;
        targetMarker.localScale = Vector3.one * (1f + 0.5f * Mathf.Sin(t * Mathf.PI));
    }

    // Clear in the middle, opaque toward the edges and most in the corners. White: the RawImage
    // color makes it red
    static Texture2D damageVignette;
    static Texture2D DamageVignette()
    {
        if (damageVignette != null) return damageVignette;
        const int size = 64;
        damageVignette = new Texture2D(size, size, TextureFormat.RGBA32, false);
        damageVignette.wrapMode = TextureWrapMode.Clamp;
        damageVignette.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                float edge = Mathf.Lerp(Mathf.Sqrt(u * u + v * v) / 1.4142f, Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)), 0.6f);
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1f, edge));
                damageVignette.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        damageVignette.Apply();
        return damageVignette;
    }

    private TMP_Text targetLabel;
    private RectTransform targetMarker;
    private RawImage overTargetFill;

    void HideLegacyMinerScore()
    {
        // Migrate HUDs already saved in scenes as well as ones made by the updated builder.
        if (minerBarFill != null) minerBarFill.gameObject.SetActive(false);
        if (minerMoneyText != null) minerMoneyText.gameObject.SetActive(false);
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
            if (child.name == "MinerSide" || child.name == "MinerLabel" || child.name == "Versus")
                child.gameObject.SetActive(false);
    }

    void UpdateMoneyBar(GameManager gm)
    {
        if (playerBarFill == null && minerBarFill == null) return;
        RectTransform bar = (playerBarFill != null ? playerBarFill : minerBarFill).parent as RectTransform;
        if (targetMarker == null && bar != null)
        {
            targetMarker = BuildMoneyTarget(bar, playerMoneyText != null ? playerMoneyText.font : null);
            targetLabel = targetMarker.GetComponentInChildren<TMP_Text>();
        }
        if (targetMarker != null)
        {
            targetMarker.gameObject.SetActive(gm.UsesMoneyTarget);
            if (targetLabel != null) targetLabel.text = "TARGET $" + gm.MoneyTarget.ToString("N0");
        }
        float total = gm.LevelGoldTotal;
        float player = total > 0f ? Mathf.Clamp01(gm.PlayerMoney / total) : 0f;
        if (playerBarFill != null)
        {
            playerBarFill.gameObject.SetActive(player > 0f);
            playerBarFill.anchorMin = Vector2.zero;
            playerBarFill.anchorMax = new Vector2(player, 1f);
            playerBarFill.offsetMin = Vector2.zero;
            playerBarFill.offsetMax = Vector2.zero;
        }
        UpdateOverTargetFill(bar, gm.UsesMoneyTarget && player > 0.5f, player);
    }

    void UpdateOverTargetFill(RectTransform bar, bool show, float playerShare)
    {
        if (bar == null) return;
        if (overTargetFill == null && show)
        {
            overTargetFill = new GameObject("OverTargetFill", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            overTargetFill.transform.SetParent(bar, false);
            overTargetFill.texture = GoldWhiteStripes();
            overTargetFill.raycastTarget = false;
            // The target flag must remain visible above the striped progress.
            if (targetMarker != null) targetMarker.SetAsLastSibling();
        }
        if (overTargetFill == null) return;
        overTargetFill.gameObject.SetActive(show);
        if (!show) return;
        RectTransform rect = overTargetFill.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(playerShare, 1f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        // Repeat at a fixed size, so a small excess doesn't squeeze all the stripes together.
        overTargetFill.uvRect = new Rect(0f, 0f,
            bar.rect.width * (playerShare - 0.5f) / 32f, bar.rect.height / 32f);
    }

    static Texture2D goldWhiteStripes;
    static Texture2D GoldWhiteStripes()
    {
        if (goldWhiteStripes != null) return goldWhiteStripes;
        // Art: felt and cream paper stripes in the same two colours (the miner lavender and white;
        // Resources/Art/Tiles/PaperStripes, a repeating tile with the same diagonal period).
        Sprite stripes = ArtLibrary.Get("Tiles/PaperStripes");
        if (stripes != null) return goldWhiteStripes = stripes.texture;
        const int size = 32;
        goldWhiteStripes = new Texture2D(size, size, TextureFormat.RGBA32, false);
        goldWhiteStripes.wrapMode = TextureWrapMode.Repeat;
        goldWhiteStripes.filterMode = FilterMode.Bilinear;
        Color gold = HUDTheme.For(HUDVariant.Beta).miner;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                goldWhiteStripes.SetPixel(x, y, (x + y) % size < size / 2 ? gold : Color.white);
        goldWhiteStripes.Apply();
        return goldWhiteStripes;
    }

    // Shared by the editor builder and saved HUDs, so existing scenes need no rebuild.
    public static RectTransform BuildMoneyTarget(RectTransform bar, TMP_FontAsset font)
    {
        RectTransform existing = bar.Find("MoneyTarget") as RectTransform;
        if (existing != null)
        {
            // Procedural sprites are recreated after reopening a scene saved by HUDBuilder.
            Image savedFlag = existing.Find("Flag")?.GetComponent<Image>();
            if (savedFlag != null && savedFlag.sprite == null) savedFlag.sprite = TargetFlagSprite();
            PlaceMoneyTarget(existing);
            return existing;
        }
        Image track = bar.GetComponent<Image>();
        if (track == null) track = bar.gameObject.AddComponent<Image>();
        track.color = HUDTheme.For(HUDVariant.Beta).barBack;
        track.raycastTarget = false;

        RectTransform marker = new GameObject("MoneyTarget", typeof(RectTransform)).GetComponent<RectTransform>();
        marker.SetParent(bar, false);
        marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0f);
        marker.pivot = new Vector2(0.5f, 1f);
        marker.sizeDelta = new Vector2(2f, 22f);
        marker.anchoredPosition = Vector2.zero;
        Color red = new Color(1f, 0.28f, 0.32f);
        TargetImage(marker, "Pole", new Vector2(0f, 3f), new Vector2(2f, 23f), red);
        Image flag = TargetImage(marker, "Flag", new Vector2(8f, -2f), new Vector2(16f, 12f), red);
        flag.sprite = TargetFlagSprite();

        TMP_Text caption = new GameObject("TargetLabel", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        caption.transform.SetParent(marker, false);
        caption.font = font;
        caption.fontSize = 16f;
        caption.fontStyle = FontStyles.Bold;
        caption.color = Color.white;
        caption.alignment = TextAlignmentOptions.MidlineLeft;
        caption.raycastTarget = false;
        caption.text = "TARGET";
        caption.rectTransform.anchorMin = caption.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        caption.rectTransform.pivot = new Vector2(0f, 1f);
        caption.rectTransform.anchoredPosition = new Vector2(23f, -2f);
        caption.rectTransform.sizeDelta = new Vector2(190f, 18f);
        PlaceMoneyTarget(marker);
        return marker;
    }

    static void PlaceMoneyTarget(RectTransform marker)
    {
        // Center the marker on the track: its pole crosses the full bar, with a larger pennant.
        marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
        marker.anchoredPosition = Vector2.zero;
        marker.SetAsLastSibling();
        RectTransform pole = marker.Find("Pole") as RectTransform;
        if (pole != null)
        {
            pole.anchoredPosition = new Vector2(0f, 12f);
            pole.sizeDelta = new Vector2(3f, 28f);
        }
        RectTransform flag = marker.Find("Flag") as RectTransform;
        if (flag != null)
        {
            flag.anchoredPosition = new Vector2(11f, 12f);
            flag.sizeDelta = new Vector2(22f, 18f);
        }
        RectTransform caption = marker.Find("TargetLabel") as RectTransform;
        if (caption != null)
        {
            caption.anchoredPosition = new Vector2(29f, -4f);
            caption.sizeDelta = new Vector2(230f, 20f);
            TMP_Text words = caption.GetComponent<TMP_Text>();
            if (words != null) words.fontSize = 16f;
        }
    }

    static Image TargetImage(RectTransform parent, string name, Vector2 at, Vector2 size, Color color)
    {
        Image image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(parent, false);
        image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        image.rectTransform.pivot = new Vector2(0.5f, 1f);
        image.rectTransform.anchoredPosition = at;
        image.rectTransform.sizeDelta = size;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static Sprite targetFlagSprite;
    static Sprite TargetFlagSprite()
    {
        if (targetFlagSprite != null) return targetFlagSprite;
        // Art: a felt pennant (Resources/Art/Fx/Pennant), white so the flag's red still colours it
        Sprite pennant = ArtLibrary.Get("Fx/Pennant");
        if (pennant != null) return targetFlagSprite = pennant;
        const int size = 32;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float edge = size - 2f * Mathf.Abs(y + 0.5f - size * 0.5f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(edge - x)));
            }
        texture.Apply();
        targetFlagSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return targetFlagSprite;
    }

    void UpdateMoney(GameManager gm)
    {
        playerMoney.bold = gm.MoneyTargetReached;
        playerMoney.alpha = 1f;

        if (playerMoneyText != null) playerMoneyText.text = "$" + gm.PlayerMoney.ToString("N0");
        if (mazeMoneyText != null)
        {
            // World map: same wallet the store spends.
            int show = (flow != null && flow.InHub) ? MazeHubState.DisplayMoney : gm.PlayerMoney;
            // The Beta Coins panel shows the bare amount next to its coin icon (Figma "150")
            string prefix = mazeMoneyText == coinsText ? "" : "$";
            mazeMoneyText.text = prefix + show.ToString("N0");
        }

        playerMoney.Apply(moneyFlashScale);
    }

    void UpdateTimer(GameManager gm)
    {
        if (timerText == null || !timerText.gameObject.activeInHierarchy) return;
        if (!gm.UseRoundTimer) return;

        // No clock running out (attack mode): the time taken so far, counting up
        if (gm.endless && !(flow != null && flow.InHub))
        {
            int up = Mathf.FloorToInt(gm.TimeSurvived);
            timerText.text = (up / 60) + ":" + (up % 60).ToString("00");
            timerText.color = timerBaseColor;
            timerText.transform.localScale = timerBaseScale;
            PlaceMapButton();
            return;
        }

        // Judge by the number on screen, so "0:10" is never red
        int seconds = Mathf.CeilToInt(gm.TimeLeft);
        timerText.text = flow != null && flow.InHub ? "" : (seconds / 60) + ":" + (seconds % 60).ToString("00");   // the hub has no clock
        timerText.color = seconds < timerWarningTime ? timerWarningColor : timerBaseColor;

        // Heartbeat: pop right after each second ticks over, settle within ~0.3 s
        float pulse = 0f;
        if (gm.IsPlaying && seconds <= timerPulseTime)
            pulse = Mathf.Clamp01(1f - (seconds - gm.TimeLeft) / 0.3f);
        timerText.transform.localScale = timerBaseScale * Mathf.Lerp(1f, timerPulseScale, pulse);
        PlaceMapButton();
    }

    // A Pause button at the right end of the top bar, the clock just left of it. It opens the Quit
    // Game popup, whose Quit goes back to the map. Only when this round was opened from the map: editor N/P play of the Game scene has
    // no node to return to.
    void EnsureMapButton()
    {
        if (timerText == null) return;
        // Maze world map has no Pause / quit-to-map chrome.
        if (levelLoader != null && levelLoader.IsMazeLevel) return;

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
        ShowAsQuit(mapButton);
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
        rect.sizeDelta = new Vector2(92f, 40f);

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
        label.text = "Quit";
        label.fontSize = 27f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.86f, 0.90f, 0.96f, 1f);
        label.raycastTarget = false;
        return button;
    }

    void PlaceMapButton()
    {
        if (mapButton == null || timerText == null) return;

        bool show = flow != null && (flow.CurrentNode != null || flow.InHub);
        if (mapButton.gameObject.activeSelf != show) mapButton.gameObject.SetActive(show);
        if (Beta) return;   // Beta: Pause has its own panel; the timer stays centered in the score

        // HUD v2: Pause takes the timer's old spot at the right edge; the timer (right aligned)
        // moves left by the button's width plus a 50 px gap
        RectTransform timerRect = timerText.rectTransform;
        RectTransform rect = mapButton.GetComponent<RectTransform>();
        float timerX = show ? timerHomeX - rect.sizeDelta.x - 50f : timerHomeX;
        timerRect.anchoredPosition = new Vector2(timerX, timerRect.anchoredPosition.y);
        if (show) rect.anchoredPosition = new Vector2(timerHomeX, timerRect.anchoredPosition.y);
    }

    // The saved button is a pause icon (two bars). Show the same Quit label the map uses.
    // Canvas is 1920x1080; the map button is 88x32 in the 1280x720 frame, so 132x48 here.
    void ShowAsQuit(Button button)
    {
        RectTransform rect = button.GetComponent<RectTransform>();
        const float width = 132f;
        const float height = 48f;
        float grow = width - rect.sizeDelta.x;
        rect.sizeDelta = new Vector2(width, height);

        if (grow > 1f && rect.parent is RectTransform panel && panel.name == "LevelPause")
        {
            panel.sizeDelta += new Vector2(grow, 0f);
            if (coinsPanel != null)
            {
                RectTransform coins = coinsPanel.GetComponent<RectTransform>();
                coins.anchoredPosition += new Vector2(-grow, 0f);
            }
        }

        for (int i = 0; i < rect.childCount; i++)
        {
            Transform child = rect.GetChild(i);
            if (child.name == "BarL" || child.name == "BarR") child.gameObject.SetActive(false);
        }

        TextMeshProUGUI label = null;
        foreach (TextMeshProUGUI text in button.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text.transform.parent == rect) { label = text; break; }
        }
        if (label == null)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(rect, false);
            var labelRect = (RectTransform)go.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            label = go.AddComponent<TextMeshProUGUI>();
            label.raycastTarget = false;
            if (levelText != null) label.font = levelText.font;
        }
        label.gameObject.SetActive(true);
        label.text = "Quit";
        label.fontSize = 27f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.10f, 0.12f, 0.11f);

        Image image = button.targetGraphic as Image;
        if (image != null) image.color = Color.white;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.94f, 0.95f, 0.94f);
        colors.pressedColor = new Color(0.82f, 0.84f, 0.82f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(1f, 1f, 1f, 0.5f);
        button.colors = colors;
    }

    public bool IsPaused => pausePopup != null && pausePopup.activeSelf;

    // Pause freezes the round (time scale 0: the clock, hooks and the character all stop). In a level
    // it offers Resume / Restart / Quit to Map; on the world map, Resume / Quit to Menu. Esc opens and
    // closes it too (HandlePopupKeys)
    void OpenPause()
    {
        if (IsPaused) return;
        if (pausePopup == null) BuildPausePopup();
        RefreshPauseText();
        pausePopup.transform.SetAsLastSibling();
        pausePopup.SetActive(true);
        timeScaleBeforePause = Time.timeScale;
        Time.timeScale = 0f;
        Sfx.Ui("ui_pause");   // sound
    }

    void ResumeFromPause()
    {
        if (pausePopup != null) pausePopup.SetActive(false);
        Time.timeScale = timeScaleBeforePause > 0f ? timeScaleBeforePause : 1f;
        Sfx.Ui("ui_pause");   // sound
    }

    void RestartFromPause()
    {
        if (pausePopup != null) pausePopup.SetActive(false);
        Time.timeScale = 1f;
        GameManager gm = GameManager.Instance;
        if (gm != null) gm.Restart();
    }

    // Leaving mid-round does not clear the node; leaving after a win does, so the clear is not lost
    // to the auto-return timer
    void QuitToMap()
    {
        ResumeFromPause();
        if (flow != null && flow.InHub) { flow.OpenMenu(); return; }   // quitting the map goes to the menu
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

    // Esc: open / close Pause while its button shows and the round is on. After the round: Enter goes
    // back to the map after a win; R is GameManager's restart, which this frame first keeps a win's clear
    void HandlePopupKeys(GameManager gm)
    {
        if (DeveloperConsole.CapturesInput || DeveloperConsole.IsOpen) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (IsPaused) ResumeFromPause();
            else if (gm.IsPlaying && mapButton != null && mapButton.gameObject.activeInHierarchy) OpenPause();
            return;
        }

        if (!gm.PlayerWon || !EndActionsReady(gm)) return;
        if (CanLeaveToMap && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            GoToMapAfterWin();
        else if (Input.GetKeyDown(KeyCode.R))
            KeepWinCleared();
    }

    // A dimmed screen (it also blocks clicks behind it) with a centered card. Built here rather than
    // by HUDBuilder, so the Game scene's saved HUD needs no rebuild
    RectTransform pauseCard;

    void OpenSoundFromPause()
    {
        if (pauseCard == null || SoundPanel.IsOpen) return;
        pauseCard.gameObject.SetActive(false);   // nothing under the Sound card can catch a click
        SoundPanel.Show(() => { if (pauseCard != null) pauseCard.gameObject.SetActive(true); },
                        () => pausePopup == null || !pausePopup.activeSelf);
    }

    void BuildPausePopup()
    {
        HUDTheme theme = PopupTheme;
        bool hub = flow != null && flow.InHub;

        pausePopup = new GameObject("PausePopup", typeof(RectTransform), typeof(Image));
        RectTransform dim = (RectTransform)pausePopup.transform;
        dim.SetParent(timerText.canvas.transform, false);
        dim.anchorMin = Vector2.zero;
        dim.anchorMax = Vector2.one;
        dim.offsetMin = Vector2.zero;
        dim.offsetMax = Vector2.zero;
        pausePopup.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

        const float width = 600f;
        const float buttonGap = 14f;
        int buttons = hub ? 3 : 4;   // + Sound
        float buttonsHeight = buttons * PopupButtonHeight + (buttons - 1) * buttonGap;
        // Padding 44 / 40, title 52, sub 30, buttons, then (levels only) a divider and the controls
        float height = 44f + 52f + 12f + 30f + 24f + buttonsHeight + 40f;
        if (!hub) height += 24f + 1f + 20f + 22f + 10f + 26f;

        RectTransform card = PopupCard(dim, "Card", new Vector2(width, height));
        float top = height * 0.5f - 44f;

        TMP_Text title = PopupText(card, "Title", hub ? "QUIT GAME?" : "PAUSED", 44f, FontStyles.Bold, Color.white,
            new Vector2(0f, top - 26f));
        title.characterSpacing = 10f;
        top -= 52f + 12f;
        pauseInfo = PopupText(card, "Info", hub ? "Back to the main menu?" : "", 22f, FontStyles.Normal, theme.title,
            new Vector2(0f, top - 15f));
        top -= 30f + 24f;

        Vector2 size = new Vector2(width - 96f, PopupButtonHeight);
        float y = top - PopupButtonHeight * 0.5f;
        PopupButton(card, PopupStyle.Primary, "Resume", "Esc", size, new Vector2(0f, y), ResumeFromPause);
        // Sound: the papercraft Sound card (volumes, mute, now playing with back / next) over a hidden card
        pauseCard = card;
        y -= PopupButtonHeight + buttonGap;
        PopupButton(card, PopupStyle.Secondary, "Sound", null, size, new Vector2(0f, y), OpenSoundFromPause);
        if (!hub)
        {
            y -= PopupButtonHeight + buttonGap;
            PopupButton(card, PopupStyle.Secondary, "Restart", "R", size, new Vector2(0f, y), RestartFromPause);
        }
        y -= PopupButtonHeight + buttonGap;
        PopupButton(card, PopupStyle.Danger, hub ? "Quit to Menu" : "Quit to Map", null, size, new Vector2(0f, y), QuitToMap);
        top -= buttonsHeight;

        if (!hub)
        {
            top -= 24f;
            PopupRect(card, "Divider", theme.divider, new Vector2(width - 96f, 1f), new Vector2(0f, top - 0.5f));
            top -= 1f + 20f;
            TMP_Text heading = PopupText(card, "ControlsTitle", "CONTROLS", 16f, FontStyles.Bold, theme.title,
                new Vector2(0f, top - 11f));
            heading.characterSpacing = 10f;
            top -= 22f + 10f;
            pauseControls = PopupText(card, "Controls", "", 18f, FontStyles.Normal, theme.title, new Vector2(0f, top - 13f));
            pauseControls.richText = true;
        }
        pausePopup.SetActive(false);
    }

    // "LEVEL 2 - 1:30 left" and the keys this character has now ("[K] Block   [J] cut ropes")
    void RefreshPauseText()
    {
        GameManager gm = GameManager.Instance;
        if (pauseInfo != null && !(flow != null && flow.InHub))
        {
            string level = levelText != null && levelText.gameObject.activeInHierarchy ? levelText.text : "";
            string time = "";
            if (gm != null && gm.UseRoundTimer)
            {
                int seconds = Mathf.CeilToInt(gm.TimeLeft);
                time = seconds / 60 + ":" + (seconds % 60).ToString("00") + " left";
            }
            pauseInfo.text = level != "" && time != "" ? level + "  \u00B7  " + time : level + time;
        }
        if (pauseControls != null)
        {
            if (player == null) player = FindFirstObjectByType<PlayerCharacter>();
            string keys = "[WASD] move";
            if (player != null && player.SkillHint != "") keys += "     " + player.SkillHint.Trim();
            pauseControls.text = KeyCaps(keys);
        }
    }

    // "[K] Block" -> the key in bold white, the rest in the title gray
    static string KeyCaps(string text)
    {
        return text.Replace("[", "<b><color=#FFFFFF>").Replace("]", "</color></b>");
    }

    // The popup card: color/bg/panel with 16 px corners and a soft drop shadow
    RectTransform PopupCard(Transform parent, string name, Vector2 size)
    {
        RectTransform card = PopupBox(parent, name, PopupTheme.panel, size, Vector2.zero);
        Shadow shadow = card.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
        shadow.effectDistance = new Vector2(0f, -12f);
        return card;
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

    // A plain rectangle (dividers, bars); never catches clicks
    RectTransform PopupRect(Transform parent, string name, Color color, Vector2 size, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        Image image = go.GetComponent<Image>();
        image.sprite = FlatSprite();
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    TMP_Text PopupText(RectTransform parent, string name, string text, float size, FontStyles style, Color color, Vector2 position)
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
        return label;
    }

    // Figma HUD/PopupButton: Primary (skill blue, dark ink), Secondary (button gray) or Danger (red ink),
    // with an optional key badge ("R", "Esc") after the label. Lightens on hover, darkens on press
    Button PopupButton(RectTransform parent, PopupStyle style, string text, string key, Vector2 size, Vector2 position,
        UnityEngine.Events.UnityAction onClick)
    {
        HUDTheme theme = PopupTheme;
        Color fill = style == PopupStyle.Primary ? theme.skill : PopupButtonFill;
        Color ink = style == PopupStyle.Primary ? theme.backdrop : style == PopupStyle.Danger ? PopupDanger : PopupButtonInk;

        RectTransform rect = PopupBox(parent, text.Replace(" ", "") + "Button", Color.white, size, position);
        Image image = rect.GetComponent<Image>();
        image.pixelsPerUnitMultiplier = 16f / 12f;   // 12 px corners from the 16 px sprite
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = fill;
        colors.highlightedColor = Color.Lerp(fill, Color.white, 0.15f);
        colors.pressedColor = Color.Lerp(fill, Color.black, 0.15f);
        colors.selectedColor = fill;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.onClick.AddListener(onClick);

        TMP_Text label = PopupText(rect, "Label", text, 26f, FontStyles.Bold, ink, Vector2.zero);
        if (string.IsNullOrEmpty(key)) return button;

        // The badge: the key in a small rounded box, label + 12 px + badge centered together
        TMP_Text keyText = PopupText(rect, "KeyText", key, 16f, FontStyles.Bold, ink, Vector2.zero);
        float labelWidth = label.GetPreferredValues(text).x;
        float badgeWidth = keyText.GetPreferredValues(key).x + 16f;
        float total = labelWidth + 12f + badgeWidth;
        label.rectTransform.sizeDelta = new Vector2(labelWidth + 4f, label.rectTransform.sizeDelta.y);
        label.rectTransform.anchoredPosition = new Vector2(-total * 0.5f + labelWidth * 0.5f, 0f);

        Color badgeColor = style == PopupStyle.Primary ? new Color(0f, 0f, 0f, 0.18f) : new Color(1f, 1f, 1f, 0.12f);
        RectTransform badge = PopupBox(rect, "Key", badgeColor, new Vector2(badgeWidth, 26f),
            new Vector2(total * 0.5f - badgeWidth * 0.5f, 0f));
        Image badgeImage = badge.GetComponent<Image>();
        badgeImage.pixelsPerUnitMultiplier = 16f / 6f;   // 6 px corners
        badgeImage.raycastTarget = false;
        keyText.rectTransform.SetParent(badge, false);
        keyText.rectTransform.sizeDelta = badge.sizeDelta;
        keyText.rectTransform.anchoredPosition = Vector2.zero;
        return button;
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
        // Beta's lifeRow is shown by UpdateSidebar, which knows whether the SKILL / DIGEST rows are
        bool rowStyle = lifeSection != null || lifeRow != null;
        if (lifeRow == null)
        {
            GameObject shown = lifeSection != null ? lifeSection : livesText.gameObject;
            shown.SetActive(roundHasLives);
        }
        int bonus = gm.BonusLives;
        if (bonus <= 0 && ((flow != null && flow.InHub) || MazeRuntime.Active != null))
            bonus = LevelLoader.PendingBraceBonusLives();
        // If Brace is only armed (not yet written into gm.Lives), still show core+bonus.
        int shownLives = gm.Lives;
        if (bonus > 0 && gm.BonusLives <= 0) shownLives = gm.Lives + bonus;
        int baseLives = Mathf.Max(0, shownLives - bonus);
        // Brace bonus: orange +N (matches the orange pip on the over-head life bar).
        if (bonus > 0 && shownLives > 0)
        {
            string mark = StatRows || rowStyle ? "\u00D7" : "x";
            livesText.richText = true;
            // Keep face color white so TMP <color=#FF8C14> stays orange (Label.Apply multiplies tint).
            livesText.color = Color.white;
            livesText.text = !rowStyle
                ? "Life: <color=#FFFFFF>" + baseLives + "</color> <color=#FF8C14>+" + bonus + "</color>"
                : "<color=#FFFFFF>" + mark + baseLives + "</color> <color=#FF8C14>+" + bonus + "</color>";
        }
        else if (!rowStyle) { livesText.text = "Life: " + gm.Lives; lives.Apply(moneyFlashScale); }
        else { livesText.text = (StatRows || lifeRow != null ? "\u00D7" : "x") + gm.Lives; lives.Apply(moneyFlashScale); }
    }

    // The map node's name ("LEVEL 4") when the map sent us here, else "LV 4/10" for editor N/P play;
    // hidden in scenes without a level loader (Dev scenes)
    void UpdateLevel()
    {
        if (levelText == null) return;

        MapNode node = flow != null ? flow.CurrentNode : null;
        string text = "";
        if (node != null) text = node.displayName.ToUpperInvariant();
        else if (flow != null && flow.InHub) text = "WORLD MAP";
        else if (flow != null && flow.InTutorialRun) text = "TUTORIAL";
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
        if (player == null || !player.gameObject.activeInHierarchy) player = FindFirstObjectByType<PlayerCharacter>();
        UpdateCharacterAvatar();
        CrossLevelStatus record = PlayerCharacter.Record;

        if (mazeHudApplied)
        {
            if (walletSection != null) walletSection.SetActive(mazeMoneyText == walletText && walletText != null);
            if (itemsSection != null) itemsSection.SetActive(false);
            if (lifeSection != null) lifeSection.SetActive(true);
            // Skill / digest cooldowns stay visible on the world map.
            bool hasSkill = UpdateSkill();
            bool hasDigest = UpdateDigest();
            UpdateLifeRow(hasSkill, hasDigest);
            // Beta: PigStatus (the pig avatar) stays on the map even when neither pill is shown
            if (skillSection != null) skillSection.SetActive(Beta || hasSkill || hasDigest);
            UpdateStatusSection();
            UpdateDividers();
            return;
        }

        UpdateWallet(record);
        bool skill = UpdateSkill();
        bool digest = UpdateDigest();
        bool life = UpdateLifeRow(skill, digest);
        if (skillSection != null) skillSection.SetActive(skill || digest || life);
        UpdateItems(record);
        UpdateStatusSection();
        UpdateDividers();
    }

    void UpdateCharacterAvatar()
    {
        if (player == null) return;
        if (avatarSource != null && avatarSource.GetComponent<PlayerCharacter>().GetType() == player.GetType()) return;
        if (characterAvatar == null)
            foreach (RectTransform rect in GetComponentsInChildren<RectTransform>(true))
                if (rect.name == "CharacterAvatar" || rect.name == "PigAvatar") { characterAvatar = rect; break; }
        if (characterAvatar == null) return;

        ChampionSpawner spawner = FindFirstObjectByType<ChampionSpawner>();
        SurvivalLoader survival = spawner == null ? FindFirstObjectByType<SurvivalLoader>() : null;
        GameObject[] roster = spawner != null ? spawner.champions : survival != null ? survival.champions : null;
        if (roster == null) return;
        GameObject source = null;
        foreach (GameObject prefab in roster)
        {
            PlayerCharacter character = prefab != null ? prefab.GetComponent<PlayerCharacter>() : null;
            if (character != null && character.GetType() == player.GetType()) { source = prefab; break; }
        }
        if (source == null) return;

        // Copy the untouched prefab used by the character shop, never the animated live player.
        // Keep the circular background; retire the old pig drawing or previous character copy.
        foreach (Transform child in characterAvatar) child.gameObject.SetActive(false);
        if (avatarFigure != null) Destroy(avatarFigure.gameObject);
        float height = Mathf.Max(1f, characterAvatar.rect.height - 8f);
        avatarFigure = UISpriteCopy.Build(source, characterAvatar, height);
        avatarFigure.name = "CharacterPortrait";
        float scale = Mathf.Min(1f, Mathf.Max(1f, characterAvatar.rect.width - 8f) / Mathf.Max(1f, avatarFigure.sizeDelta.x));
        avatarFigure.localScale = Vector3.one * scale;
        avatarFigure.anchorMin = avatarFigure.anchorMax = Vector2.one * .5f;
        avatarFigure.anchoredPosition = new Vector2(0f, -avatarFigure.sizeDelta.y * scale * .5f);
        avatarSource = source;
    }

    // Beta: LIFE xN takes the SKILL / DIGEST rows' place in PigStatus when neither is shown (the
    // world map), so the panel never needs a third row. Returns whether it is shown
    bool UpdateLifeRow(bool hasSkill, bool hasDigest)
    {
        if (lifeRow == null) return false;
        bool shown = roundHasLives && livesText != null && !hasSkill && !hasDigest;
        if (lifeRow.activeSelf != shown) lifeRow.SetActive(shown);
        return shown;
    }

    void UpdateWallet(CrossLevelStatus record)
    {
        if (walletSection == null) return;
        walletSection.SetActive(record != null);
        if (record != null && walletText != null) walletText.text = record.WalletLabel();
    }

    // "Active" while the skill runs, then the seconds until Space works again with the bar filling
    // back up, then "Ready". Returns whether the row is shown
    bool UpdateSkill()
    {
        if (skillText == null) return false;
        bool shown = player != null && player.HasSkill;
        if (skillBlock != null) skillBlock.SetActive(shown);
        else SetRow(skillText, shown);
        if (skillBarFill != null) skillBarFill.parent.gameObject.SetActive(shown);
        if (!shown) return false;

        float readyIn = player.SkillReadyIn;
        if (Beta)
        {
            // The pill refills like the bar did; dark ink on the full pill, white while it refills
            float pill = readyIn > 0f && player.SkillWait > 0f ? Mathf.Clamp01(1f - readyIn / player.SkillWait) : 1f;
            string state = player.SkillActive ? "ACTIVE" : readyIn > 0f ? Seconds(readyIn) : "READY";
            SetValue(skillText, state, pill >= 0.999f ? pillSkillInk : pillWaitInk);
            if (skillBarFill != null) skillBarFill.anchorMax = new Vector2(pill, 1f);
            return true;
        }
        // HUD v2 states are all caps, like the digest's CAN EAT (seconds keep their lowercase s)
        bool v2 = skillBlock != null;
        if (player.SkillActive) SetValue(skillText, v2 ? "ACTIVE" : "Active", activeColor);
        else if (readyIn > 0f) SetValue(skillText, Seconds(readyIn), waitColor);
        else SetValue(skillText, v2 ? "READY" : "Ready", v2 ? skillColor : readyColor);

        float fill = readyIn > 0f && player.SkillWait > 0f ? 1f - readyIn / player.SkillWait : 1f;
        if (skillBarFill != null) skillBarFill.anchorMax = new Vector2(Mathf.Clamp01(fill), 1f);
        return true;
    }

    // Seconds left in the digest, else "CAN EAT", in the digest green. The bar is full right after eating and
    // drains as the digest runs out, so an empty bar means the pig can eat again. An older HUD (no digestBlock) keeps "Ready" in green and the character's tint
    bool UpdateDigest()
    {
        if (digestText == null) return false;
        bool shown = player != null && player.UsesDigest;
        if (digestBlock != null) digestBlock.SetActive(shown);
        else SetRow(digestText, shown);
        if (!shown) return false;

        if (Beta)
        {
            // The pill fills back up while the pig digests and is full again at CAN EAT
            float pill = player.IsDigesting ? Mathf.Clamp01(player.DigestProgress01) : 1f;
            string state = player.CanEat ? "CAN EAT" : Seconds(player.DigestLeft);
            SetValue(digestText, state, pill >= 0.999f ? pillDigestInk : pillWaitInk);
            if (digestBarFill != null) digestBarFill.anchorMax = new Vector2(pill, 1f);
            return true;
        }

        // HUD v2: the same green the character's body drains in while it digests (digestTint)
        bool v2 = digestBlock != null;
        Color tint = player.digestTint;
        if (player.CanEat) SetValue(digestText, v2 ? "CAN EAT" : "Ready", v2 ? tint : readyColor);
        else SetValue(digestText, Seconds(player.DigestLeft), tint);
        if (v2 && digestBarFill != null)
        {
            Image fillImage = digestBarFill.GetComponent<Image>();
            if (fillImage != null) fillImage.color = tint;
        }

        float fill = player.IsDigesting ? 1f - player.DigestProgress01 : 0f;
        if (digestBarFill != null) digestBarFill.anchorMax = new Vector2(Mathf.Clamp01(fill), 1f);
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
            if (CrossLevelStatus.IsStoreEffect(skill.id))
            {
                if (skill.armed || skill.active || skill.secondsLeft > 0f) held++;
            }
            if (held <= 0) continue;
            if (ItemStatRows) SetItemRow(ItemRow(count++), ItemName(skill.id).ToUpperInvariant(), "\u00D7" + held, waitColor);
            else SetValue(ItemRow(count++), ItemName(skill.id) + " x" + held, waitColor);
        }
        if (count == 0)
        {
            if (ItemStatRows) SetItemRow(ItemRow(count++), "ITEMS", "None", noneColor);
            else SetValue(ItemRow(count++), "None", noneColor);
        }
        for (int i = count; i < itemRows.Count; i++) ItemRowObject(itemRows[i]).SetActive(false);
    }

    // HUD v2: the template is the value of a label / value row, and each item copies the whole row
    bool ItemStatRows => itemRowTemplate != null && itemRowTemplate.transform.parent.GetComponent<HorizontalLayoutGroup>() != null;

    GameObject ItemRowObject(TMP_Text value) => ItemStatRows ? value.transform.parent.gameObject : value.gameObject;

    static void SetItemRow(TMP_Text value, string title, string text, Color color)
    {
        TMP_Text label = value.transform.parent.GetChild(0).GetComponent<TMP_Text>();
        if (label != null && label != value) label.text = title;
        SetValue(value, text, color);
    }

    // Rows are copies of the template, made once and reused
    TMP_Text ItemRow(int index)
    {
        while (itemRows.Count <= index)
        {
            TMP_Text row;
            if (ItemStatRows)
            {
                Transform template = itemRowTemplate.transform.parent;
                GameObject copy = Instantiate(template.gameObject, template.parent);
                copy.name = "Item" + itemRows.Count;
                row = copy.transform.GetChild(itemRowTemplate.transform.GetSiblingIndex()).GetComponent<TMP_Text>();
            }
            else
            {
                row = Instantiate(itemRowTemplate, itemRowTemplate.transform.parent);
                row.name = "Item" + itemRows.Count;
            }
            itemRows.Add(row);
        }
        ItemRowObject(itemRows[index]).SetActive(true);
        return itemRows[index];
    }

    // A divider shows only between two visible sections, so a hidden one never leaves two in a row
    void UpdateDividers()
    {
        Transform sidebar = SidebarRect;
        if (sidebar == null) return;
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
        if (id == "brace") return "Life";
        if (id == "time_machine") return "Time Machine";
        return string.IsNullOrEmpty(id) ? "?" : char.ToUpperInvariant(id[0]) + id.Substring(1);
    }

    void UpdateEndPanel(GameManager gm)
    {
        if (gm.IsPlaying || endPanel == null) return;
        // A story is told instead of the result, or the level goes straight on (to the store)
        if (TutorialDialogue.HidesResult || (levelLoader != null && levelLoader.SkipsResult))
        {
            if (endPanel.activeSelf) endPanel.SetActive(false);
            return;
        }

        if (!endPanel.activeSelf)
        {
            endPanel.SetActive(true);
            BuildEndCard(gm);
        }

        // The run's record is set when the run ends, which can be just after the card is built
        if (endRecord != null && endRecord.text != gm.NewRecordLine) endRecord.text = gm.NewRecordLine;

        // Every frame: the buttons wait for a story or the skill summary after the win (so does
        // GameFlow's return timer), and the return countdown ticks
        bool ready = EndActionsReady(gm);
        if (endActions != null && endActions.activeSelf != ready) endActions.SetActive(ready);
        if (endFooter != null) endFooter.text = EndFooter(gm, ready);
    }

    // The map node this round came from, so its buttons can go back. Not in the first-time tutorial run
    // (GameFlow moves it on by itself) or editor N/P play
    bool CanLeaveToMap => flow != null && flow.CurrentNode != null && !flow.InTutorialRun;

    bool EndActionsReady(GameManager gm)
    {
        return !gm.IsPlaying && endCard != null && endCard.activeInHierarchy && !leavingToMap
            && !TutorialDialogue.Playing && !SkillUnlockSummary.Playing;
    }

    // Under the buttons after a win. GameFlow goes back by itself returnDelay seconds after the win,
    // so count the same time down; Back to Map / Enter just goes sooner
    string EndFooter(GameManager gm, bool ready)
    {
        if (!gm.PlayerWon || (flow != null && flow.InTutorialRun)) return "";
        if (CanLeaveToMap)
        {
            if (!ready) { returnAt = -1f; return ""; }
            if (returnAt < 0f) returnAt = Time.unscaledTime + flow.returnDelay;
            int left = Mathf.Max(1, Mathf.CeilToInt(returnAt - Time.unscaledTime));
            return "Returning to the map in " + left + "...";
        }
        return levelLoader != null ? levelLoader.NextStepHint : "";   // editor N/P play: "Level 3 in 2..."
    }

    // Back to Map / Enter after a win: the same path as GameFlow's timer (clears the node, refills HP)
    void GoToMapAfterWin()
    {
        if (leavingToMap || flow == null) return;
        leavingToMap = true;
        flow.CompleteCurrentNode();
    }

    void LeaveAfterLoss()
    {
        if (leavingToMap || flow == null) return;
        leavingToMap = true;
        flow.ReturnToMap();
    }

    void RetryLevel()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) return;
        if (gm.PlayerWon) KeepWinCleared();
        gm.Restart();
    }

    // The node is only marked cleared on the way back to the map, so a replay after a win (Play Again
    // or R) would lose the clear if the replay were lost. Keep it, as CompleteCurrentNode would
    void KeepWinCleared()
    {
        MapNode node = flow != null ? flow.CurrentNode : null;
        if (node == null || node.type != NodeType.Battle || flow.InTutorialRun) return;
        MapHub.RefillHp();
        ProgressStore.MarkCompleted(node.id);
    }

    // Figma Version3 "End - Win / Lose / Died (U1)": a card over the dimmed arena with the level, the
    // result, how much was earned or missing, a YOU vs TARGET bar and the buttons. Built once at runtime
    // like the pause popup; the scene's Result / Detail labels are hidden
    void BuildEndCard(GameManager gm)
    {
        if (endCard != null) return;
        HUDTheme theme = PopupTheme;
        if (resultText != null) resultText.gameObject.SetActive(false);
        if (detailText != null) detailText.gameObject.SetActive(false);
        Image dim = endPanel.GetComponent<Image>();
        if (dim != null) dim.color = new Color(0f, 0f, 0f, 0.6f);

        bool won = gm.PlayerWon;
        bool died = !won && gm.PlayerDied;
        bool target = gm.UsesMoneyTarget && gm.LevelGoldTotal > 0;
        bool map = CanLeaveToMap;
        string level = levelText != null && levelText.gameObject.activeInHierarchy ? levelText.text : "";

        string title = won ? (target ? "Achieved target!" : gm.EndReason)
            : died ? "Died"
            : target ? "Didn't achieve the target" : gm.EndReason;
        string sub = won ? "Earned $" + gm.PlayerMoney.ToString("N0") + "!"
            : died ? gm.EndReason
            : target ? "$" + Mathf.Max(0, gm.MoneyTarget - gm.PlayerMoney).ToString("N0") + " short" : "";
        Color subColor = won ? theme.pig : PopupDanger;

        // Buttons: after a win, Back to Map [Enter] + Play Again [R]; after a loss, Retry [R] + Back to Map
        var buttons = new List<(PopupStyle style, string text, string key, UnityEngine.Events.UnityAction click)>();
        void AddButton(PopupStyle style, string text, string key, UnityEngine.Events.UnityAction click)
            => buttons.Add((style, text, key, click));
        if (won)
        {
            if (map) AddButton(PopupStyle.Primary, "Back to Map", "Enter", GoToMapAfterWin);
            if (map) AddButton(PopupStyle.Secondary, "Play Again", "R", RetryLevel);
        }
        else
        {
            AddButton(PopupStyle.Primary, "Retry", "R", RetryLevel);
            if (map) AddButton(PopupStyle.Secondary, "Back to Map", null, LeaveAfterLoss);
        }

        // Rows top-down, 20 px apart: (height, builder at the row's center y)
        var rows = new List<(float height, System.Action<RectTransform, float> make)>();
        void Row(float rowHeight, System.Action<RectTransform, float> make) => rows.Add((rowHeight, make));
        if (level != "")
            Row(24f, (card, y) => PopupText(card, "Level", level, 18f, FontStyles.Bold, theme.title, new Vector2(0f, y)).characterSpacing = 12f);
        Row(76f, (card, y) => PopupText(card, "Result", title, won || died ? 64f : 52f, FontStyles.Bold, Color.white, new Vector2(0f, y)));
        if (sub != "")
            Row(38f, (card, y) => PopupText(card, "Detail", sub, 30f, FontStyles.Bold, subColor, new Vector2(0f, y)));
        if (target)
            Row(72f, (card, y) => EndProgress(card, y + 2f, gm, theme));   // 4 px more space above
        // A survival run (LevelLoader waves): the time survived, the wave reached, and the gold banked
        if (levelLoader != null && levelLoader.Waves)
        {
            Row(30f, (card, y) => endRecord = PopupText(card, "Record", gm.NewRecordLine, 24f, FontStyles.Bold, theme.pig, new Vector2(0f, y)));
            Row(24f, (card, y) => PopupText(card, "Banked", won ? "$" + gm.PlayerMoney.ToString("N0") + " to your wallet" : "No gold banked (round lost)", 18f, FontStyles.Normal, theme.title, new Vector2(0f, y)));
        }
        if (gm.MinerMoney > 0)
            Row(24f, (card, y) => PopupText(card, "Miner", "Miner $" + gm.MinerMoney.ToString("N0"), 18f, FontStyles.Normal, theme.title, new Vector2(0f, y)));
        if (buttons.Count > 0)
            Row(PopupButtonHeight, (card, y) => EndButtons(card, y, buttons));
        if (won) Row(24f, (card, y) => endFooter = PopupText(card, "Footer", "", 18f, FontStyles.Normal, theme.title, new Vector2(0f, y)));

        const float width = 760f;
        const float gap = 20f;
        float height = 44f + 40f + gap * (rows.Count - 1);
        foreach (var row in rows) height += row.height;

        RectTransform cardRect = PopupCard(endPanel.transform, "EndCard", new Vector2(width, height));
        endCard = cardRect.gameObject;
        float top = height * 0.5f - 44f;
        foreach (var row in rows)
        {
            row.make(cardRect, top - row.height * 0.5f);
            top -= row.height + gap;
        }
    }

    // "YOU $180 ... TARGET $300" over a 600 px bar: the player's share of all the level's gold in pig
    // pink, and a white tick at the target (half the gold)
    void EndProgress(RectTransform card, float y, GameManager gm, HUDTheme theme)
    {
        const float width = 600f;
        string gray = "#" + ColorUtility.ToHtmlStringRGB(theme.title);
        string pink = "#" + ColorUtility.ToHtmlStringRGB(theme.pig);

        TMP_Text you = PopupText(card, "You", "<size=16><color=" + gray + "><cspace=2>YOU</cspace></color></size>  <color="
            + pink + ">$" + gm.PlayerMoney.ToString("N0") + "</color>", 30f, FontStyles.Bold, Color.white, new Vector2(0f, y + 18f));
        you.rectTransform.sizeDelta = new Vector2(width, you.rectTransform.sizeDelta.y);
        you.alignment = TextAlignmentOptions.Left;
        TMP_Text goal = PopupText(card, "Target", "<size=16><color=" + gray + "><cspace=2>TARGET</cspace></color></size>  $"
            + gm.MoneyTarget.ToString("N0"), 30f, FontStyles.Bold, Color.white, new Vector2(0f, y + 18f));
        goal.rectTransform.sizeDelta = new Vector2(width, goal.rectTransform.sizeDelta.y);
        goal.alignment = TextAlignmentOptions.Right;

        float total = Mathf.Max(1, gm.LevelGoldTotal);
        float share = Mathf.Clamp01(gm.PlayerMoney / total);
        float mark = Mathf.Clamp01(gm.MoneyTarget / total);
        float barY = y - 25f;
        RectTransform track = PopupRect(card, "Track", theme.barBack, new Vector2(width, 14f), new Vector2(0f, barY));
        RectTransform fill = PopupRect(track, "Fill", theme.pig, Vector2.zero, Vector2.zero);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(share, 1f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
        PopupRect(card, "TargetMarker", Color.white, new Vector2(4f, 22f), new Vector2(-width * 0.5f + width * mark, barY));
    }

    // The card's buttons side by side, 320 x 72, 20 px apart, centered
    void EndButtons(RectTransform card, float y,
        List<(PopupStyle style, string text, string key, UnityEngine.Events.UnityAction click)> buttons)
    {
        const float width = 320f;
        const float gap = 20f;
        var row = new GameObject("Buttons", typeof(RectTransform));
        RectTransform rect = (RectTransform)row.transform;
        rect.SetParent(card, false);
        rect.sizeDelta = new Vector2(buttons.Count * width + (buttons.Count - 1) * gap, PopupButtonHeight);
        rect.anchoredPosition = new Vector2(0f, y);
        endActions = row;

        float x = -rect.sizeDelta.x * 0.5f + width * 0.5f;
        foreach (var b in buttons)
        {
            PopupButton(rect, b.style, b.text, b.key, new Vector2(width, PopupButtonHeight), new Vector2(x, 0f), b.click);
            x += width + gap;
        }
    }

    // "+$600" next to a money label: a copy of the label, so it shares its font and size
    void SpawnPopup(TMP_Text source, int delta, Color color)
    {
        if (source == null || delta == 0) return;

        TMP_Text text = Instantiate(source, source.transform.parent);
        text.name = source.name + "Popup";
        // Version B's amounts sit in a layout row: keep the popup out of it, or the row would make
        // room for it and shift everything while it floats
        LayoutElement layout = text.GetComponent<LayoutElement>();
        if (layout == null) layout = text.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        text.text = (delta > 0 ? "+$" : "-$") + Mathf.Abs(delta).ToString("N0");
        text.color = color;
        text.transform.localScale = Vector3.one;

        Vector2 start = source.rectTransform.anchoredPosition + popupOffset;
        text.rectTransform.anchoredPosition = start;
        popups.Add(new Popup { text = text, start = start, color = color });
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
            Color c = p.color;
            c.a *= 1f - t;
            p.text.color = c;
        }
    }

    class Popup
    {
        public TMP_Text text;
        public Vector2 start;
        public Color color;
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
