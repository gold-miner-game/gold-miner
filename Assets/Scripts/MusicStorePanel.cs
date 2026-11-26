using UnityEngine;

// The music store, opened on the world map when the player walks into its portal (MapHub.Enter).
// Buy a song with the run wallet, pick an owned
// song (or the game's own music) and leave. The world map stays loaded and frozen while it is open.
// What the picked song does is the sound system's call: it reads MusicLibrary.SelectedId.
public class MusicStorePanel : MonoBehaviour
{
    public static MusicStorePanel Open { get; private set; }
    public static bool IsOpen => Open != null;

    MapNode node;
    float timeScaleBefore = 1f;
    string message = "";
    Vector2 scroll;
    GUIStyle titleStyle, rowStyle, noteStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Open = null; }

    public static MusicStorePanel Show(MapNode node, Transform parent)
    {
        if (Open != null) return Open;
        var go = new GameObject("Music Store");
        if (parent != null) go.transform.SetParent(parent, false);
        var panel = go.AddComponent<MusicStorePanel>();
        panel.node = node;
        panel.timeScaleBefore = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        Open = panel;
        PlayAnalytics.NodeEnter(node != null ? node.id : "music_store");
        return panel;
    }

    // Leaving counts as a visit (like the Store), so the portal shows as cleared afterwards
    public void Leave()
    {
        if (node != null) ProgressStore.MarkCompleted(node.id);
        Close();
    }

    void Close()
    {
        if (Open == this) Open = null;
        Time.timeScale = timeScaleBefore > 0f ? timeScaleBefore : 1f;
        Destroy(gameObject);
    }

    // Keep the map frozen even if the pause popup opens and closes on top of the store
    void Update() { if (Open == this) Time.timeScale = 0f; }

    void OnDestroy()
    {
        if (Open != this) return;
        Open = null;
        Time.timeScale = timeScaleBefore > 0f ? timeScaleBefore : 1f;
    }

    public string Buy(MusicLibrary.Song song)
    {
        switch (MusicLibrary.Buy(song.id))
        {
            case MusicLibrary.BuyResult.Bought: return message = "Bought " + song.title;
            case MusicLibrary.BuyResult.AlreadyOwned: return message = "You already own " + song.title;
            case MusicLibrary.BuyResult.NotEnoughGold: return message = "Not enough gold for " + song.title;
            default: return message = "Can't buy " + song.title + " now";
        }
    }

    public string Pick(string id)
    {
        if (!MusicLibrary.Select(id)) return message = "Buy it first";
        MusicLibrary.Song song = MusicLibrary.Find(id);
        return message = song != null ? "Now playing: " + song.title : "Back to the game's own music";
    }

    // --- The look: a cut-paper record shop (art lead) ------------------------------------------------
    // A stitched felt card; the radio first, as the clear way back to the game's rotation; then one
    // paper strip per song: its record (turning a notch per stop-motion beat while it plays), the title
    // with its style under it, a price column wide enough for four digits ("Free" for the free songs,
    // "Owned" once bought), "Playing" on the song you hear (SoundDirector.NowPlaying) and the star
    // stamp on your pick (MusicLibrary.SelectedId). A free song is taken and picked in one click.
    // IMGUI buttons click with Sfx.Ui("ui_click").
    // Art: Resources/Art/Music/{Record,Radio,Icon_Star} (tools/art/make_music.py), UI/FeltPanel, UI/PaperCard.
    GUIStyle cardStyle, stripStyle, styleStyle, priceStyle, stampStyle;
    Texture recordArt, radioArt, starArt;
    const float RowH = 58f, RowGap = 8f;
    static readonly Color Ink = new Color(0.24f, 0.16f, 0.1f), Umber = new Color(0.48f, 0.34f, 0.22f),
                          Gold = new Color(1f, 0.84f, 0.42f);

    void Styles()
    {
        cardStyle = new GUIStyle(GUI.skin.box);
        ArtLibrary.FeltBox(cardStyle);
        stripStyle = new GUIStyle(GUI.skin.box);
        ArtLibrary.PaperBox(stripStyle);
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 34, alignment = TextAnchor.MiddleCenter };
        rowStyle = new GUIStyle(GUI.skin.label) { fontSize = 21, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
        styleStyle = new GUIStyle(rowStyle) { fontSize = 15 };
        priceStyle = new GUIStyle(rowStyle) { alignment = TextAnchor.MiddleRight };
        stampStyle = new GUIStyle(rowStyle) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
        noteStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
        if (PaperType.Display != null) titleStyle.font = PaperType.Display;
        if (PaperType.Body != null) rowStyle.font = styleStyle.font = priceStyle.font = stampStyle.font = noteStyle.font = PaperType.Body;
        titleStyle.normal.textColor = Gold;
        rowStyle.normal.textColor = priceStyle.normal.textColor = Ink;
        styleStyle.normal.textColor = stampStyle.normal.textColor = Umber;
        noteStyle.normal.textColor = ArtLibrary.Cream;
        recordArt = Tex("Music/Record");
        radioArt = Tex("Music/Radio");
        starArt = Tex("Music/Icon_Star");
    }

    static Texture Tex(string name) { Sprite s = ArtLibrary.Get(name); return s != null ? s.texture : null; }

    static bool Button(Rect r, string label)
    {
        if (!GUI.Button(r, label)) return false;
        Sfx.Ui("ui_click");   // IMGUI buttons don't click by themselves
        return true;
    }

    // "Dig It Disco (funk disco)" -> "Dig It Disco" and "funk disco"
    static void Split(string full, out string title, out string style)
    {
        title = full ?? ""; style = "";
        int open = title.LastIndexOf(" (");
        if (open > 0 && title.EndsWith(")")) { style = title.Substring(open + 2, title.Length - open - 3); title = title.Substring(0, open); }
    }

    void OnGUI()
    {
        ScreenFrame.BeginGUI();
        GUI.depth = -10;
        if (cardStyle == null) Styles();

        float arenaWidth = ScreenFrame.GuiWidth * (1f - HUDLayout.SidebarWidth);
        float width = Mathf.Min(760f, arenaWidth - 40f);
        float height = Mathf.Min(650f, ScreenFrame.GuiHeight - 30f);
        var box = new Rect((arenaWidth - width) * 0.5f, (ScreenFrame.GuiHeight - height) * 0.5f, width, height);
        GUI.Box(box, GUIContent.none, cardStyle);

        float x = box.x + 30f, w = box.width - 60f, y = box.y + 18f;
        GUI.Label(new Rect(x, y, w, 44f), node != null ? node.displayName : "Music Store", titleStyle);
        y += 46f;
        CrossLevelStatus wallet = CrossLevelStatus.Active;
        GUI.Label(new Rect(x, y, w, 24f), "Wallet  " + (wallet != null ? wallet.WalletLabel() : "$0"), noteStyle);
        y += 32f;

        string picked = MusicLibrary.SelectedId;
        SoundDirector.Song now = SoundDirector.NowPlaying;
        // The radio first: the clear way back to the game's own rotation
        Strip(new Rect(x, y, w - 16f, RowH), radioArt, false, "The game's music (radio)",
              "every free and bought song, in turn", "", picked == null, false,
              picked == null ? null : "Pick", () => Pick(null));
        y += RowH + RowGap + 4f;

        var songs = MusicLibrary.Songs;
        float listHeight = box.yMax - 116f - y;
        var view = new Rect(0f, 0f, w - 16f, songs.Count * (RowH + RowGap));
        scroll = GUI.BeginScrollView(new Rect(x, y, w, listHeight), scroll, view);
        for (int i = 0; i < songs.Count; i++)
        {
            MusicLibrary.Song song = songs[i];
            bool owned = MusicLibrary.IsOwned(song.id);
            bool free = song.price <= 0;
            bool isPick = owned && picked == song.id;
            bool hearing = now != null && now.file == song.id;
            Split(song.title, out string title, out string style);
            string price = owned ? "Owned" : free ? "Free" : "$" + song.price.ToString("N0");
            string action = isPick ? null : !owned && !free ? "Buy" : "Pick";
            Strip(new Rect(0f, i * (RowH + RowGap), view.width, RowH), recordArt, hearing, title, style, price, isPick, hearing,
                  action, () =>
                  {
                      if (owned) Pick(song.id);
                      else if (free) { Buy(song); if (MusicLibrary.IsOwned(song.id)) Pick(song.id); }   // a free song: taken and picked in one click (Buy's message stays if it failed)
                      else Buy(song);
                  });
        }
        if (songs.Count == 0) GUI.Label(new Rect(0f, 0f, view.width, RowH), "No songs for sale yet", noteStyle);
        GUI.EndScrollView();

        GUI.Label(new Rect(x, box.yMax - 104f, w, 30f), message, noteStyle);
        if (Button(new Rect(box.center.x - 90f, box.yMax - 64f, 180f, 46f), "Leave")) Leave();
    }

    // One paper strip: art, title over style, the Playing / picked stamps, price, the action button
    void Strip(Rect r, Texture art, bool spinning, string title, string style, string price, bool isPick, bool hearing,
               string action, System.Action onClick)
    {
        GUI.Box(r, GUIContent.none, stripStyle);
        if (art != null)
        {
            // the store freezes time, so the record turns on real time, a held notch per stop-motion pose
            Texture shown = art == recordArt ? ArtLibrary.RecordFrame(spinning) : art;
            if (shown != null) GUI.DrawTexture(new Rect(r.x + 8f, r.y + 5f, r.height - 10f, r.height - 10f), shown, ScaleMode.ScaleToFit, true);
        }
        float tx = r.x + r.height + 6f;
        float right = r.xMax - 132f;                     // the button's column
        const float priceW = 100f, stampW = 96f;
        float textW = right - priceW - stampW - tx - 8f;
        GUI.Label(new Rect(tx, r.y + 6f, textW, 28f), title, rowStyle);
        if (style != "") GUI.Label(new Rect(tx + 1f, r.y + 31f, textW, 20f), style, styleStyle);

        // the stamps: what you hear, and what you picked
        var stamp = new Rect(right - priceW - stampW, r.y, stampW, r.height);
        if (isPick && starArt != null) GUI.DrawTexture(new Rect(stamp.center.x - 15f, r.y + 4f, 30f, 30f), starArt, ScaleMode.ScaleToFit, true);
        if (hearing) GUI.Label(new Rect(stamp.x, isPick ? r.y + 32f : r.y, stampW, isPick ? 22f : r.height), "Playing", stampStyle);
        else if (isPick) GUI.Label(new Rect(stamp.x, r.y + 32f, stampW, 22f), "Picked", stampStyle);

        if (price != "") GUI.Label(new Rect(right - priceW, r.y, priceW - 10f, r.height), price, priceStyle);
        if (action != null && Button(new Rect(right + 6f, r.y + 9f, 120f, r.height - 18f), action)) onClick();
    }
}
