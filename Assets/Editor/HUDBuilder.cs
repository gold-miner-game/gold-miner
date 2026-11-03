using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Owner: D (Hook & UI)
// Tools > Gold Miner > Create HUD > Version A / B: builds the HUD canvas in the open scene from that
// version's HUDTheme and wires HUDController.
// Layout (HUDLayout): a bar across the top (level, pig $ VS miner $, timer + Pause), a sidebar on the
// right (StatusSection: Wallet + Life rows, SkillsSection: Skill + Digest meters, ItemsSection: item rows), and the arena in the rest, which GameplayCamera draws into.
// Design source: Figma "Oinky Oinky - HUD Redesign", frames "HUD / A" and "HUD / B". The values live in HUDTheme
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
