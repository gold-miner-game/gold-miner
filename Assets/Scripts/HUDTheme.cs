using UnityEngine;

// Owner: Doeun (UI)
// The HUD's look per version, for A/B playtests. HUDBuilder builds a HUD from one of these
// (Tools > Gold Miner > Create HUD > Version A / B); HUDController.variant remembers which.
// Version A is Figma "HUD / A" (was "HUD / v2"). Version B starts as a copy of A until it is designed
public enum HUDVariant { A, B }

public class HUDTheme
{
    // Colors (Figma variables in the HUD collection)
    public Color backdrop = new Color(0.07f, 0.09f, 0.15f);      // color/bg/backdrop: behind the panels, outside the arena
    public Color panel = new Color(0.118f, 0.165f, 0.247f);      // color/bg/panel #1E2A3F
    public Color divider = new Color(1f, 1f, 1f, 0.12f);         // color/line/divider
    public Color title = new Color(0.68f, 0.74f, 0.85f);         // color/text/title
    public Color pig = new Color(1f, 0.72f, 0.82f);              // color/accent/pig
    public Color miner = new Color(1f, 0.85f, 0.4f);             // color/accent/miner
    public Color skill = new Color(0.45f, 0.65f, 1f);            // color/accent/skill
    public Color digest = new Color(0.65f, 0.9f, 0.55f);         // color/accent/digest #A6E68C = PlayerCharacter.digestTint
    public Color barBack = new Color(1f, 1f, 1f, 0.15f);         // color/bar/track

    // Sidebar spacing
    public int sidebarPaddingX = 20;      // left / right
    public int sidebarPaddingY = 50;      // top / bottom
    public float sectionGap = 32f;        // between sections and dividers
    public float statusRowGap = 24f;      // between WALLET and LIFE (and item rows)
    public int metersTop = 0;             // above the SKILL meter
    public float meterGap = 50f;          // between the Skill and Digest blocks
    public float statRowHeight = 36f;     // WALLET / LIFE / item rows

    // Top bar score. false: "$0 VS $0" (A). true: "YOU $0 [bar] $0 MINER" (B), where the bar fills
    // from each side with that side's share of all the gold in the level
    public bool moneyBar = false;
    public float moneyBarWidth = 560f;
    public float moneyBarHeight = 16f;
    public float moneyGap = 16f;          // between the labels, amounts and bar
    public float moneyLabelSize = 22f;    // YOU / MINER
    public bool timerBold = false;

    public static HUDTheme For(HUDVariant variant)
    {
        switch (variant)
        {
            case HUDVariant.B: return VersionB();
            default: return new HUDTheme();
        }
    }

    // Version B (Figma "HUD / B"): the score is a tug bar, the timer is bold. Sidebar as in A
    static HUDTheme VersionB()
    {
        HUDTheme theme = new HUDTheme();
        theme.moneyBar = true;
        theme.timerBold = true;
        return theme;
    }
}
