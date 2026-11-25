using UnityEngine;

// The music store, opened on the world map when the player walks into its portal (MapHub.Enter).
// Logic and a plain placeholder panel only (no art): buy a song with the run wallet, pick an owned
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

    void OnGUI()
    {
        ScreenFrame.BeginGUI();
        GUI.depth = -10;
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            rowStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleLeft };
            noteStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            titleStyle.normal.textColor = rowStyle.normal.textColor = noteStyle.normal.textColor = Color.white;
        }

        float arenaWidth = ScreenFrame.GuiWidth * (1f - HUDLayout.SidebarWidth);
        float width = Mathf.Min(720f, arenaWidth - 40f);
        float height = Mathf.Min(620f, ScreenFrame.GuiHeight - 40f);
        var box = new Rect((arenaWidth - width) * 0.5f, (ScreenFrame.GuiHeight - height) * 0.5f, width, height);
        GUI.Box(box, GUIContent.none);
        GUI.Box(box, GUIContent.none);   // twice: the default skin's box is too see-through over the map

        float x = box.x + 20f, w = box.width - 40f, y = box.y + 14f;
        GUI.Label(new Rect(x, y, w, 40f), node != null ? node.displayName : "Music Store", titleStyle);
        y += 44f;
        CrossLevelStatus wallet = CrossLevelStatus.Active;
        GUI.Label(new Rect(x, y, w, 26f), "Wallet: " + (wallet != null ? wallet.WalletLabel() : "$0"), noteStyle);
        y += 34f;

        string picked = MusicLibrary.SelectedId;
        Row(new Rect(x, y, w - 20f, 36f), picked == null ? "> Game music (each place's own song)" : "Game music (each place's own song)",
            "", picked == null ? null : "Play", () => Pick(null));
        y += 42f;

        var songs = MusicLibrary.Songs;
        float listHeight = box.yMax - 110f - y;
        var view = new Rect(0f, 0f, w - 20f, songs.Count * 42f);
        scroll = GUI.BeginScrollView(new Rect(x, y, w, listHeight), scroll, view);
        for (int i = 0; i < songs.Count; i++)
        {
            MusicLibrary.Song song = songs[i];
            bool owned = MusicLibrary.IsOwned(song.id);
            bool playing = owned && picked == song.id;
            string name = (playing ? "> " : "") + song.title;
            string state = owned ? "Owned" : "$" + song.price.ToString("N0");
            string action = !owned ? "Buy" : playing ? null : "Play";
            Row(new Rect(0f, i * 42f, view.width, 36f), name, state, action,
                () => { if (owned) Pick(song.id); else Buy(song); });
        }
        if (songs.Count == 0) GUI.Label(new Rect(0f, 0f, view.width, 36f), "No songs for sale yet", noteStyle);
        GUI.EndScrollView();

        GUI.Label(new Rect(x, box.yMax - 100f, w, 30f), message, noteStyle);
        if (GUI.Button(new Rect(box.center.x - 90f, box.yMax - 60f, 180f, 44f), "Leave")) Leave();
    }

    void Row(Rect r, string name, string state, string action, System.Action onClick)
    {
        GUI.Label(new Rect(r.x, r.y, r.width - 260f, r.height), name, rowStyle);
        GUI.Label(new Rect(r.xMax - 250f, r.y, 110f, r.height), state, rowStyle);
        if (action != null && GUI.Button(new Rect(r.xMax - 130f, r.y, 120f, r.height), action)) onClick();
    }
}
