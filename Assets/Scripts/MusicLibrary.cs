using System;
using System.Collections.Generic;
using UnityEngine;

// The music store's data: which songs are for sale, which ones the player owns, and which one the
// player picked to play. Logic only. It plays nothing itself: the sound system reads SelectedId
// (and listens to Changed) and decides where and how the picked song plays.
//
// The catalogue is Resources/Music/music_store.json:
//   { "songs": [ { "id": "lair", "title": "Evil Meowmeow (jazz)", "price": 150 }, ... ] }
// id = the song's file name in Resources/Audio/Music (the sound system's track name).
// Owned songs and the pick are saved in PlayerPrefs, so they last between runs. ProgressStore.ResetAll
// (wiping the save) clears them too.
public static class MusicLibrary
{
    public const string CatalogueResource = "Music/music_store";
    public const string OwnedKey = "gm.music.owned";
    public const string SelectedKey = "gm.music.selected";

    [Serializable]
    public class Song
    {
        public string id;
        public string title;
        public int price;
    }

    [Serializable]
    class Catalogue { public List<Song> songs = new List<Song>(); }

    [Serializable]
    class OwnedSave { public List<string> ids = new List<string>(); }

    public enum BuyResult { Bought, AlreadyOwned, NotEnoughGold, UnknownSong, NoWallet }

    // The pick changed (null = back to the game's own music for each place)
    public static event Action<string> Changed;

    static List<Song> songs;
    static HashSet<string> owned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()   // Play without a domain reload keeps statics
    {
        songs = null;
        owned = null;
        Changed = null;
    }

    public static IReadOnlyList<Song> Songs
    {
        get
        {
            if (songs == null) Load(Resources.Load<TextAsset>(CatalogueResource));
            return songs;
        }
    }

    // Tests and tools can swap the catalogue; null or broken text gives an empty store
    public static void Load(TextAsset catalogue) => LoadJson(catalogue != null ? catalogue.text : null);

    public static void LoadJson(string json)
    {
        songs = new List<Song>();
        if (string.IsNullOrEmpty(json)) return;
        Catalogue data;
        try { data = JsonUtility.FromJson<Catalogue>(json); }
        catch (ArgumentException error) { Debug.LogWarning("MusicLibrary: bad catalogue: " + error.Message); return; }
        if (data == null || data.songs == null) return;
        var seen = new HashSet<string>();
        foreach (Song song in data.songs)
        {
            if (song == null || string.IsNullOrEmpty(song.id) || !seen.Add(song.id)) continue;
            if (string.IsNullOrEmpty(song.title)) song.title = song.id;
            song.price = Mathf.Max(0, song.price);
            songs.Add(song);
        }
    }

    public static Song Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (Song song in Songs) if (song.id == id) return song;
        return null;
    }

    static HashSet<string> Owned
    {
        get
        {
            if (owned != null) return owned;
            owned = new HashSet<string>();
            string json = PlayerPrefs.GetString(OwnedKey, "");
            if (json != "")
            {
                OwnedSave save = null;
                try { save = JsonUtility.FromJson<OwnedSave>(json); } catch (ArgumentException) { }
                if (save != null && save.ids != null) foreach (string id in save.ids) if (!string.IsNullOrEmpty(id)) owned.Add(id);
            }
            return owned;
        }
    }

    public static bool IsOwned(string id) => !string.IsNullOrEmpty(id) && Owned.Contains(id);

    public static int OwnedCount => Owned.Count;

    // Pays from the run wallet (the same one the Store uses). A song costs its price once and is kept.
    public static BuyResult Buy(string id) => Buy(id, CrossLevelStatus.Active != null ? CrossLevelStatus.Active : CrossLevelStatus.Ensure());

    public static BuyResult Buy(string id, CrossLevelStatus wallet)
    {
        Song song = Find(id);
        if (song == null) return BuyResult.UnknownSong;
        if (IsOwned(id)) return BuyResult.AlreadyOwned;
        if (wallet == null) return BuyResult.NoWallet;
        if (wallet.getWallet() < song.price) return BuyResult.NotEnoughGold;
        wallet.addWallet(-song.price);
        Owned.Add(id);
        SaveOwned();
        return BuyResult.Bought;
    }

    // The song the player picked to play, or null for the game's own music
    public static string SelectedId
    {
        get
        {
            string id = PlayerPrefs.GetString(SelectedKey, "");
            return id != "" && IsOwned(id) ? id : null;
        }
    }

    // Only an owned song can be picked; null goes back to the game's own music
    public static bool Select(string id)
    {
        if (!string.IsNullOrEmpty(id) && !IsOwned(id)) return false;
        string next = string.IsNullOrEmpty(id) ? null : id;
        if (next == SelectedId) return true;
        if (next == null) PlayerPrefs.DeleteKey(SelectedKey);
        else PlayerPrefs.SetString(SelectedKey, next);
        PlayerPrefs.Save();
        Changed?.Invoke(next);
        return true;
    }

    // ProgressStore.ResetAll: a wiped save starts with no songs and the game's own music
    public static void ResetSave()
    {
        bool hadPick = PlayerPrefs.HasKey(SelectedKey);
        PlayerPrefs.DeleteKey(OwnedKey);
        PlayerPrefs.DeleteKey(SelectedKey);
        PlayerPrefs.Save();
        owned = null;
        if (hadPick) Changed?.Invoke(null);
    }

    static void SaveOwned()
    {
        var save = new OwnedSave { ids = new List<string>(Owned) };
        save.ids.Sort(string.CompareOrdinal);
        PlayerPrefs.SetString(OwnedKey, JsonUtility.ToJson(save));
        PlayerPrefs.Save();
    }
}
