using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Shop counter. Items sit on the shelf; hovering one shows its description.
// No shopkeeper. Built from Unity UI shapes, not imported art.
// The Store scene holds the counter, wallet bar, Leave button and tip as real objects (Tools > Gold
// Miner > Build Store UI), so they can be moved and restyled in the Scene view; the items on the
// shelf are still made at runtime, under the Counter, one per offer and named after it (e.g.
// "Swift"), so a TutorialUIHole can point at them. Without the scene objects it builds its own.
public class StoreView : MonoBehaviour
{
    static StoreView active;
    static Sprite white;
    static Sprite circle;
    const int ShelfSize = 3;
    static readonly System.Random shopRandom = new System.Random();
    static List<ShopOffer> shelfOffers;
    static bool shufflePending = true;
    static readonly HashSet<string> purchasedThisShuffle = new HashSet<string>();

    // Keep the same selection across visits and purchases until another round finishes.
    public static void ShuffleStock()
    {
        shelfOffers = null;
        purchasedThisShuffle.Clear();
        shufflePending = true;
    }

    static List<ShopOffer> ShelfOffers()
    {
        if (shelfOffers != null) return shelfOffers;
        var pool = Offers();
        pool.RemoveAll(offer => PurchasedThisShuffle(offer.id));
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int swap = shopRandom.Next(i + 1);
            ShopOffer offer = pool[i];
            pool[i] = pool[swap];
            pool[swap] = offer;
        }
        shelfOffers = pool.GetRange(0, Mathf.Min(ShelfSize, pool.Count));
        return shelfOffers;
    }

    [Header("Scene objects (Build Store UI fills these)")]
    [SerializeField] Canvas canvas;
    [SerializeField] Text walletText;
    [SerializeField] Text itemsText;
    [SerializeField] Button leaveButton;
    [SerializeField] Text leaveLabel;
    [SerializeField] RectTransform counter;
    [SerializeField] RectTransform tip;
    [SerializeField] Text tipText;
    StoreItemDemo itemDemo;
    RectTransform demoTip;
    Action onLeave;
    readonly List<GameObject> items = new List<GameObject>();
    readonly List<ShopOffer> itemOffers = new List<ShopOffer>();
    readonly List<GameObject> shufflePreviews = new List<GameObject>();
    readonly List<Vector2> shufflePositions = new List<Vector2>();
    Coroutine shuffleRoutine;
    Text shuffleLabel;
    bool shuffling;

    public static void Show(Action leave)
    {
        StoreView view = Ensure();
        view.onLeave = leave;
        view.canvas.gameObject.SetActive(true);
        view.leaveLabel.text = "Leave";
        view.HideTip();
        view.FillShelf();
        view.Refresh();
        PlayAnalytics.StoreOpen(AffordableCount());
        if (shufflePending && view.items.Count > 0)
            view.shuffleRoutine = view.StartCoroutine(view.AnimateShuffle());
    }

    // Items still on the shelf that the wallet pays for
    public static int AffordableCount()
    {
        CrossLevelStatus player = CrossLevelStatus.Active;
        if (player == null) return 0;
        int count = 0;
        foreach (ShopOffer offer in ShelfOffers())
            if (!PurchasedThisShuffle(offer.id) && player.getWallet() >= offer.price) count++;
        return count;
    }

    public static void Hide()
    {
        if (active != null && active.canvas != null)
        {
            active.StopShuffle();
            active.canvas.gameObject.SetActive(false);
        }
    }

    static StoreView Ensure()
    {
        if (active != null) return active;
        StoreView inScene = FindFirstObjectByType<StoreView>(FindObjectsInactive.Include);
        if (inScene != null) return inScene;
        var go = new GameObject("StoreView");
        return go.AddComponent<StoreView>();
    }

    void Awake()
    {
        if (active != null && active != this)
        {
            Destroy(gameObject);
            return;
        }

        active = this;
        if (FindFirstObjectByType<EventSystem>() == null) MakeEventSystem(transform);
        MakeSprites();
        if (canvas == null) Build();
        else CraftBaked();   // art: the store baked into the scene gets the same papercraft look
        if (leaveButton != null) leaveButton.onClick.AddListener(() =>
        {
            PlayAnalytics.StoreLeave();
            if (onLeave != null) onLeave();
        });
        canvas.gameObject.SetActive(false);
    }

    static void MakeEventSystem(Transform parent)
    {
        var events = new GameObject("StoreEventSystem");
        events.transform.SetParent(parent, false);
        events.AddComponent<EventSystem>();
        // Input System package: StandaloneInputModule often gets no clicks in this project.
        var ui = events.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        if (ui != null) ui.AssignDefaultActions();
    }

    void OnDestroy()
    {
        StopShuffle();
        if (active == this) active = null;
    }

    void OnDisable() { StopShuffle(); }

    // Lays the store out as objects in the open scene, for editing (Tools > Gold Miner > Build Store
    // UI). Its panels are plain white images (no generated sprite, which a scene can't keep)
    public void BuildInScene()
    {
        white = null;
        Build();
        if (FindFirstObjectByType<EventSystem>() == null) MakeEventSystem(null);
    }

    static void MakeSprites()
    {
        if (white == null)
        {
            var sheet = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
            sheet.SetPixels(pixels);
            sheet.Apply();
            white = Sprite.Create(sheet, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        }
        if (circle == null) circle = CircleSprite();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { active = null; white = null; circle = null; ShuffleStock(); }   // Play without a domain reload keeps statics

    void Build()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvasGo = new GameObject("StoreCanvas");
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900);   // laid out in 1280x720 numbers, drawn at 80%: the HUD's and the stories' scale
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        Image bg = Panel(canvasGo.transform, "Background", Color.clear);
        Stretch(bg.rectTransform);
        bg.color = new Color(0.36f, 0.22f, 0.13f);

        Image bar = Panel(bg.transform, "TopBar", new Color(0.72f, 0.5f, 0.18f));
        bar.rectTransform.anchorMin = new Vector2(0f, 1f);
        bar.rectTransform.anchorMax = new Vector2(1f, 1f);
        bar.rectTransform.pivot = new Vector2(0.5f, 1f);
        bar.rectTransform.sizeDelta = new Vector2(0f, 78f);
        bar.rectTransform.anchoredPosition = Vector2.zero;

        walletText = Label(bar.transform, "Wallet", font, 28, TextAnchor.MiddleLeft, new Color(0.15f, 0.08f, 0.03f));
        walletText.rectTransform.anchorMin = new Vector2(0f, 0.42f);
        walletText.rectTransform.anchorMax = new Vector2(1f, 1f);
        walletText.rectTransform.offsetMin = new Vector2(28f, 0f);
        walletText.rectTransform.offsetMax = new Vector2(-180f, -6f);

        itemsText = Label(bar.transform, "Items", font, 18, TextAnchor.MiddleLeft, new Color(0.25f, 0.12f, 0.04f));
        itemsText.rectTransform.anchorMin = new Vector2(0f, 0f);
        itemsText.rectTransform.anchorMax = new Vector2(1f, 0.48f);
        itemsText.rectTransform.offsetMin = new Vector2(28f, 6f);
        itemsText.rectTransform.offsetMax = new Vector2(-180f, 0f);

        leaveButton = MakeButton(bar.transform, "Leave", font, out leaveLabel);
        leaveLabel.fontSize = 22;
        leaveLabel.color = new Color(0.25f, 0.12f, 0.04f);
        RectTransform leaveRect = leaveButton.GetComponent<RectTransform>();
        leaveRect.anchorMin = leaveRect.anchorMax = new Vector2(1f, 0.5f);
        leaveRect.pivot = new Vector2(1f, 0.5f);
        leaveRect.sizeDelta = new Vector2(140f, 52f);
        leaveRect.anchoredPosition = new Vector2(-24f, 0f);

        Image counterImage = Panel(bg.transform, "Counter", new Color(0.58f, 0.38f, 0.18f));
        counter = counterImage.rectTransform;
        counter.anchorMin = new Vector2(0.16f, 0.14f);
        counter.anchorMax = new Vector2(0.84f, 0.78f);
        counter.offsetMin = Vector2.zero;
        counter.offsetMax = Vector2.zero;

        Image lip = Panel(counter, "Lip", new Color(0.42f, 0.26f, 0.12f));
        lip.rectTransform.anchorMin = new Vector2(0f, 0f);
        lip.rectTransform.anchorMax = new Vector2(1f, 0.1f);
        lip.rectTransform.offsetMin = Vector2.zero;
        lip.rectTransform.offsetMax = Vector2.zero;

        Craft(bg, bar, counterImage, lip);

        tip = Panel(canvasGo.transform, "Tip", new Color(0.97f, 0.95f, 0.88f)).rectTransform;
        CraftTip();
        tip.sizeDelta = new Vector2(280f, 72f);
        tip.pivot = new Vector2(0.5f, 0f);
        tip.gameObject.SetActive(false);
        tipText = Label(tip, "TipText", font, 20, TextAnchor.MiddleCenter, new Color(0.12f, 0.08f, 0.05f));
        Stretch(tipText.rectTransform, 16f, 12f, 16f, 12f);
        tipText.horizontalOverflow = HorizontalWrapMode.Wrap;
        tipText.verticalOverflow = VerticalWrapMode.Overflow;
    }

    // Art (papercraft): the Evil Meowmeow's shop as a painted cabinet. Its upper plank lines up with
    // where items stand (icons 208 px up a 576 px counter), the floor is felt, the bar a stitched
    // felt strip with a gold paper Leave button. Layout rects are untouched; without art nothing changes.
    static bool crafted;
    static readonly Color Cream = new Color(0.97f, 0.9f, 0.74f);

    void CraftBaked()
    {
        Transform background = canvas.transform.Find("Background");
        Transform bar = background != null ? background.Find("TopBar") : null;
        Transform lip = counter != null ? counter.Find("Lip") : null;
        if (bar == null || lip == null || counter.Find("CabinetArt") != null) return;
        Craft(background.GetComponent<Image>(), bar.GetComponent<Image>(), counter.GetComponent<Image>(), lip.GetComponent<Image>());
        CraftTip();
    }

    void CraftTip()
    {
        Sprite paper = ArtLibrary.Get("UI/PaperCard");
        Image image = tip != null ? tip.GetComponent<Image>() : null;
        if (paper == null || image == null) return;
        image.sprite = paper;
        image.type = Image.Type.Sliced;
    }

    void Craft(Image bg, Image bar, Image counterImage, Image lip)
    {
        if (bg == null || bar == null || counterImage == null || lip == null) return;
        Sprite cabinet = ArtLibrary.Get("Store/Cabinet");
        Sprite floor = ArtLibrary.Get("MineFloor"), felt = ArtLibrary.Get("UI/FeltPanel"), paper = ArtLibrary.Get("UI/PaperCard");
        crafted = cabinet != null && floor != null && felt != null && paper != null;
        if (!crafted) return;

        bg.sprite = floor;
        bg.type = Image.Type.Tiled;
        bg.pixelsPerUnitMultiplier = 0.6f;
        bg.color = new Color(0.78f, 0.74f, 0.7f);

        bar.sprite = felt;
        bar.type = Image.Type.Sliced;
        bar.color = Color.white;
        walletText.color = Cream;
        itemsText.color = new Color(Cream.r, Cream.g, Cream.b, 0.75f);
        Image leave = leaveButton.GetComponent<Image>();
        if (leave != null) { leave.sprite = paper; leave.type = Image.Type.Sliced; leave.color = new Color(0.98f, 0.8f, 0.33f); }

        counterImage.color = Color.clear;
        lip.color = Color.clear;
        var art = new GameObject("CabinetArt", typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)art.transform;
        rect.SetParent(counterImage.transform, false);
        rect.SetAsFirstSibling();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(0f, -10f);
        rect.offsetMax = new Vector2(0f, -10f);
        Image look = art.GetComponent<Image>();
        look.sprite = cabinet;
        look.preserveAspect = true;
        look.raycastTarget = false;
    }

    void FillShelf()
    {
        StopShuffle();
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (items[i] != null) items[i].SetActive(false);
            Destroy(items[i]);
        }
        items.Clear();
        itemOffers.Clear();
        if (counter == null || CrossLevelStatus.Active == null) return;

        List<ShopOffer> offers = ShelfOffers();
        var forSale = new List<ShopOffer>();
        for (int i = 0; i < offers.Count; i++)
            if (!PurchasedThisShuffle(offers[i].id)) forSale.Add(offers[i]);

        const float spacing = 240f;
        for (int i = 0; i < forSale.Count; i++)
        {
            ShopOffer offer = forSale[i];
            string id = offer.id;
            int price = offer.price;
            string title = offer.title;
            string description = offer.description;
            string icon = offer.icon;
            Color color = offer.color;
            float x = (i - (forSale.Count - 1) * 0.5f) * spacing;
            const float y = 144f;
            GameObject item = AddItem(counter, x, y, id, title, price, description,
                () => Buy(id, price),
                parent => DrawIcon(parent, icon, color));
            items.Add(item);
            itemOffers.Add(offer);
        }
    }

    IEnumerator AnimateShuffle()
    {
        shuffling = true;
        HideTip();
        shufflePositions.Clear();
        var previewOffers = Offers();
        previewOffers.RemoveAll(offer => PurchasedThisShuffle(offer.id));
        var variants = new List<List<GameObject>>();
        foreach (GameObject item in items)
        {
            RectTransform rect = item.GetComponent<RectTransform>();
            shufflePositions.Add(rect.anchoredPosition);
            var group = item.GetComponent<CanvasGroup>();
            if (group == null) group = item.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            // Keep the actual purchased item intact; only the temporary art and price cycle.
            RectTransform icon = (RectTransform)item.transform.Find("Icon");
            icon.gameObject.SetActive(false);
            var preview = new GameObject("ShufflePreview", typeof(RectTransform));
            RectTransform previewRect = (RectTransform)preview.transform;
            previewRect.SetParent(item.transform, false);
            previewRect.anchorMin = icon.anchorMin;
            previewRect.anchorMax = icon.anchorMax;
            previewRect.pivot = icon.pivot;
            previewRect.sizeDelta = icon.sizeDelta;
            previewRect.anchoredPosition = icon.anchoredPosition;
            shufflePreviews.Add(preview);
            var slotVariants = new List<GameObject>();
            foreach (ShopOffer offer in previewOffers)
            {
                var art = new GameObject(offer.title, typeof(RectTransform));
                RectTransform artRect = (RectTransform)art.transform;
                artRect.SetParent(previewRect, false);
                Stretch(artRect);
                DrawIcon(artRect, offer.icon, offer.color);
                art.SetActive(false);
                slotVariants.Add(art);
            }
            variants.Add(slotVariants);
        }
        if (shuffleLabel == null)
        {
            shuffleLabel = Label(counter, "ShuffleLabel", walletText.font, 22,
                TextAnchor.MiddleCenter, new Color(0.97f, 0.88f, 0.65f));
            shuffleLabel.rectTransform.anchorMin = shuffleLabel.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            shuffleLabel.rectTransform.anchoredPosition = new Vector2(0f, 68f);
            shuffleLabel.rectTransform.sizeDelta = new Vector2(260f, 36f);
            shuffleLabel.text = "Shuffling...";
        }
        shuffleLabel.gameObject.SetActive(true);

        // Real-time animation also works while a tutorial holds the game clock.
        const float duration = 1.25f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            for (int i = 0; i < items.Count; i++)
            {
                RectTransform rect = items[i].GetComponent<RectTransform>();
                Vector2 target = shufflePositions[i];
                float tilt;
                if (t < 0.72f)
                {
                    // Three times as many goods changes, with the same shuffle timing and movement.
                    float progress = t / 0.72f;
                    int cycle = Mathf.FloorToInt(27f * (1f - (1f - progress) * (1f - progress)));
                    int choice = (cycle + i) % previewOffers.Count;
                    for (int v = 0; v < variants[i].Count; v++)
                        variants[i][v].SetActive(v == choice);
                    items[i].transform.Find("Price").GetComponent<Text>().text = "$" + previewOffers[choice].price;
                    // A full circuit returns every product to its actual sale slot.
                    float orbit = t / 0.72f * items.Count;
                    int pass = Mathf.FloorToInt(orbit);
                    float u = Mathf.SmoothStep(0f, 1f, orbit - pass);
                    int from = (i + pass) % items.Count;
                    int to = (i + pass + 1) % items.Count;
                    Vector2 at = Vector2.Lerp(shufflePositions[from], shufflePositions[to], u);
                    float arc = Mathf.Sin(u * Mathf.PI);
                    at.y += arc * (to < from ? -42f : 48f);
                    rect.anchoredPosition = at;
                    tilt = Mathf.Sin(u * Mathf.PI * 2f) * 9f;
                    rect.localScale = Vector3.one * (0.92f + arc * 0.06f);
                }
                else
                {
                    shufflePreviews[i].SetActive(false);
                    items[i].transform.Find("Icon").gameObject.SetActive(true);
                    items[i].transform.Find("Price").GetComponent<Text>().text = "$" + itemOffers[i].price;
                    float settle = (t - 0.72f) / 0.28f;
                    float bounce = Mathf.Sin(settle * Mathf.PI) * (1f - settle);
                    rect.anchoredPosition = target + Vector2.up * (bounce * 20f);
                    rect.localScale = Vector3.one * (1f + bounce * 0.1f);
                    tilt = 0f;
                }
                rect.localRotation = Quaternion.Euler(0f, 0f, tilt);
            }
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        shufflePending = false;
        shuffleRoutine = null;
        StopShuffle();
    }

    void StopShuffle()
    {
        if (shuffleRoutine != null) StopCoroutine(shuffleRoutine);
        shuffleRoutine = null;
        foreach (GameObject preview in shufflePreviews)
        {
            if (preview == null) continue;
            preview.SetActive(false);
            Destroy(preview);
        }
        shufflePreviews.Clear();
        for (int i = 0; i < items.Count && i < shufflePositions.Count; i++)
        {
            if (items[i] == null) continue;
            items[i].transform.Find("Icon").gameObject.SetActive(true);
            items[i].transform.Find("Price").GetComponent<Text>().text = "$" + itemOffers[i].price;
            RectTransform rect = items[i].GetComponent<RectTransform>();
            rect.anchoredPosition = shufflePositions[i];
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            CanvasGroup group = items[i].GetComponent<CanvasGroup>();
            if (group != null) { group.blocksRaycasts = true; group.interactable = true; }
        }
        shufflePositions.Clear();
        if (shuffleLabel != null) shuffleLabel.gameObject.SetActive(false);
        shuffling = false;
    }

    static List<ShopOffer> Offers()
    {
        return new List<ShopOffer>
        {
            new ShopOffer
            {
                id = "swift", title = "Swift", price = 200,
                description = "Double speed next level.",
                icon = "bottle", color = new Color(0.45f, 0.72f, 0.86f)
            },
            new ShopOffer
            {
                id = "ration", title = "Ration", price = 250,
                description = "No digestion next level.",
                icon = "ration", color = new Color(0.86f, 0.74f, 0.5f)
            },
            new ShopOffer
            {
                id = "shield", title = "Shield", price = 350,
                description = "No life lost for 5s.",
                icon = "shield", color = new Color(0.45f, 0.78f, 0.95f)
            },
            new ShopOffer
            {
                id = "brace", title = "Life", price = 400,
                description = "+1 life next level (orange bonus).",
                icon = "life", color = new Color(0.9f, 0.15f, 0.15f)
            },
            new ShopOffer
            {
                id = "time_machine", title = "Time Machine", price = 500,
                description = "Stand still: slow time next round.",
                icon = "clock"
            }
        };
    }

    // Pays for one and arms it for the next playable level. One purchase per item per shuffle.
    // Swift: double speed. Ration: no digest. Shield: 5s invuln. Brace: +1 orange bonus life.
    static bool Buy(string id, int price)
    {
        CrossLevelStatus player = CrossLevelStatus.Active;
        if (player == null || PurchasedThisShuffle(id) || player.getWallet() < price) return false;
        player.addWallet(-price);
        GrantItem(player, id);
        purchasedThisShuffle.Add(id);
        PlayAnalytics.Purchase(id);
        return true;
    }

    // Console and purchases share the same stock/activation rules, including arming outside rounds.
    static void GrantItem(CrossLevelStatus player, string id)
    {
        PlayerSkill skill = player.addSkill(id);
        skill.stock++;
        skill.unlocked = true;
        bool used = player.useSkill(id);
        // The loader normally copies an armed Life into the next round's HP. A grant during
        // play needs the same HP immediately, since this round has already been configured.
        GameManager round = GameManager.Instance;
        if (used && id == "brace" && CrossLevelStatus.StoreRoundActive)
        {
            round.AddLife();
            round.SetBonusLives(round.BonusLives + 1);
        }
        if (id == "time_machine" && skill.active && CrossLevelStatus.StoreRoundActive)
            TimeMovesWithPlayer.ApplyStoreEffect(FindFirstObjectByType<PlayerCharacter>(), player);
    }

    public static List<string> ConsoleItemNames()
    {
        var names = new List<string>();
        foreach (ShopOffer offer in Offers()) names.Add(offer.title.Replace(' ', '_'));
        return names;
    }

    public static void RefreshWallet()
    {
        if (active != null) active.Refresh();
    }

    public static string GrantConsoleItem(string name)
    {
        string key = ConsoleItemKey(name);
        foreach (ShopOffer offer in Offers())
        {
            if (key != ConsoleItemKey(offer.title) && key != ConsoleItemKey(offer.id)) continue;
            CrossLevelStatus record = CrossLevelStatus.Ensure();
            int beforeStock = record.getSkill(offer.id)?.stock ?? 0;
            GrantItem(record, offer.id);
            PlayerSkill item = record.getSkill(offer.id);
            if (active != null) active.Refresh();
            string state = item.stock > beforeStock ? "Added to spare stock."
                : item.armed ? "Ready for your next level."
                : "Active in this level.";
            PlayerCharacter target = FindFirstObjectByType<PlayerCharacter>();
            TimeMovesWithPlayer time = target != null ? target.GetComponent<TimeMovesWithPlayer>() : null;
            Debug.Log($"[Console goods] {offer.id}: active={item.active}, armed={item.armed}, stock={item.stock}, seconds={item.secondsLeft:F2}, round={CrossLevelStatus.StoreRoundActive}, tutorial={LevelLoader.Tutorial}, map={MapHub.Active != null}, player={(target != null ? target.name : "none")}, timeEffect={(time != null ? (time.enabled ? "enabled" : "disabled") : "absent")}, scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            return "Granted " + offer.title + ". " + state;
        }
        return "Usage: good <item_name> (e.g. good Time Machine). Items: " + string.Join(", ", ConsoleItemNames());
    }

    static string ConsoleItemKey(string name)
    {
        var key = new System.Text.StringBuilder();
        foreach (char character in (name ?? "").Trim().Trim('"', '\''))
            if (!char.IsWhiteSpace(character) && character != '_' && character != '-')
                key.Append(char.ToLowerInvariant(character));
        return key.ToString();
    }

    void DrawIcon(RectTransform parent, string icon, Color color)
    {
        Sprite painted = ArtLibrary.Get("Icons/" + icon);   // art: painted felt/paper item
        if (painted != null)
        {
            var go = new GameObject("Art", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Image image = go.GetComponent<Image>();
            image.sprite = painted;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return;
        }
        if (icon == "bottle") Bottle(parent);
        else if (icon == "ration") Ration(parent);
        else if (icon == "shield") Shield(parent);
        else if (icon == "life") Life(parent);
        else if (icon == "clock") TimeMachine(parent);
        else Piece(parent, 0f, 0f, 70f, 70f, color);
    }

    GameObject AddItem(Transform shelf, float x, float y, string id, string itemName, int price, string description,
        Func<bool> buy, Action<RectTransform> icon)
    {
        Font font = walletText.font;
        var root = new GameObject(itemName);
        root.transform.SetParent(shelf, false);
        RectTransform rect = root.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(170f, 156f);
        rect.anchoredPosition = new Vector2(x, y);

        Image hit = root.AddComponent<Image>();
        hit.sprite = white;
        hit.color = new Color(0f, 0f, 0f, 0f);
        Button button = root.AddComponent<Button>();
        button.targetGraphic = hit;
        button.onClick.AddListener(() =>
        {
            if (shuffling) return;
            CrossLevelStatus player = CrossLevelStatus.Active;
            if (player == null) return;
            if (player.getWallet() < price)
            {
                ShowTip(rect, itemName, "Not enough money!", id);
                Refresh();
                return;
            }
            if (!buy()) return;
            HideTip();
            FillShelf();
            Refresh();
        });

        var hover = root.AddComponent<StoreItemHover>();
        hover.Bind(rect, () =>
        {
            ShowTip(rect, itemName, description, id);
            PlayAnalytics.StoreHover(id);
        }, HideTip);

        var iconRoot = new GameObject("Icon");
        iconRoot.transform.SetParent(root.transform, false);
        RectTransform iconRect = iconRoot.AddComponent<RectTransform>();
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0f);
        iconRect.pivot = new Vector2(0.5f, 0f);
        iconRect.sizeDelta = new Vector2(90f, 84f);
        iconRect.anchoredPosition = new Vector2(0f, 64f);
        icon(iconRect);

        Text priceText = Label(root.transform, "Price", font, 26, TextAnchor.MiddleCenter, new Color(0.12f, 0.06f, 0.02f));
        priceText.rectTransform.anchorMin = priceText.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        priceText.rectTransform.pivot = new Vector2(0.5f, 0f);
        priceText.rectTransform.sizeDelta = new Vector2(170f, 36f);
        priceText.rectTransform.anchoredPosition = new Vector2(0f, 8f);
        priceText.text = "$" + price;
        if (crafted)   // art: cream lettering with a dark cut edge, readable on the plum wallpaper
        {
            priceText.color = Cream;
            priceText.fontStyle = FontStyle.Bold;
            Outline edge = priceText.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0.12f, 0.05f, 0.02f, 0.9f);
            edge.effectDistance = new Vector2(2f, -2f);
        }

        Text ownedText = Label(root.transform, "Owned", font, 18, TextAnchor.MiddleCenter, new Color(0.25f, 0.14f, 0.06f));
        ownedText.rectTransform.anchorMin = ownedText.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        ownedText.rectTransform.sizeDelta = new Vector2(170f, 28f);
        ownedText.rectTransform.anchoredPosition = new Vector2(0f, 44f);
        ownedText.gameObject.AddComponent<OwnedLabel>().Bind(id);
        if (crafted)
        {
            ownedText.color = Cream;
            Outline edge = ownedText.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0.12f, 0.05f, 0.02f, 0.8f);
        }
        return root;
    }

    void Life(RectTransform parent)
    {
        Image disc = Piece(parent, 0f, 0f, 74f, 74f, new Color(0.9f, 0.15f, 0.15f));
        disc.sprite = circle;
        Piece(parent, 0f, 17f, 12f, 40f, Color.white);
        Piece(parent, 0f, 31f, 40f, 12f, Color.white);
    }

    void Bottle(RectTransform parent)
    {
        Piece(parent, 0f, 0f, 36f, 56f, new Color(0.45f, 0.72f, 0.86f));
        Piece(parent, 0f, 56f, 16f, 16f, new Color(0.45f, 0.72f, 0.86f));
        Piece(parent, 0f, 72f, 20f, 8f, new Color(0.55f, 0.36f, 0.18f));
    }

    void Ration(RectTransform parent)
    {
        Piece(parent, 0f, 0f, 68f, 50f, new Color(0.86f, 0.74f, 0.5f));
        Piece(parent, 0f, 22f, 52f, 12f, new Color(0.35f, 0.62f, 0.32f));
    }

    void Shield(RectTransform parent)
    {
        Image disc = Piece(parent, 0f, 0f, 74f, 74f, new Color(0.45f, 0.78f, 0.95f));
        disc.sprite = circle;
        Image inner = Piece(disc.rectTransform, 0f, 19f, 36f, 36f, new Color(0.85f, 0.95f, 1f));
        inner.sprite = circle;
    }

    void TimeMachine(RectTransform parent)
    {
        Color brass = new Color(0.86f, 0.64f, 0.28f);
        Color ink = new Color(0.25f, 0.14f, 0.06f);
        Piece(parent, 0f, 72f, 16f, 8f, brass);
        Piece(parent, 0f, 80f, 26f, 6f, ink);
        Image rim = Piece(parent, 0f, 0f, 72f, 72f, brass);
        rim.sprite = circle;
        Image face = Piece(parent, 0f, 7f, 58f, 58f, new Color(0.97f, 0.95f, 0.88f));
        face.sprite = circle;
        Piece(parent, 0f, 35f, 5f, 22f, ink);
        Piece(parent, 0f, 33f, 22f, 5f, ink).rectTransform.pivot = new Vector2(0f, 0f);
        Image pin = Piece(parent, 0f, 32f, 8f, 8f, ink);
        pin.sprite = circle;
    }

    // Bottom of the graphic sits on `bottom`, so a row of products shares one line.
    Image Piece(RectTransform parent, float x, float bottom, float width, float height, Color color)
    {
        Image image = Panel(parent, "Part", color);
        image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        image.rectTransform.pivot = new Vector2(0.5f, 0f);
        image.rectTransform.sizeDelta = new Vector2(width, height);
        image.rectTransform.anchoredPosition = new Vector2(x, bottom);
        return image;
    }

    void Refresh()
    {
        CrossLevelStatus status = CrossLevelStatus.Active;
        if (status == null || walletText == null) return;
        walletText.text = status.WalletLabel();
        if (itemsText != null) itemsText.text = OwnedLine(status);
        foreach (OwnedLabel label in canvas.GetComponentsInChildren<OwnedLabel>(true))
            label.Refresh();
    }

    // Ownership survives between rounds; purchase limits only last until the next stock shuffle.
    static bool PurchasedThisShuffle(string id) => purchasedThisShuffle.Contains(id);

    // Bought items still count after the effect is armed, because that spends the stock.
    static int Held(PlayerSkill skill)
    {
        if (skill == null || !IsStoreItem(skill.id)) return 0;
        int count = skill.stock;
        if (skill.armed || skill.active || skill.secondsLeft > 0f) count++;
        return count;
    }

    static bool IsStoreItem(string id)
    {
        return CrossLevelStatus.IsStoreEffect(id);
    }

    static string OwnedLine(CrossLevelStatus status)
    {
        var parts = new List<string>();
        foreach (PlayerSkill skill in status.getSkills())
        {
            int count = Held(skill);
            if (count <= 0) continue;
            string name = skill.id == "brace" ? "Life"
                : skill.id == "time_machine" ? "Time Machine" : char.ToUpperInvariant(skill.id[0]) + skill.id.Substring(1);
            parts.Add(name + " x" + count);
        }
        return string.Join("   ", parts);
    }

    void ShowTip(RectTransform item, string title, string body, string id = null)
    {
        tip.gameObject.SetActive(true);
        bool demo = !string.IsNullOrEmpty(id);
        tip.sizeDelta = new Vector2(280f, 88f);
        // The preview must never steal the pointer from the shelf item beneath it.
        CanvasGroup group = tip.GetComponent<CanvasGroup>();
        if (group == null) group = tip.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        Stretch(tipText.rectTransform, 12f, 8f, 12f, 8f);
        if (demo)
        {
            if (itemDemo == null)
            {
                Image border = Panel(canvas.transform, "DemoTip", new Color(0.15f, 0.09f, 0.05f));
                demoTip = border.rectTransform;
                demoTip.pivot = new Vector2(0.5f, 0.5f);
                demoTip.sizeDelta = new Vector2(256f, 256f);
                border.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
                Image background = Panel(border.transform, "ScreenBackground", Color.black);
                background.raycastTarget = false;
                Stretch(background.rectTransform, 3f, 3f, 3f, 3f);
                var screen = new GameObject("GameplayDemo", typeof(RectTransform));
                screen.transform.SetParent(border.transform, false);
                Stretch((RectTransform)screen.transform, 3f, 3f, 3f, 3f);
                itemDemo = screen.AddComponent<StoreItemDemo>();
            }
            demoTip.gameObject.SetActive(true);
            itemDemo.Play(id, demoTip);
        }
        else if (demoTip != null) demoTip.gameObject.SetActive(false);
        tipText.supportRichText = true;
        tipText.text = "<b>" + title + "</b>\n" + body;

        Vector3[] corners = new Vector3[4];
        item.GetWorldCorners(corners);
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        for (int i = 0; i < corners.Length; i++) corners[i] = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[i]);
        float bottom = corners[0].y;
        float top = corners[1].y;
        float centerX = (corners[0].x + corners[2].x) * 0.5f;
        const float gap = 12f;
        PlaceHoverPanel(tip, centerX, bottom - gap - tip.rect.height * canvas.scaleFactor, uiCamera);
        if (demo) PlaceHoverPanel(demoTip, centerX, top + gap, uiCamera);
    }

    void PlaceHoverPanel(RectTransform panel, float centerX, float y, Camera uiCamera)
    {
        float tipHeight = panel.rect.height * canvas.scaleFactor;
        float tipWidth = panel.rect.width * canvas.scaleFactor;
        // Inside the 16:9 frame (ScreenFrame), not the bars around it; the canvas is in screen pixels
        Rect frame = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? ScreenFrame.Pixels : canvas.pixelRect;
        centerX = Mathf.Clamp(centerX, frame.xMin + tipWidth * 0.5f + 12f, frame.xMax - tipWidth * 0.5f - 12f);
        y = Mathf.Clamp(y, frame.yMin + 12f, frame.yMax - tipHeight - 12f);
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)canvas.transform,
            new Vector2(centerX, y + tipHeight * panel.pivot.y), uiCamera, out Vector3 position)) panel.position = position;
    }

    void HideTip()
    {
        if (tip != null) tip.gameObject.SetActive(false);
        if (demoTip != null) demoTip.gameObject.SetActive(false);
    }

    static Image Panel(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>();
        image.sprite = white;
        image.color = color;
        image.rectTransform.sizeDelta = new Vector2(100f, 100f);
        return image;
    }

    static Text Label(Transform parent, string name, Font font, int size, TextAnchor anchor, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Text text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = anchor;
        text.color = color;
        text.raycastTarget = false;
        text.rectTransform.sizeDelta = new Vector2(200f, 40f);
        return text;
    }

    static Button MakeButton(Transform parent, string name, Font font, out Text label)
    {
        Image image = Panel(parent, name, new Color(0.93f, 0.78f, 0.42f));
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        label = Label(image.transform, "Label", font, 22, TextAnchor.MiddleCenter, Color.black);
        Stretch(label.rectTransform);
        return button;
    }

    static void Stretch(RectTransform rect, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    static Sprite CircleSprite()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        float radius = size * 0.5f - 0.5f;
        var center = new Vector2(size * 0.5f, size * 0.5f);
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                pixels[y * size + x] = d <= radius ? Color.white : Color.clear;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    class OwnedLabel : MonoBehaviour
    {
        Text text;
        string id;

        public void Bind(string skillId)
        {
            text = GetComponent<Text>();
            id = skillId;
            Refresh();
        }

        public void Refresh()
        {
            if (text == null || CrossLevelStatus.Active == null) return;
            int count = Held(CrossLevelStatus.Active.getSkill(id));
            text.text = count > 0 ? "x" + count : "";
        }
    }
}

public class ShopOffer
{
    public string id;
    public string title;
    public int price;
    public string description;
    public string icon;
    public Color color = Color.white;
}

public class StoreItemHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    RectTransform rect;
    Action show;
    Action hide;

    public void Bind(RectTransform item, Action onShow, Action onHide)
    {
        rect = item;
        show = onShow;
        hide = onHide;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (show != null) show();
        if (rect != null) rect.localScale = Vector3.one * 1.06f;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (hide != null) hide();
        if (rect != null) rect.localScale = Vector3.one;
    }
}
