using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Owner: D (Hook & UI)
// Tools > Gold Miner > Create HUD > Version A / B / Beta: builds the HUD canvas in the open scene from that
// version's HUDTheme and wires HUDController.
// Layout (HUDLayout): a bar across the top (level, pig $ VS miner $, timer + Pause), a sidebar on the
// right (StatusSection: Wallet + Life rows, SkillsSection: Skill + Digest meters, ItemsSection: item rows), and the arena in the rest, which GameplayCamera draws into.
// Design source: Figma "Oinky Oinky - HUD Redesign", frames "HUD / A" and "HUD / B". The values live in HUDTheme.
// Beta (Figma Version3 "HUD - v3 topbar") has no sidebar: see CreateBetaHUD
public static class HUDBuilder
{
    // The version being built; the names below read from it
    static HUDTheme theme = new HUDTheme();
    static Color Backdrop => theme.backdrop;
    static Color Panel => theme.panel;
    static Color Divider => theme.divider;
    static Color TitleColor => theme.title;
    static Color PigColor => theme.pig;
    static Color MinerColor => theme.miner;
    static Color SkillColor => theme.skill;
    static Color DigestColor => theme.digest;
    static Color BarBack => theme.barBack;
    static int SidebarPaddingX => theme.sidebarPaddingX;
    static int SidebarPaddingY => theme.sidebarPaddingY;
    static float SectionGap => theme.sectionGap;
    static float StatusRowGap => theme.statusRowGap;
    static int MetersTop => theme.metersTop;
    static float MeterGap => theme.meterGap;
    static float StatRowHeight => theme.statRowHeight;

    [MenuItem("Tools/Gold Miner/Create HUD/Version A")]
    static void CreateHUDVersionA() { CreateHUD(HUDVariant.A); }

    [MenuItem("Tools/Gold Miner/Create HUD/Version B")]
    static void CreateHUDVersionB() { CreateHUD(HUDVariant.B); }

    [MenuItem("Tools/Gold Miner/Create HUD/Version Beta")]
    static void CreateHUDVersionBeta() { CreateHUD(HUDVariant.Beta); }

    static void CreateHUD(HUDVariant variant)
    {
        theme = HUDTheme.For(variant);

        HUDController existing = Object.FindFirstObjectByType<HUDController>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.LogWarning("HUDBuilder: this scene already has a HUD (" + existing.name + "). Delete it first to rebuild.");
            return;
        }

        if (TMP_Settings.defaultFontAsset == null)
        {
            Debug.LogError("HUDBuilder: no default TMP font. Import it via Window > TextMeshPro > Import TMP Essential Resources, then try again.");
            return;
        }

        // Canvas that scales with the window
        GameObject root = new GameObject("HUD (" + variant + ")", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Undo.RegisterCreatedObjectUndo(root, "Create HUD");

        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        root.AddComponent<GraphicRaycaster>();
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Transform canvas = root.transform;
        if (variant == HUDVariant.Beta)
        {
            CreateBetaHUD(root);
            return;
        }
        Vector2 center = new Vector2(0.5f, 0.5f);
        float top = 1f - HUDLayout.TopBarHeight;
        float side = 1f - HUDLayout.SidebarWidth;

        // Fill everything outside the arena every frame. The camera only clears its own rect,
        // so without this the old timer digits smear in the gaps between panels
        GameObject topBackdrop = CreateImage(canvas, "TopBackdrop", Backdrop, new Vector2(0f, top), Vector2.one, 0f, false);
        GameObject sideBackdrop = CreateImage(canvas, "SideBackdrop", Backdrop, new Vector2(side, 0f), new Vector2(1f, top), 0f, false);
        GameObject leftBackdrop = CreateImage(canvas, HUDController.LeftBackdropName, Backdrop, Vector2.zero, Vector2.zero, 0f, false);
        GameObject bottomBackdrop = CreateImage(canvas, HUDController.BottomBackdropName, Backdrop, Vector2.zero, Vector2.zero, 0f, false);

        // Top bar: level on the left, the score in the middle, the timer and Pause on the right
        // (HUDController.PlaceMapButton puts Pause at the right edge and the timer just left of it)
        Transform topBar = CreateImage(canvas, "TopBar", Panel, new Vector2(0f, top), Vector2.one, 0f, true).transform;
        TMP_Text level = CreateText(topBar, "Level", "LV 1/10", new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(420f, 60f), 30f, TextAlignmentOptions.Left);
        level.color = TitleColor;
        level.fontStyle = FontStyles.Bold;
        TMP_Text playerMoney, minerMoney;
        RectTransform playerBarFill = null, minerBarFill = null;
        if (theme.moneyBar)
        {
            CreateMoneyBar(topBar, out playerMoney, out minerMoney, out playerBarFill, out minerBarFill);
        }
        else
        {
            playerMoney = CreateText(topBar, "PlayerMoney", "$0", new Vector2(0.5f, 0.5f), new Vector2(-50f, 0f), new Vector2(400f, 60f), 40f, TextAlignmentOptions.Right);
            playerMoney.rectTransform.pivot = new Vector2(1f, 0.5f);
            playerMoney.color = PigColor;
            TMP_Text versus = CreateText(topBar, "Versus", "VS", center, Vector2.zero, new Vector2(80f, 60f), 26f, TextAlignmentOptions.Center);
            versus.color = TitleColor;
            minerMoney = CreateText(topBar, "MinerMoney", "$0", new Vector2(0.5f, 0.5f), new Vector2(50f, 0f), new Vector2(400f, 60f), 40f, TextAlignmentOptions.Left);
            minerMoney.rectTransform.pivot = new Vector2(0f, 0.5f);
            minerMoney.color = MinerColor;
        }
        TMP_Text timer = CreateText(topBar, "Timer", "1:30", new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(200f, 60f), 36f, TextAlignmentOptions.Right);
        if (theme.timerBold) timer.fontStyle = FontStyles.Bold;
        Button map = HUDController.CreateMapButton(timer);

        // Sidebar: sections stacked top to bottom with dividers between them
        GameObject sidebar = CreateImage(canvas, "Sidebar", Panel, new Vector2(side, 0f), new Vector2(1f, top), 0f, true);
        HUDLayout.PlaceFrame((RectTransform)topBar, (RectTransform)sidebar.transform, (RectTransform)topBackdrop.transform,
            (RectTransform)sideBackdrop.transform, (RectTransform)leftBackdrop.transform, (RectTransform)bottomBackdrop.transform);
        VerticalLayoutGroup stack = sidebar.AddComponent<VerticalLayoutGroup>();
        stack.padding = new RectOffset(SidebarPaddingX, SidebarPaddingX, SidebarPaddingY, SidebarPaddingY);
        SetUpStack(stack, SectionGap);
        stack.childAlignment = TextAnchor.UpperCenter;

        // StatusSection: what the player has. WALLET and LIFE as label / value rows
        GameObject statusSection = new GameObject("StatusSection", typeof(RectTransform));
        statusSection.transform.SetParent(sidebar.transform, false);
        SetUpStack(statusSection.AddComponent<VerticalLayoutGroup>(), StatusRowGap);
        GameObject walletSection = CreateStatRow(statusSection.transform, "Wallet", "WALLET", "WalletValue", "$0", out TMP_Text wallet);
        GameObject lifeSection = CreateStatRow(statusSection.transform, "Life", "LIFE", "Lives", "\u00D73", out TMP_Text lives);
        CreateDivider(sidebar.transform);

        // Skill and digest share a section: both are "can I act right now". Each is a meter:
        // a row with the title on the left and the state on the right, and a bar under it
        // SkillsSection: what the player can do right now
        GameObject skillSection = new GameObject("SkillsSection", typeof(RectTransform));
        skillSection.transform.SetParent(sidebar.transform, false);
        VerticalLayoutGroup meters = skillSection.AddComponent<VerticalLayoutGroup>();
        SetUpStack(meters, MeterGap);
        meters.childAlignment = TextAnchor.UpperCenter;
        meters.padding = new RectOffset(0, 0, MetersTop, 0);
        GameObject skillBlock = CreateMeter(skillSection.transform, "Skill", "SKILL", "READY", SkillColor, out TMP_Text skill, out RectTransform skillBarFill);
        GameObject digestBlock = CreateMeter(skillSection.transform, "Digest", "DIGEST", "CAN EAT", DigestColor, out TMP_Text digest, out RectTransform digestBarFill);
        CreateDivider(sidebar.transform);

        // ItemsSection: rows like WALLET / LIFE, one per owned store item ("SHIELD  ×2"), or
        // "ITEMS  None". HUDController copies the hidden template row
        GameObject itemsSection = new GameObject("ItemsSection", typeof(RectTransform));
        itemsSection.transform.SetParent(sidebar.transform, false);
        SetUpStack(itemsSection.AddComponent<VerticalLayoutGroup>(), StatusRowGap);
        GameObject itemRow = CreateStatRow(itemsSection.transform, "ItemRow", "ITEMS", "ItemValue", "None", out TMP_Text itemTemplate);
        itemRow.SetActive(false);
        // No key uses items during a round yet; the badge stays hidden until one does
        TMP_Text itemKey = CreateValue(itemsSection.transform, "ItemKey", "E", TitleColor);
        itemKey.gameObject.SetActive(false);

        // End panel: covers the arena only, so the score in the bars stays readable
        GameObject endPanel = CreateImage(canvas, "EndPanel", new Color(0f, 0f, 0f, 0.65f), Vector2.zero, new Vector2(side, top), 0f, false);
        TMP_Text result = CreateText(endPanel.transform, "Result", "YOU WIN", center, new Vector2(0f, 90f), new Vector2(1200f, 140f), 110f, TextAlignmentOptions.Center);
        TMP_Text detail = CreateText(endPanel.transform, "Detail", "Time's up!\nPress R to restart", center, new Vector2(0f, -80f), new Vector2(1400f, 300f), 40f, TextAlignmentOptions.Center);

        // HUDController hides the panel on Start; keep it hidden in the editor too
        endPanel.SetActive(false);

        HUDController hud = root.AddComponent<HUDController>();
        hud.variant = variant;
        hud.playerMoneyText = playerMoney;
        hud.playerBarFill = playerBarFill;
        hud.minerBarFill = minerBarFill;
        hud.minerMoneyText = minerMoney;
        hud.timerText = timer;
        hud.mapButton = map;
        hud.livesText = lives;
        hud.endPanel = endPanel;
        hud.resultText = result;
        hud.detailText = detail;
        hud.levelText = level;
        hud.sidebar = (RectTransform)sidebar.transform;
        hud.statusSection = statusSection;
        hud.lifeSection = lifeSection;
        hud.walletSection = walletSection;
        hud.walletText = wallet;
        hud.skillSection = skillSection;
        hud.skillText = skill;
        hud.skillBarFill = skillBarFill;
        hud.skillBlock = skillBlock;
        hud.digestText = digest;
        hud.digestBarFill = digestBarFill;
        hud.digestBlock = digestBlock;
        hud.itemsSection = itemsSection;
        hud.itemRowTemplate = itemTemplate;
        hud.itemKeyText = itemKey;

        EditorUtility.SetDirty(hud);
        Selection.activeGameObject = root;
        Debug.Log("HUDBuilder: HUD version " + variant + " created. Save the scene to keep it.");
    }

    // ---------------------------------------------------------------------------------------------
    // HUD Beta: Figma "Oinky Oinky - HUD Redesign" > Version3 > "HUD - v3 topbar".
    // No sidebar. One 92 px row of panels across the top (HUDLayout.TopBarOnly), left to right:
    //   PigStatus  : pig avatar, SKILL pill (READY / ACTIVE / seconds), DIGEST pill (CAN EAT / seconds)
    //   Money      : "YOU $320"  timer  "$230 MINER", the tug bar under them (split by each side's share)
    //   LevelPause : LEVEL n and the pause button
    // The Figma Coins panel is not built yet (no coins in the game). Life and Items are not shown.
    // Positions are Figma's, in canvas units from each panel's top left corner
    static void CreateBetaHUD(GameObject root)
    {
        Transform canvas = root.transform;
        Vector2 center = new Vector2(0.5f, 0.5f);
        bool wasTopBarOnly = HUDLayout.TopBarOnly;
        HUDLayout.TopBarOnly = true;   // PlaceFrame and the end panel read the Beta split
        try
        {
            float top = 1f - HUDLayout.TopBarHeight;

            GameObject topBackdrop = CreateImage(canvas, "TopBackdrop", Backdrop, new Vector2(0f, top), Vector2.one, 0f, false);
            GameObject sideBackdrop = CreateImage(canvas, "SideBackdrop", Backdrop, Vector2.zero, Vector2.zero, 0f, false);
            GameObject leftBackdrop = CreateImage(canvas, HUDController.LeftBackdropName, Backdrop, Vector2.zero, Vector2.zero, 0f, false);
            GameObject bottomBackdrop = CreateImage(canvas, HUDController.BottomBackdropName, Backdrop, Vector2.zero, Vector2.zero, 0f, false);

            // The bar itself is see-through: the panels sit on the backdrop
            GameObject topBarGo = new GameObject("TopBar", typeof(RectTransform));
            RectTransform topBar = (RectTransform)topBarGo.transform;
            topBar.SetParent(canvas, false);
            HUDLayout.PlaceFrame(topBar, null, (RectTransform)topBackdrop.transform, (RectTransform)sideBackdrop.transform,
                (RectTransform)leftBackdrop.transform, (RectTransform)bottomBackdrop.transform);

            // PigStatus ----------------------------------------------------------------------------
            RectTransform pig = BetaPanel(topBar, "PigStatus", new Vector2(0f, 0.5f), 0f, 360f);
            CreatePigAvatar(pig);

            RectTransform skillRow = Stretch(pig, "Skill");
            BetaText(skillRow, "SkillLabel", "SKILL", 90f, 10f, 86f, 32f, 14f, TextAlignmentOptions.Left, theme.betaLabel, 10f);
            TMP_Text skill = BetaPill(skillRow, "SkillBar", 182f, 10f, 160f, 32f, SkillColor, "READY", 15f, theme.betaSkillInk, out RectTransform skillFill);

            RectTransform digestRow = Stretch(pig, "Digest");
            BetaText(digestRow, "DigestLabel", "DIGEST", 90f, 52f, 86f, 18f, 12f, TextAlignmentOptions.Left, theme.betaLabel, 10f);
            TMP_Text digest = BetaPill(digestRow, "DigestBar", 182f, 52f, 160f, 18f, DigestColor, "CAN EAT", 11f, theme.betaDigestInk, out RectTransform digestFill);

            // Money --------------------------------------------------------------------------------
            const float moneyWidth = 1016f;
            RectTransform money = BetaPanel(topBar, "Money", center, 0f, moneyWidth);
            Transform playerSide = BetaMoneySide(money, "PlayerSide", 24f, 0f);
            BetaText(playerSide, "YouLabel", "YOU", 0f, 0f, 0f, 0f, 13f, TextAlignmentOptions.Center, theme.betaLabel, 10f);
            TMP_Text playerMoney = BetaText(playerSide, "PlayerMoney", "$0", 0f, 0f, 0f, 0f, 30f, TextAlignmentOptions.Center, PigColor, 0f);
            Transform minerSide = BetaMoneySide(money, "MinerSide", -24f, 1f);
            TMP_Text minerMoney = BetaText(minerSide, "MinerMoney", "$0", 0f, 0f, 0f, 0f, 30f, TextAlignmentOptions.Center, MinerColor, 0f);
            BetaText(minerSide, "MinerLabel", "MINER", 0f, 0f, 0f, 0f, 13f, TextAlignmentOptions.Center, theme.betaLabel, 10f);

            TMP_Text timer = BetaText(money, "Timer", "1:30", (moneyWidth - 200f) / 2f, 10f, 200f, 40f, 32f, TextAlignmentOptions.Center, Color.white, 0f);

            // The tug bar always spans the panel; HUDController splits it player : miner
            GameObject bar = new GameObject("MoneyBar", typeof(RectTransform));
            RectTransform barRect = (RectTransform)bar.transform;
            barRect.SetParent(money, false);
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.sizeDelta = new Vector2(-48f, 8f);
            barRect.anchoredPosition = new Vector2(0f, -58f);
            RectTransform playerFill = (RectTransform)CreateImage(barRect, "PlayerFill", theme.betaPlayerBar, Vector2.zero, new Vector2(0.5f, 1f), 0f, false).transform;
            RectTransform minerFill = (RectTransform)CreateImage(barRect, "MinerFill", theme.betaMinerBar, new Vector2(0.5f, 0f), Vector2.one, 0f, false).transform;
            Round(playerFill.GetComponent<Image>(), 4f);
            Round(minerFill.GetComponent<Image>(), 4f);
            playerFill.offsetMax = new Vector2(-2f, 0f);
            minerFill.offsetMin = new Vector2(2f, 0f);

            // LevelPause ---------------------------------------------------------------------------
            RectTransform levelPanel = BetaPanel(topBar, "LevelPause", new Vector2(1f, 0.5f), 0f, 230f);
            TMP_Text level = BetaText(levelPanel, "Level", "LEVEL 1", 22f, 20f, 130f, 40f, 17f, TextAlignmentOptions.Left, theme.betaLevel, 12f);
            Button pause = CreatePauseButton(levelPanel);

            // End panel: covers the arena only, so the score in the bar stays readable
            GameObject endPanel = CreateImage(canvas, "EndPanel", new Color(0f, 0f, 0f, 0.65f), Vector2.zero, new Vector2(1f, top), 0f, false);
            TMP_Text result = CreateText(endPanel.transform, "Result", "YOU WIN", center, new Vector2(0f, 90f), new Vector2(1200f, 140f), 110f, TextAlignmentOptions.Center);
            TMP_Text detail = CreateText(endPanel.transform, "Detail", "Time's up!\nPress R to restart", center, new Vector2(0f, -80f), new Vector2(1400f, 300f), 40f, TextAlignmentOptions.Center);
            endPanel.SetActive(false);

            HUDController hud = root.AddComponent<HUDController>();
            hud.variant = HUDVariant.Beta;
            hud.topBar = topBar;
            hud.moneyPanel = money.gameObject;
            hud.levelPanel = levelPanel.gameObject;
            hud.playerMoneyText = playerMoney;
            hud.minerMoneyText = minerMoney;
            hud.playerBarFill = playerFill;
            hud.minerBarFill = minerFill;
            hud.timerText = timer;
            hud.mapButton = pause;
            hud.levelText = level;
            hud.skillSection = pig.gameObject;   // the whole panel hides when the character has neither
            hud.skillBlock = skillRow.gameObject;
            hud.skillText = skill;
            hud.skillBarFill = skillFill;
            hud.digestBlock = digestRow.gameObject;
            hud.digestText = digest;
            hud.digestBarFill = digestFill;
            hud.pillSkillInk = theme.betaSkillInk;
            hud.pillDigestInk = theme.betaDigestInk;
            hud.playerPopupColor = PigColor;
            hud.moneyFlashColor = new Color32(0xD4, 0xC6, 0xFF, 0xFF);   // miner flash: a light purple
            hud.endPanel = endPanel;
            hud.resultText = result;
            hud.detailText = detail;

            EditorUtility.SetDirty(hud);
            Selection.activeGameObject = root;
            Debug.Log("HUDBuilder: HUD version Beta created. Save the scene to keep it.");
        }
        finally
        {
            HUDLayout.TopBarOnly = wasTopBarOnly;
        }
    }

    // An 80 px tall rounded panel, vertically centered in the top bar, pinned at anchorX (0 left, 0.5 center, 1 right)
    static RectTransform BetaPanel(Transform topBar, string name, Vector2 anchor, float x, float width)
    {
        GameObject go = CreateImage(topBar, name, theme.betaPanel, anchor, anchor, 0f, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.pivot = anchor;
        rect.sizeDelta = new Vector2(width, 80f);
        rect.anchoredPosition = new Vector2(x, 0f);
        Round(go.GetComponent<Image>(), 12f);
        return rect;
    }

    // An empty child filling its parent, so a group of children can be hidden together
    static RectTransform Stretch(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    // Puts a rect at Figma coordinates: x, y from the parent's top left corner
    static void TopLeft(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, -y);
    }

    // Bold text at Figma coordinates (a zero size means a layout group places it)
    static TMP_Text BetaText(Transform parent, string name, string text, float x, float y, float width, float height,
        float size, TextAlignmentOptions alignment, Color color, float spacing)
    {
        TMP_Text label = CreateText(parent, name, text, new Vector2(0f, 1f), Vector2.zero, Vector2.zero, size, alignment);
        if (width > 0f) TopLeft(label.rectTransform, x, y, width, height);
        label.color = color;
        label.fontStyle = FontStyles.Bold;
        label.characterSpacing = spacing;
        return label;
    }

    // "SkillBar": a rounded track that masks a fill (HUDController sets its anchorMax.x) with the
    // state centered on top. Returns the state text
    static TMP_Text BetaPill(Transform parent, string name, float x, float y, float width, float height, Color color,
        string state, float size, Color ink, out RectTransform fill)
    {
        GameObject track = CreateImage(parent, name, theme.betaPillTrack, Vector2.zero, Vector2.zero, 0f, false);
        TopLeft((RectTransform)track.transform, x, y, width, height);
        Round(track.GetComponent<Image>(), height / 2f);
        track.AddComponent<Mask>().showMaskGraphic = true;

        fill = (RectTransform)CreateImage(track.transform, "Fill", color, Vector2.zero, Vector2.one, 0f, false).transform;

        TMP_Text text = StretchText(track.transform, "State", state, size);
        text.color = ink;
        text.fontStyle = FontStyles.Bold;
        text.characterSpacing = 14f;
        return text;
    }

    // A label stretched over its parent
    static TMP_Text StretchText(Transform parent, string name, string text, float size)
    {
        TMP_Text label = CreateText(parent, name, text, Vector2.zero, Vector2.zero, Vector2.zero, size, TextAlignmentOptions.Center);
        RectTransform rect = label.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return label;
    }

    // "YOU $0" pinned 24 px in from the left, "$0 MINER" from the right; amounts grow inward
    static Transform BetaMoneySide(RectTransform panel, string name, float x, float pivotX)
    {
        GameObject side = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)side.transform;
        rect.SetParent(panel, false);
        rect.anchorMin = rect.anchorMax = new Vector2(pivotX, 1f);
        rect.pivot = new Vector2(pivotX, 0.5f);
        rect.anchoredPosition = new Vector2(x, -32f);   // the row's middle, 32 px down (Figma $320: y 12, 40 tall)
        HorizontalLayoutGroup line = side.AddComponent<HorizontalLayoutGroup>();
        line.childAlignment = pivotX > 0.5f ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
        line.spacing = 10f;
        line.childControlWidth = true;
        line.childControlHeight = true;
        line.childForceExpandWidth = false;
        line.childForceExpandHeight = false;
        ContentSizeFitter fit = side.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return side.transform;
    }

    // The pig face from Figma, in a 64 px dark circle. Pink circles for the head and snout; the ears
    // use the project's Triangle sprite when it is there
    static void CreatePigAvatar(RectTransform panel)
    {
        Color pink = theme.pig;
        RectTransform avatar = Shape(panel, "PigAvatar", new Color32(0x2A, 0x26, 0x22, 0xFF), 10f, 8f, 64f, 64f, 32f);
        Sprite triangle = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Triangle.png");
        if (triangle != null)
        {
            Shape(avatar, "EarL", pink, 13f, 8f, 16f, 16f, 0f).GetComponent<Image>().sprite = triangle;
            Shape(avatar, "EarR", pink, 35f, 8f, 16f, 16f, 0f).GetComponent<Image>().sprite = triangle;
        }
        Shape(avatar, "Head", pink, 10f, 14f, 44f, 40f, 20f);
        Color dark = new Color32(0x2A, 0x26, 0x22, 0xFF);
        Shape(avatar, "EyeL", dark, 22f, 26f, 5f, 5f, 2.5f);
        Shape(avatar, "EyeR", dark, 37f, 26f, 5f, 5f, 2.5f);
        Shape(avatar, "Snout", new Color32(0xE0, 0x70, 0x9A, 0xFF), 23f, 35f, 18f, 12f, 6f);
        Color nostril = new Color32(0x7A, 0x2E, 0x48, 0xFF);
        Shape(avatar, "NostrilL", nostril, 28f, 39f, 3f, 4f, 1.5f);
        Shape(avatar, "NostrilR", nostril, 35f, 39f, 3f, 4f, 1.5f);
    }

    static RectTransform Shape(Transform parent, string name, Color color, float x, float y, float width, float height, float radius)
    {
        GameObject go = CreateImage(parent, name, color, Vector2.zero, Vector2.zero, 0f, false);
        RectTransform rect = (RectTransform)go.transform;
        TopLeft(rect, x, y, width, height);
        if (radius > 0f) Round(go.GetComponent<Image>(), radius);
        return rect;
    }

    // 56 px dark rounded button with a white pause icon, 12 px in from the panel's right edge.
    // HUDController hooks it up (Pause popup) and shows it only when the map opened this round
    static Button CreatePauseButton(RectTransform panel)
    {
        GameObject go = CreateImage(panel, "MapButton", Color.white, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), 0f, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(56f, 56f);
        rect.anchoredPosition = new Vector2(-12f, 0f);
        Image image = go.GetComponent<Image>();
        Round(image, 10f);
        image.raycastTarget = true;

        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = theme.betaButton;
        colors.highlightedColor = new Color32(0x2C, 0x36, 0x31, 0xFF);
        colors.pressedColor = new Color32(0x12, 0x17, 0x14, 0xFF);
        colors.selectedColor = theme.betaButton;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        foreach (float x in new[] { -6f, 6f })
        {
            GameObject barGo = CreateImage(rect, x < 0f ? "BarL" : "BarR", Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), 0f, false);
            RectTransform bar = (RectTransform)barGo.transform;
            bar.sizeDelta = new Vector2(6f, 20f);
            bar.anchoredPosition = new Vector2(x, 0f);
            Round(barGo.GetComponent<Image>(), 1.5f);
        }
        return button;
    }

    // Rounded corners of `radius` canvas units from one shared sprite (a 32 px radius circle,
    // 9-sliced); pixelsPerUnitMultiplier scales its corners down to the radius asked for
    const string RoundedSpritePath = "Assets/Sprites/HUDRounded.png";
    const float RoundedSpriteRadius = 32f;

    static void Round(Image image, float radius)
    {
        Sprite sprite = RoundedSprite();
        if (sprite == null || radius <= 0f) return;
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = RoundedSpriteRadius / radius;
    }

    static Sprite RoundedSprite()
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(RoundedSpritePath);
        if (sprite != null) return sprite;

        const int size = 66;   // 32 px corners and a 2 px middle to stretch
        float r = RoundedSpriteRadius;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(0f, Mathf.Max(r - (x + 0.5f), x + 0.5f - (size - r)));
                float dy = Mathf.Max(0f, Mathf.Max(r - (y + 0.5f), y + 0.5f - (size - r)));
                float alpha = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(RoundedSpritePath));
        File.WriteAllBytes(RoundedSpritePath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(RoundedSpritePath);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(RoundedSpritePath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.spriteBorder = new Vector4(r, r, r, r);
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(RoundedSpritePath);
    }

    // A white TMP label anchored and pivoted at the same corner, so position is the margin from it
    static TMP_Text CreateText(Transform parent, string name, string text, Vector2 anchor, Vector2 position, Vector2 size, float fontSize, TextAlignmentOptions alignment)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = Color.white;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        return label;
    }

    // A flat image stretched between two anchors (fractions of the parent), inset by `inset` px.
    // Rounded uses Unity's built-in rounded UI sprite, sliced so the corners keep their shape
    // Version B score: the bar is fixed at the center of the top bar; "YOU $0" hangs off its left end
    // and "$0 MINER" off its right end, so longer amounts grow outward and never move the bar.
    // The bar is a rounded track that masks two fills: pink from the left, gold from the right
    static void CreateMoneyBar(Transform topBar, out TMP_Text playerMoney, out TMP_Text minerMoney,
        out RectTransform playerFill, out RectTransform minerFill)
    {
        Vector2 center = new Vector2(0.5f, 0.5f);
        GameObject track = CreateImage(topBar, "MoneyBar", BarBack, center, center, 0f, true);
        RectTransform trackRect = (RectTransform)track.transform;
        trackRect.sizeDelta = new Vector2(theme.moneyBarWidth, theme.moneyBarHeight);
        trackRect.anchoredPosition = Vector2.zero;
        track.AddComponent<Mask>().showMaskGraphic = true;
        playerFill = (RectTransform)CreateImage(track.transform, "PlayerFill", PigColor, Vector2.zero, new Vector2(0f, 1f), 0f, false).transform;
        minerFill = (RectTransform)CreateImage(track.transform, "MinerFill", MinerColor, new Vector2(1f, 0f), Vector2.one, 0f, false).transform;

        float edge = theme.moneyBarWidth * 0.5f + theme.moneyGap;
        Transform playerSide = MoneySide(topBar, "PlayerSide", -edge, 1f);
        MoneyLabel(playerSide, "YouLabel", "YOU");
        playerMoney = MoneyAmount(playerSide, "PlayerMoney", PigColor);

        Transform minerSide = MoneySide(topBar, "MinerSide", edge, 0f);
        minerMoney = MoneyAmount(minerSide, "MinerMoney", MinerColor);
        MoneyLabel(minerSide, "MinerLabel", "MINER");
    }

    // A row that sizes to its content and is pinned at one end (pivotX 1: its right end, 0: its left)
    static Transform MoneySide(Transform topBar, string name, float x, float pivotX)
    {
        GameObject side = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)side.transform;
        rect.SetParent(topBar, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(pivotX, 0.5f);
        rect.anchoredPosition = new Vector2(x, 0f);
        HorizontalLayoutGroup line = side.AddComponent<HorizontalLayoutGroup>();
        line.childAlignment = pivotX > 0.5f ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
        line.spacing = theme.moneyGap;
        line.childControlWidth = true;
        line.childControlHeight = true;
        line.childForceExpandWidth = false;
        line.childForceExpandHeight = false;
        ContentSizeFitter fit = side.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return side.transform;
    }

    static void MoneyLabel(Transform parent, string name, string text)
    {
        TMP_Text label = CreateText(parent, name, text, Vector2.zero, Vector2.zero, Vector2.zero, theme.moneyLabelSize, TextAlignmentOptions.Center);
        label.color = TitleColor;
        label.fontStyle = FontStyles.Bold;
    }

    static TMP_Text MoneyAmount(Transform parent, string name, Color color)
    {
        TMP_Text amount = CreateText(parent, name, "$0", Vector2.zero, Vector2.zero, Vector2.zero, 40f, TextAlignmentOptions.Center);
        amount.color = color;
        amount.fontStyle = FontStyles.Bold;
        return amount;
    }

    static GameObject CreateImage(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, float inset, bool rounded)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);

        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        if (rounded)
        {
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
        }
        return go;
    }

    // Children fill the width, keep their own height, and are centered
    static void SetUpStack(VerticalLayoutGroup stack, float spacing)
    {
        stack.childAlignment = TextAnchor.MiddleCenter;
        stack.spacing = spacing;
        stack.childControlWidth = true;
        stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;
    }

    // A sidebar section: a centered column that takes an equal share of the sidebar's height
    static GameObject CreateSection(Transform parent, string name, string title)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<LayoutElement>().flexibleHeight = 1f;
        SetUpStack(go.AddComponent<VerticalLayoutGroup>(), 4f);
        CreateTitle(go.transform, title);
        return go;
    }

    static TMP_Text CreateTitle(Transform parent, string title)
    {
        TMP_Text text = CreateText(parent, title + "Title", title, Vector2.zero, Vector2.zero, Vector2.zero, 22f, TextAlignmentOptions.Center);
        text.color = TitleColor;
        text.characterSpacing = 4f;
        return text;
    }

    static TMP_Text CreateValue(Transform parent, string name, string value, Color color)
    {
        TMP_Text text = CreateText(parent, name, value, Vector2.zero, Vector2.zero, Vector2.zero, 32f, TextAlignmentOptions.Center);
        text.color = color;
        return text;
    }

    static void CreateDivider(Transform parent)
    {
        GameObject go = CreateImage(parent, "Divider", Divider, Vector2.zero, Vector2.zero, 0f, false);
        LayoutElement layout = go.AddComponent<LayoutElement>();
        layout.minHeight = 2f;
        layout.preferredHeight = 2f;
    }

    // "Wallet": a row with the bold title on the left and the bold value on the right
    static GameObject CreateStatRow(Transform parent, string name, string title, string valueName, string value, out TMP_Text valueText)
    {
        GameObject row = new GameObject(name, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        LayoutElement layout = row.AddComponent<LayoutElement>();
        layout.minHeight = StatRowHeight;
        layout.preferredHeight = StatRowHeight;
        layout.flexibleHeight = 0f;   // else the row's layout group asks for spare height and rows spread apart
        HorizontalLayoutGroup line = row.AddComponent<HorizontalLayoutGroup>();
        line.childAlignment = TextAnchor.MiddleLeft;
        line.childControlWidth = true;
        line.childControlHeight = true;
        line.childForceExpandWidth = true;
        line.childForceExpandHeight = false;

        TMP_Text titleText = CreateText(row.transform, name + "Title", title, Vector2.zero, Vector2.zero, Vector2.zero, 24f, TextAlignmentOptions.Left);
        titleText.color = TitleColor;
        titleText.fontStyle = FontStyles.Bold;
        valueText = CreateText(row.transform, valueName, value, Vector2.zero, Vector2.zero, Vector2.zero, 24f, TextAlignmentOptions.Right);
        valueText.color = Color.white;
        valueText.fontStyle = FontStyles.Bold;
        return row;
    }

    // "SkillBlock": a "SkillRow" (bold title left, small state right) over a "SkillBar". Returns the block
    static GameObject CreateMeter(Transform parent, string name, string title, string value, Color color, out TMP_Text valueText, out RectTransform barFill)
    {
        GameObject block = new GameObject(name + "Block", typeof(RectTransform));
        block.transform.SetParent(parent, false);
        SetUpStack(block.AddComponent<VerticalLayoutGroup>(), 10f);

        GameObject row = new GameObject(name + "Row", typeof(RectTransform));
        row.transform.SetParent(block.transform, false);
        HorizontalLayoutGroup line = row.AddComponent<HorizontalLayoutGroup>();
        line.childAlignment = TextAnchor.LowerLeft;
        line.childControlWidth = true;
        line.childControlHeight = true;
        line.childForceExpandWidth = true;
        line.childForceExpandHeight = false;

        TMP_Text titleText = CreateText(row.transform, name + "Title", title, Vector2.zero, Vector2.zero, Vector2.zero, 24f, TextAlignmentOptions.BottomLeft);
        titleText.color = TitleColor;
        titleText.fontStyle = FontStyles.Bold;
        valueText = CreateText(row.transform, name + "Value", value, Vector2.zero, Vector2.zero, Vector2.zero, 16f, TextAlignmentOptions.BottomRight);
        valueText.color = color;

        barFill = CreateBar(block.transform, name + "Bar", color);
        return block;
    }

    // A thin rounded progress bar; returns the fill, which HUDController stretches by setting anchorMax.x
    static RectTransform CreateBar(Transform parent, string name, Color fillColor)
    {
        GameObject back = CreateImage(parent, name, BarBack, Vector2.zero, Vector2.zero, 0f, true);
        LayoutElement layout = back.AddComponent<LayoutElement>();
        layout.minHeight = 8f;
        layout.preferredHeight = 8f;

        GameObject fill = CreateImage(back.transform, "Fill", fillColor, Vector2.zero, Vector2.one, 0f, true);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMax = new Vector2(0f, 1f);   // empty until a cooldown runs
        return fillRect;
    }
}
