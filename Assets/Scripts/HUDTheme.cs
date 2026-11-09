using UnityEngine;

// Owner: Doeun (UI)
// The HUD's look per version, for A/B playtests. HUDBuilder builds a HUD from one of these
// (Tools > Gold Miner > Create HUD > Version A / B / Beta); HUDController.variant remembers which.
// Version A is Figma "HUD / A" (was "HUD / v2"). Version B starts as a copy of A until it is designed.
// Beta is Figma Version3 "HUD - v3 topbar": no sidebar, everything in a row of panels across the top
public enum HUDVariant { A, B, Beta }

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
            case HUDVariant.Beta: return VersionBeta();
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

    // Beta (Figma Version3 "HUD - v3 topbar"): panels on a darker backdrop. The colors below are
    // the Figma fills; the layout values live in HUDBuilder.CreateBetaTopBar
    public Color betaPanel = new Color32(0x0E, 0x12, 0x10, 0xFF);      // panel fill
    public Color betaLabel = new Color32(0x9A, 0xA3, 0xA0, 0xFF);      // SKILL / DIGEST / YOU / MINER
    public Color betaLevel = new Color32(0xB8, 0xC0, 0xBC, 0xFF);      // LEVEL 2
    public Color betaPlayerBar = new Color32(0xF2, 0x5F, 0x7E, 0xFF);  // tug bar, player side
    public Color betaMinerBar = new Color32(0x7C, 0x4D, 0xFF, 0xFF);   // tug bar, miner side
    public Color betaPillTrack = new Color32(0x23, 0x2A, 0x26, 0xFF);  // skill / digest pill while it refills
    public Color betaSkillInk = new Color32(0x16, 0x32, 0x4D, 0xFF);   // READY / ACTIVE on the full blue pill
    public Color betaDigestInk = new Color32(0x1E, 0x40, 0x12, 0xFF);  // CAN EAT on the full green pill
    public Color betaButton = new Color32(0x1D, 0x24, 0x21, 0xFF);     // pause button

    static HUDTheme VersionBeta()
    {
        HUDTheme theme = new HUDTheme();
        theme.backdrop = new Color32(0x16, 0x1A, 0x18, 0xFF);
        theme.panel = theme.betaPanel;
        theme.pig = new Color32(0xF7, 0x8F, 0xB3, 0xFF);     // $320
        theme.miner = new Color32(0xA7, 0x8B, 0xFA, 0xFF);   // $230
        theme.skill = new Color32(0x4A, 0xA8, 0xF0, 0xFF);   // READY pill
        theme.digest = new Color32(0x7E, 0xD9, 0x57, 0xFF);  // CAN EAT pill
        theme.moneyBar = true;
        theme.timerBold = true;
        return theme;
    }
}
