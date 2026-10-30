using UnityEngine;

// Owner: Doeun (UI)
// The screen split shared by the HUD and the camera: a bar across the top, a sidebar on the right,
// and the arena in what is left (bottom left). All values are fractions of the screen
public static class HUDLayout
{
    public const float TopBarHeight = 0.08f;
    public const float SidebarWidth = 0.143f;

    // Maze world map: hide the battle top bar so the arena can use the full height.
    public static bool HideTopBar;

    // In viewport space (0..1, origin bottom left), as Camera.rect wants it
    public static Rect GameplayViewport => new Rect(
        0f, 0f,
        1f - SidebarWidth,
        HideTopBar ? 1f : 1f - TopBarHeight);
}
