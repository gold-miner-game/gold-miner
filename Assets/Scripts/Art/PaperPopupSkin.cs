using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Art: the papercraft look for the HUD's Pause card and end card (HUDController builds them at runtime,
// Figma "Pause (U2)" / "End (U1)"). Called by PaperPopups the frame a card appears, so it is never seen
// flat. Display only: every button keeps its click, key and place; the bar keeps its share and target.
//   buttons      the main menu's stitched felt buttons, gold lettering (red for Quit), the default one
//                with the menu's gold paper tab; key badges become paper key caps (ArtLibrary.PaperCap)
//   divider      a row of stitches
//   YOU/TARGET   a paper-strip track with a pig-pink paper fill, and the HUD's red pole and felt pennant
//                at the target
public static class PaperPopupSkin
{
    static readonly Color Gold = new Color(0.98f, 0.8f, 0.32f);          // PaperType's gold leaf
    static readonly Color Ink = new Color(0.16f, 0.12f, 0.1f);           // key cap letters
    static readonly Color Stitch = new Color(0.84f, 0.78f, 0.67f, 0.85f);
    static readonly Color TrackPaper = new Color(0.47f, 0.41f, 0.35f);
    static readonly Color FlagRed = new Color(1f, 0.28f, 0.32f);         // the HUD's TARGET flag
    const float TabWidth = 12f;

    // ---- cards ---------------------------------------------------------------------------------------

    public static void Card(RectTransform card)
    {
        if (card == null) return;
        Sprite felt = ArtLibrary.Get("UI/FeltPanel");
        if (felt == null) return;
        foreach (Button button in card.GetComponentsInChildren<Button>(true)) FeltButton(button, felt);
        Transform divider = card.Find("Divider");
        if (divider != null) Stitches((RectTransform)divider);
        Transform track = card.Find("Track");
        if (track != null) Bar((RectTransform)track, card.Find("TargetMarker") as RectTransform);
    }

    static void FeltButton(Button button, Sprite felt)
    {
        Image image = button.targetGraphic as Image;
        if (image == null) return;
        Color fill = button.colors.normalColor;
        bool primary = Mathf.Max(fill.r, fill.g, fill.b) > 0.6f;   // the skill-blue default (Resume, Retry, Back to Map)
        TMP_Text label = button.transform.Find("Label")?.GetComponent<TMP_Text>();
        bool danger = label != null && label.color.r > 0.9f && label.color.g < 0.6f;   // red ink: Quit

        image.sprite = felt;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = Mathf.Clamp(64f / Mathf.Max(8f, image.rectTransform.rect.height), 1f, 4f);
        image.color = Color.white;
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.9f, 0.9f, 0.9f);
        colors.highlightedColor = Color.white;
        colors.selectedColor = colors.normalColor;
        colors.pressedColor = new Color(0.72f, 0.72f, 0.72f);
        button.colors = colors;
        if (label != null && !danger) label.color = Gold;

        if (primary)
        {
            var tab = new GameObject("PaperTab", typeof(RectTransform), typeof(Image));
            RectTransform t = (RectTransform)tab.transform;
            t.SetParent(button.transform, false);
            t.anchorMin = new Vector2(0f, 0f);
            t.anchorMax = new Vector2(0f, 1f);
            t.pivot = new Vector2(0f, 0.5f);
            t.sizeDelta = new Vector2(TabWidth, -10f);
            t.anchoredPosition = new Vector2(-TabWidth * 0.5f, 0f);
            Image tabImage = tab.GetComponent<Image>();
            ArtLibrary.PaperCap(tabImage, 6f);
            tabImage.color = Gold;
            tabImage.raycastTarget = false;
        }

        Transform key = button.transform.Find("Key");
        if (key != null)
        {
            Image cap = key.GetComponent<Image>();
            if (ArtLibrary.PaperCap(cap, 4f)) cap.color = ArtLibrary.Cream;
            TMP_Text letter = key.Find("KeyText")?.GetComponent<TMP_Text>();
            if (letter != null) letter.color = Ink;
        }
        if (button.GetComponent<PaperPress>() == null) button.gameObject.AddComponent<PaperPress>();
    }

    // A line of stitches in place of the 1-pixel divider
    static void Stitches(RectTransform divider)
    {
        Image line = divider.GetComponent<Image>();
        if (line == null) return;
        line.color = Color.clear;
        float width = divider.rect.width;
        for (float x = 0f; x + 9f <= width; x += 16f)
        {
            var dash = new GameObject("Stitch", typeof(RectTransform), typeof(Image));
            RectTransform d = (RectTransform)dash.transform;
            d.SetParent(divider, false);
            d.anchorMin = d.anchorMax = new Vector2(0f, 0.5f);
            d.pivot = new Vector2(0f, 0.5f);
            d.sizeDelta = new Vector2(9f, 2f);
            d.anchoredPosition = new Vector2(x, 0f);
            Image image = dash.GetComponent<Image>();
            image.color = Stitch;
            image.raycastTarget = false;
        }
    }

    // YOU vs TARGET: paper strips and the HUD's flag
    static void Bar(RectTransform track, RectTransform marker)
    {
        Image back = track.GetComponent<Image>();
        if (back == null || !ArtLibrary.PaperCap(back, 6f)) return;
        back.color = TrackPaper;
        Transform fill = track.Find("Fill");
        Image strip = fill != null ? fill.GetComponent<Image>() : null;
        if (strip != null)
        {
            Color pink = strip.color;
            ArtLibrary.PaperCap(strip, 6f);
            strip.color = pink;
        }
        if (marker == null) return;
        Image pole = marker.GetComponent<Image>();
        if (pole != null) pole.color = FlagRed;
        Sprite pennant = ArtLibrary.Get("Fx/Pennant");
        if (pennant == null) return;
        var flag = new GameObject("Flag", typeof(RectTransform), typeof(Image));
        RectTransform f = (RectTransform)flag.transform;
        f.SetParent(marker, false);
        f.anchorMin = f.anchorMax = new Vector2(0.5f, 1f);
        f.pivot = new Vector2(0f, 1f);
        f.sizeDelta = new Vector2(22f, 18f);
        f.anchoredPosition = new Vector2(1f, 0f);
        Image image = flag.GetComponent<Image>();
        image.sprite = pennant;
        image.color = FlagRed;
        image.raycastTarget = false;
    }
}
