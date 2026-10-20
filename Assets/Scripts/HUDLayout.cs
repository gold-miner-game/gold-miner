using UnityEngine;

// Owner: Doeun (UI)
// The screen split shared by the HUD and the camera: a bar across the top, a sidebar on the right,
// and the arena in what is left (bottom left). All values are fractions of the 16:9 frame
// (ScreenFrame), which is the whole screen unless the screen isn't 16:9
public static class HUDLayout
{
    public const float TopBarHeight = 0.08f;
    public const float SidebarWidth = 0.143f;

    // 0..1 of the frame (origin bottom left); ScreenFrame.ToViewport turns it into a Camera.rect
    public static Rect GameplayViewport => new Rect(0f, 0f, 1f - SidebarWidth, 1f - TopBarHeight);
}
