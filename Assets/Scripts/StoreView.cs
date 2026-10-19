using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Shop counter. Items sit on the shelf; hovering one shows its description.
// No shopkeeper. Built from Unity UI shapes, not imported art.
public class StoreView : MonoBehaviour
{
    static StoreView active;
    static Sprite white;
    static Sprite circle;

    Canvas canvas;
    Text walletText;
    Text leaveLabel;
    RectTransform counter;
    RectTransform tip;
    Text tipText;
    bool nextLevel;
    Action onLeave;
    readonly List<GameObject> items = new List<GameObject>();

    public static void Show(bool asNextLevel, Action leave)
    {
        StoreView view = Ensure();
        view.nextLevel = asNextLevel;
        view.onLeave = leave;
        view.canvas.gameObject.SetActive(true);
        view.leaveLabel.text = asNextLevel ? "Next" : "Close";
        view.HideTip();
        view.FillShelf();
        view.Refresh();
    }

    public static void Hide()
    {
        if (active != null && active.canvas != null)
            active.canvas.gameObject.SetActive(false);
    }

    static StoreView Ensure()
    {
        if (active != null) return active;
        var go = new GameObject("StoreView");
        DontDestroyOnLoad(go);
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
        DontDestroyOnLoad(gameObject);
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var events = new GameObject("StoreEventSystem");
            events.transform.SetParent(transform, false);
            events.AddComponent<EventSystem>();
            events.AddComponent<StandaloneInputModule>();
        }
        Build();
        canvas.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (active == this) active = null;
    }

    void Build()
    {
        var sheet = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var pixels = new Color[16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        sheet.SetPixels(pixels);
        sheet.Apply();
        white = Sprite.Create(sheet, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        circle = CircleSprite();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvasGo = new GameObject("StoreCanvas");
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
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
        Stretch(walletText.rectTransform, 28f, 8f, 360f, 8f);

        Button leave = MakeButton(bar.transform, "Leave", font, out leaveLabel);
        leaveLabel.fontSize = 22;
        leaveLabel.color = new Color(0.25f, 0.12f, 0.04f);
        RectTransform leaveRect = leave.GetComponent<RectTransform>();
        leaveRect.anchorMin = leaveRect.anchorMax = new Vector2(1f, 0.5f);
        leaveRect.pivot = new Vector2(1f, 0.5f);
        leaveRect.sizeDelta = new Vector2(140f, 52f);
        leaveRect.anchoredPosition = new Vector2(-24f, 0f);
        leave.onClick.AddListener(() => { if (onLeave != null) onLeave(); });

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

        tip = Panel(canvasGo.transform, "Tip", new Color(0.97f, 0.95f, 0.88f)).rectTransform;
        tip.sizeDelta = new Vector2(280f, 64f);
        tip.pivot = new Vector2(0.5f, 0f);
        tip.gameObject.SetActive(false);
        tipText = Label(tip, "TipText", font, 20, TextAnchor.MiddleCenter, new Color(0.12f, 0.08f, 0.05f));
        Stretch(tipText.rectTransform, 16f, 12f, 16f, 12f);
        tipText.horizontalOverflow = HorizontalWrapMode.Wrap;
        tipText.verticalOverflow = VerticalWrapMode.Overflow;
    }

    void FillShelf()
    {
        for (int i = items.Count - 1; i >= 0; i--)
            Destroy(items[i]);
        items.Clear();
        if (counter == null || CrossLevelStatus.Active == null) return;

        List<ShopOffer> offers = Offers();
        const float col = 210f;
        const float row = 200f;
        for (int i = 0; i < offers.Count; i++)
        {
            ShopOffer offer = offers[i];
            string id = offer.id;
            int price = offer.price;
            string title = offer.title;
            string description = offer.description;
            string icon = offer.icon;
            Color color = offer.color;
            int column = i % 2;
            int line = i / 2;
            float x = column == 0 ? -col : col;
            float y = -36f - line * row;
            GameObject item = AddItem(counter, x, y, title, price, description,
                () => Stock(id),
                () => Buy(id, price),
                parent => DrawIcon(parent, icon, color));
            items.Add(item);
        }
    }

    static List<ShopOffer> Offers()
    {
        return new List<ShopOffer>
        {
            new ShopOffer
            {
                id = "swift", title = "Swift", price = 200,
                description = "Double speed this level.",
                icon = "bottle", color = new Color(0.45f, 0.72f, 0.86f)
            },
            new ShopOffer
            {
                id = "ration", title = "Ration", price = 250,
                description = "No digestion this level.",
                icon = "ration", color = new Color(0.86f, 0.74f, 0.5f)
            },
            new ShopOffer
            {
                id = "shield", title = "Shield", price = 350,
                description = "No life lost for 60s.",
                icon = "shield", color = new Color(0.45f, 0.78f, 0.95f)
            },
            new ShopOffer
            {
                id = "brace", title = "Brace", price = 400,
                description = "Blocking for this level.",
                icon = "brace", color = new Color(0.25f, 0.45f, 0.85f)
            }
        };
    }

    static int Stock(string id)
    {
        CrossLevelStatus player = CrossLevelStatus.Active;
        if (player == null) return 0;
        PlayerSkill skill = player.getSkill(id);
        return skill == null ? 0 : skill.stock;
    }

    static bool Buy(string id, int price)
    {
        CrossLevelStatus player = CrossLevelStatus.Active;
        if (player == null || player.getWallet() < price) return false;
        player.setWallet(player.getWallet() - price);
        PlayerSkill skill = player.addSkill(id);
        skill.stock++;
        skill.unlocked = true;
        return true;
    }

    void DrawIcon(RectTransform parent, string icon, Color color)
    {
        if (icon == "bottle") Bottle(parent);
        else if (icon == "ration") Ration(parent);
        else if (icon == "shield") Shield(parent);
        else if (icon == "brace") Brace(parent);
        else Block(parent, new Vector2(0f, 8f), new Vector2(70f, 70f), color);
    }

    GameObject AddItem(Transform shelf, float x, float y, string itemName, int price, string description,
        Func<int> owned, Func<bool> buy, Action<RectTransform> icon)
    {
        Font font = walletText.font;
        var root = new GameObject(itemName);
        root.transform.SetParent(shelf, false);
        RectTransform rect = root.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(180f, 168f);
        rect.anchoredPosition = new Vector2(x, y);

        Image hit = root.AddComponent<Image>();
        hit.sprite = white;
        hit.color = new Color(0f, 0f, 0f, 0f);
        Button button = root.AddComponent<Button>();
        button.targetGraphic = hit;
        button.onClick.AddListener(() =>
        {
            if (CrossLevelStatus.Active == null) return;
            if (!buy()) ShowTip(rect, itemName, "Not enough.");
            Refresh();
        });

        var hover = root.AddComponent<StoreItemHover>();
        hover.Bind(rect, () => ShowTip(rect, itemName, description), HideTip);

        var iconRoot = new GameObject("Icon");
        iconRoot.transform.SetParent(root.transform, false);
        RectTransform iconRect = iconRoot.AddComponent<RectTransform>();
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.62f);
        iconRect.sizeDelta = new Vector2(90f, 110f);
        iconRect.anchoredPosition = Vector2.zero;
        icon(iconRect);

        Text priceText = Label(root.transform, "Price", font, 26, TextAnchor.MiddleCenter, new Color(0.12f, 0.06f, 0.02f));
        priceText.rectTransform.anchorMin = priceText.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        priceText.rectTransform.pivot = new Vector2(0.5f, 0f);
        priceText.rectTransform.sizeDelta = new Vector2(170f, 36f);
        priceText.rectTransform.anchoredPosition = new Vector2(0f, 8f);
        priceText.text = "$" + price;

        Text ownedText = Label(root.transform, "Owned", font, 18, TextAnchor.MiddleCenter, new Color(0.25f, 0.14f, 0.06f));
        ownedText.rectTransform.anchorMin = ownedText.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        ownedText.rectTransform.sizeDelta = new Vector2(170f, 28f);
        ownedText.rectTransform.anchoredPosition = new Vector2(0f, 44f);
        ownedText.gameObject.AddComponent<OwnedLabel>().Bind(owned);
        return root;
    }

    void Brace(RectTransform parent)
    {
        Block(parent, new Vector2(0f, -8f), new Vector2(78f, 18f), new Color(0.25f, 0.45f, 0.85f));
        Block(parent, new Vector2(0f, 16f), new Vector2(78f, 18f), new Color(0.25f, 0.45f, 0.85f));
        Block(parent, new Vector2(0f, 40f), new Vector2(78f, 18f), new Color(0.55f, 0.75f, 1f));
    }

    void Bottle(RectTransform parent)
    {
        Block(parent, new Vector2(0f, -8f), new Vector2(40f, 72f), new Color(0.45f, 0.72f, 0.86f));
        Block(parent, new Vector2(0f, 42f), new Vector2(18f, 22f), new Color(0.45f, 0.72f, 0.86f));
        Block(parent, new Vector2(0f, 58f), new Vector2(22f, 10f), new Color(0.55f, 0.36f, 0.18f));
    }

    void Ration(RectTransform parent)
    {
        Block(parent, new Vector2(0f, -6f), new Vector2(70f, 58f), new Color(0.86f, 0.74f, 0.5f));
        Block(parent, new Vector2(0f, 8f), new Vector2(54f, 14f), new Color(0.35f, 0.62f, 0.32f));
    }

    void Shield(RectTransform parent)
    {
        var disc = new GameObject("Disc");
        disc.transform.SetParent(parent, false);
        RectTransform rect = disc.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.45f);
        rect.sizeDelta = new Vector2(78f, 78f);
        Image image = disc.AddComponent<Image>();
        image.sprite = circle;
        image.color = new Color(0.45f, 0.78f, 0.95f);
        var inner = new GameObject("Inner");
        inner.transform.SetParent(disc.transform, false);
        RectTransform innerRect = inner.AddComponent<RectTransform>();
        innerRect.anchorMin = innerRect.anchorMax = new Vector2(0.5f, 0.5f);
        innerRect.sizeDelta = new Vector2(36f, 36f);
        Image innerImage = inner.AddComponent<Image>();
        innerImage.sprite = circle;
        innerImage.color = new Color(0.85f, 0.95f, 1f);
    }

    void Block(RectTransform parent, Vector2 pos, Vector2 size, Color color)
    {
        Image image = Panel(parent, "Part", color);
        image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        image.rectTransform.sizeDelta = size;
        image.rectTransform.anchoredPosition = pos;
    }

    void Refresh()
    {
        CrossLevelStatus status = CrossLevelStatus.Active;
        if (status == null || walletText == null) return;
        walletText.text = "$" + status.getWallet().ToString("N0");
        foreach (OwnedLabel label in canvas.GetComponentsInChildren<OwnedLabel>(true))
            label.Refresh();
    }

    void ShowTip(RectTransform item, string title, string body)
    {
        tip.gameObject.SetActive(true);
        tipText.text = title + "  " + body;
        Vector3 pos = item.position + new Vector3(0f, 150f, 0f);
        pos.x = Mathf.Clamp(pos.x, 200f, Screen.width - 200f);
        pos.y = Mathf.Clamp(pos.y, 80f, Screen.height - 140f);
        tip.position = pos;
    }

    void HideTip()
    {
        if (tip != null) tip.gameObject.SetActive(false);
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
        Func<int> owned;

        public void Bind(Func<int> count)
        {
            text = GetComponent<Text>();
            owned = count;
            Refresh();
        }

        public void Refresh()
        {
            if (text == null || owned == null || CrossLevelStatus.Active == null) return;
            int count = owned();
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
