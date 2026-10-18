using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Owner: D (Hook & UI)
// Tools > Gold Miner > Create HUD: builds the HUD canvas in the open scene and wires HUDController.
// Layout (HUDLayout): a bar across the top (level, pig $ VS miner $, timer), a sidebar on the right
// (Life, Wallet, Skill + Digest, Items), and the arena in the rest, which GameplayCamera draws into
public static class HUDBuilder
{
    static readonly Color Backdrop = new Color(0.07f, 0.09f, 0.15f);      // behind the panels, outside the arena
    static readonly Color Panel = new Color(0.118f, 0.165f, 0.247f);      // #1E2A3F
    static readonly Color Divider = new Color(1f, 1f, 1f, 0.12f);
    static readonly Color TitleColor = new Color(0.68f, 0.74f, 0.85f);
    static readonly Color PigColor = new Color(1f, 0.72f, 0.82f);         // pink
    static readonly Color MinerColor = new Color(1f, 0.85f, 0.4f);        // gold
    static readonly Color ReadyColor = new Color(0.55f, 0.9f, 0.6f);      // green
    static readonly Color BarBack = new Color(1f, 1f, 1f, 0.15f);
    static readonly Color BarFill = new Color(0.45f, 0.65f, 1f);
    const float Margin = 8f;                                              // gap around each panel

    [MenuItem("Tools/Gold Miner/Create HUD")]
    static void CreateHUD()
    {
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
        GameObject root = new GameObject("HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Undo.RegisterCreatedObjectUndo(root, "Create HUD");

        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
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
        CreateImage(canvas, "TopBackdrop", Backdrop, new Vector2(0f, top), Vector2.one, 0f, false);
        CreateImage(canvas, "SideBackdrop", Backdrop, new Vector2(side, 0f), new Vector2(1f, top), 0f, false);

        // Top bar: level on the left, the score in the middle, the timer on the right
        Transform topBar = CreateImage(canvas, "TopBar", Panel, new Vector2(0f, top), Vector2.one, Margin, true).transform;
        TMP_Text level = CreateText(topBar, "Level", "LV 1/10", new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(420f, 60f), 30f, TextAlignmentOptions.Left);
        level.color = TitleColor;
        TMP_Text playerMoney = CreateText(topBar, "PlayerMoney", "$0", new Vector2(0.5f, 0.5f), new Vector2(-50f, 0f), new Vector2(400f, 60f), 40f, TextAlignmentOptions.Right);
        playerMoney.rectTransform.pivot = new Vector2(1f, 0.5f);
        playerMoney.color = PigColor;
        TMP_Text versus = CreateText(topBar, "Versus", "VS", center, Vector2.zero, new Vector2(80f, 60f), 26f, TextAlignmentOptions.Center);
        versus.color = TitleColor;
        TMP_Text minerMoney = CreateText(topBar, "MinerMoney", "$0", new Vector2(0.5f, 0.5f), new Vector2(50f, 0f), new Vector2(400f, 60f), 40f, TextAlignmentOptions.Left);
        minerMoney.rectTransform.pivot = new Vector2(0f, 0.5f);
        minerMoney.color = MinerColor;
        TMP_Text timer = CreateText(topBar, "Timer", "1:30", new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(200f, 60f), 36f, TextAlignmentOptions.Right);

        // Sidebar: sections stacked top to bottom with dividers between them
        GameObject sidebar = CreateImage(canvas, "Sidebar", Panel, new Vector2(side, 0f), new Vector2(1f, top), Margin, true);
        VerticalLayoutGroup stack = sidebar.AddComponent<VerticalLayoutGroup>();
        stack.padding = new RectOffset(16, 16, 16, 16);
        SetUpStack(stack, 0f);

        GameObject lifeSection = CreateSection(sidebar.transform, "Life", "LIFE");
        TMP_Text lives = CreateValue(lifeSection.transform, "Lives", "x3", Color.white);
        CreateDivider(sidebar.transform);

        GameObject walletSection = CreateSection(sidebar.transform, "Wallet", "WALLET");
        TMP_Text wallet = CreateValue(walletSection.transform, "WalletValue", "$0", MinerColor);
        CreateDivider(sidebar.transform);

        // Skill and digest share a section: both are "can I act right now"
        GameObject skillSection = CreateSection(sidebar.transform, "Skill", "SKILL");
        TMP_Text skill = CreateValue(skillSection.transform, "SkillValue", "Ready", ReadyColor);
        RectTransform skillBarFill = CreateBar(skillSection.transform, "SkillBar");
        CreateTitle(skillSection.transform, "DIGEST");
        TMP_Text digest = CreateValue(skillSection.transform, "DigestValue", "Ready", ReadyColor);
        CreateDivider(sidebar.transform);

        // Items: HUDController adds one row per owned store item under ItemsRoot
        GameObject itemsSection = CreateSection(sidebar.transform, "Items", "ITEMS");
        GameObject itemsRoot = new GameObject("ItemsRoot", typeof(RectTransform));
        itemsRoot.transform.SetParent(itemsSection.transform, false);
        SetUpStack(itemsRoot.AddComponent<VerticalLayoutGroup>(), 4f);
        TMP_Text itemTemplate = CreateValue(itemsRoot.transform, "ItemRow", "shield x2", Color.white);
        itemTemplate.gameObject.SetActive(false);
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
        hud.playerMoneyText = playerMoney;
        hud.minerMoneyText = minerMoney;
        hud.timerText = timer;
        hud.livesText = lives;
        hud.endPanel = endPanel;
        hud.resultText = result;
        hud.detailText = detail;
        hud.levelText = level;
        hud.lifeSection = lifeSection;
        hud.walletSection = walletSection;
        hud.walletText = wallet;
        hud.skillSection = skillSection;
        hud.skillText = skill;
        hud.skillBarFill = skillBarFill;
        hud.digestText = digest;
        hud.itemsSection = itemsSection;
        hud.itemRowTemplate = itemTemplate;
        hud.itemKeyText = itemKey;

        EditorUtility.SetDirty(hud);
        Selection.activeGameObject = root;
        Debug.Log("HUDBuilder: HUD created. Save the scene to keep it.");
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

    // A thin progress bar; returns the fill, which HUDController stretches by setting anchorMax.x
    static RectTransform CreateBar(Transform parent, string name)
    {
        GameObject back = CreateImage(parent, name, BarBack, Vector2.zero, Vector2.zero, 0f, false);
        LayoutElement layout = back.AddComponent<LayoutElement>();
        layout.minHeight = 8f;
        layout.preferredHeight = 8f;

        GameObject fill = CreateImage(back.transform, "Fill", BarFill, Vector2.zero, Vector2.one, 0f, false);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMax = new Vector2(0f, 1f);   // empty until a cooldown runs
        return fillRect;
    }
}
