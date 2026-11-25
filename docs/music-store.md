# Music store

A store on the world map where the player buys songs with the run wallet, then picks one of the
songs they own (or the game's own music). This branch has the logic only: no art, no music, and
**no playback**. Where and how the picked song plays is up to the sound system.

## On the map

- WorldMap node `music_store` (type `MusicStore`, "Music Store"). Its prerequisite is `battle_06`
  (Level 1), the same as Level 2, so it opens at the same moment Level 2 does.
- Its portal is made at runtime from a green rock: `MapHub.PortalFromGreenRock` switches the
  rock off and puts a hidden `HubPortal` on its spot, so the world map scene is not edited. The rock
  is the lower tooth just right of Level 2 (`MapHub.musicStoreAt`, world position 0.525, -2.94; the
  nearest green rock within half a tile is used).
- Until it opens it is a solid green rock like every locked portal; then it gets the usual
  one-time reveal and spotlight.
- Walking into it opens `MusicStorePanel` on the map itself (no scene change). The map is frozen
  (time scale 0) while it is open. Leave closes it and marks the node visited, like the Store.
  The panel is a plain IMGUI placeholder for the art pass.

## Data: `MusicLibrary`

- Catalogue: `Assets/Resources/Music/music_store.json`, a list of `{ id, title, price }`.
  `id` is the track name in `Resources/Audio/Music`. The sound owner set the list and prices: all six
  songs, the three radio starters free, the rest 300 to 450 (see docs/audio.md, "Music: the radio").
- `Buy(id)` pays `price` once from the run wallet (`CrossLevelStatus`, the Store's wallet). Results:
  `Bought`, `AlreadyOwned`, `NotEnoughGold`, `UnknownSong`, `NoWallet`.
- `Select(id)` picks an owned song; `Select(null)` goes back to the game's own music.
- Owned songs (`gm.music.owned`) and the pick (`gm.music.selected`) are saved in PlayerPrefs and kept
  between runs. Wiping the save (`ProgressStore.ResetAll`) clears both.

## For the sound system

Read `MusicLibrary.SelectedId` (null = play the normal per-place music) and listen to
`MusicLibrary.Changed` (sends the new id, or null). Nothing else is needed from the store.

## Test

`MusicStorePlayTest.Run`: node and unlock, the green rock becoming the portal in the real world map
scene, buying, picking, saving, and the panel. It puts back the saved values it touches.
