using UnityEngine;

// Owner: Doeun (UI)
// The screen split shared by the HUD and the camera: a bar across the top, a sidebar on the right,
// and the arena in what is left (bottom left). Fractions are of the screen; Gap is in canvas units
// (the HUD canvas scales a 1920x1080 reference). The screen edge, the top bar, the sidebar and the
// arena are all Gap apart
// HUD Beta has no sidebar and a taller top bar; its HUDController sets TopBarOnly on Awake
public static class HUDLayout
{
    public const float Gap = 8f;

    // HUD Beta (Figma "HUD - v3 topbar"): a 92 px row of panels, Gap above and below (108 of 1080)
    public static bool TopBarOnly;
    public static float TopBarHeight => TopBarOnly ? 0.1f : 0.08f;
    public static float SidebarWidth => TopBarOnly ? 0f : 0.143f;

    // Maze world map: hide the battle top bar so the arena can use the full height.
    public static bool HideTopBar;

    // Canvas units to screen pixels, as the HUD's CanvasScaler does it (1920x1080, match 0.5)
    public static float CanvasScale => Mathf.Sqrt(Screen.width / 1920f * (Screen.height / 1080f));

    // In viewport space (0..1, origin bottom left), as Camera.rect wants it: Gap in from the left
    // and bottom edges; the panels' own Gap insets make the gaps to the top bar and the sidebar
    public static Rect GameplayViewport
    {
        get
        {
            float gapX = Gap * CanvasScale / Mathf.Max(1f, Screen.width);
            float gapY = Gap * CanvasScale / Mathf.Max(1f, Screen.height);
            float right = TopBarOnly ? 1f - gapX : 1f - SidebarWidth;
            float top = HideTopBar ? 1f - gapY : 1f - TopBarHeight;
            return new Rect(gapX, gapY, right - gapX, top - gapY);
        }
    }

    // Places the HUD panels and the backdrops that fill the gaps (the camera only clears the arena).
    // Any of them may be null. HUDBuilder calls this when it builds the HUD, and HUDController at
    // runtime, so a HUD saved before the gaps were even gets them too
    public static void PlaceFrame(RectTransform topBar, RectTransform sidebar, RectTransform topBackdrop,
        RectTransform sideBackdrop, RectTransform leftBackdrop, RectTransform bottomBackdrop)
    {
        float top = 1f - TopBarHeight;
        float side = 1f - SidebarWidth;
        bool full = HideTopBar;
        float sideTop = full ? 1f : top;

        // Backdrops: the bands outside the arena
        if (full) Place(topBackdrop, new Vector2(0f, 1f), new Vector2(side, 1f), new Vector2(0f, -Gap), Vector2.zero);
        else Place(topBackdrop, new Vector2(0f, top), Vector2.one, Vector2.zero, Vector2.zero);
        // Without a sidebar the side band is just the Gap down the right edge
        Place(sideBackdrop, new Vector2(side, 0f), new Vector2(1f, sideTop), new Vector2(TopBarOnly ? -Gap : 0f, 0f), Vector2.zero);
        Place(leftBackdrop, Vector2.zero, new Vector2(0f, sideTop), Vector2.zero, new Vector2(Gap, 0f));
        Place(bottomBackdrop, Vector2.zero, new Vector2(side, 0f), Vector2.zero, new Vector2(0f, Gap));

        // Panels: Gap from the screen edge and from each other
        Place(topBar, new Vector2(0f, top), Vector2.one, new Vector2(Gap, Gap), new Vector2(-Gap, -Gap));
        Place(sidebar, new Vector2(side, 0f), new Vector2(1f, sideTop), new Vector2(Gap, Gap), new Vector2(-Gap, full ? -Gap : 0f));
    }

    static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        if (rect == null) return;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}
