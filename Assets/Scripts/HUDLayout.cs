using UnityEngine;

// Owner: Doeun (UI)
// The screen split shared by the HUD and the camera: a bar across the top, a sidebar on the right,
// and the arena in what is left (bottom left). Fractions are of the 16:9 frame (ScreenFrame); Gap is
// in canvas units (inside the frame the HUD canvas is always 1920x1080). The frame edge, the top bar,
// the sidebar and the arena are all Gap apart
public static class HUDLayout
{
    public const float TopBarHeight = 0.08f;
    public const float SidebarWidth = 0.143f;
    public const float Gap = 8f;

    // Maze world map: hide the battle top bar so the arena can use the full height.
    public static bool HideTopBar;

    // 0..1 of the frame (origin bottom left; ScreenFrame.ToViewport turns it into a Camera.rect): Gap
    // in from the left and bottom edges; the panels' own Gap insets make the gaps to the top bar and sidebar
    public static Rect GameplayViewport
    {
        get
        {
            float gapX = Gap / 1920f;
            float gapY = Gap / 1080f;
            float right = 1f - SidebarWidth;
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
        Place(sideBackdrop, new Vector2(side, 0f), new Vector2(1f, sideTop), Vector2.zero, Vector2.zero);
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
