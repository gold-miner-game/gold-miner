# Request: music radio controls (UI)

Owner of the music: Claude (sound director). Owner of this request: whoever builds HUD/menus.

## What already works (sound side, done)

The game is fast, so the music no longer changes with every scene. A **radio** plays the songs one after
another, through the menu, map, store and levels, and keeps playing through level changes and restarts.
**Survival** is the exception: a run is long, so it loops its own song ("Hook, Line and Oinky"), and the
radio resumes where it stopped when the player leaves survival. The chosen song is saved between sessions.

Every song has its own title:

| # | Title | Style |
|---|---|---|
| 0 | Under the Dirt Map | saloon jazz |
| 1 | Hog Wild Swing | hot jazz |
| 2 | Velvet Claws | villain big band |
| 3 | Lantern Light Shuffle | noir jazz |
| 4 | Evil Meowmeow | cabaret metal |
| survival (joins the radio once bought) | Hook, Line and Oinky | surf rockabilly |

## What the player needs (UI, please build)

Update: the world map's **music store** now does the picking (buy, then pick a song, or pick "the
game's music" for the radio), and it's wired to the sound. What's still open is item 2, plus item 1
only if you want quick skipping outside the store.

1. **Pick a song.** Next and previous buttons, and optionally a list of the five radio songs, in the pause
   popup and the main menu. Optional keyboard shortcut: N for next, B for back. Those keys are free; check
   they don't clash with anything new.
2. **Now playing.** A small tag (for example bottom left, about 3 seconds, papercraft style, Fredoka)
   with the song title and style whenever a new song starts. The tag should not cover gameplay
   information.
3. In survival, hide or disable the song controls, because its song is fixed.

## How to wire it

```csharp
SoundDirector.Song[] songs = SoundDirector.Radio;   // .title, .style (radio order above)
SoundDirector.NextSong();                           // skip forward (ignored in survival)
SoundDirector.PreviousSong();                       // skip back
SoundDirector.PlaySong(index);                      // pick from the list
int playing = SoundDirector.RadioIndex;             // highlight the current song
SoundDirector.Song now = SoundDirector.NowPlaying;  // the radio or the survival song
SoundDirector.SongChanged += song => ShowTag(song.title, song.style);   // fires on every new song
```

uGUI buttons click by themselves, so don't add a click sound. The music volume slider is in
docs/audio-settings-request.md.

## Done when

- The player can change songs from the pause popup and the main menu, and sees the title of each new song.
- Controls are hidden or disabled in survival.
