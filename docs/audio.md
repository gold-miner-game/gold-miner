# Audio

Owner: Claude (sound director). Code: `Assets/Scripts/Audio/`. Sounds: `Assets/Resources/Audio/`.

## Direction

The game looks like a handmade papercraft diorama of a gold-rush mine (docs/art-style.md). The sound
follows the same idea:

- **Effects are tactile and toy-sized.** Paper, felt, cardboard, tin, wood and coins, recorded close and
  dry. Nothing sounds sci-fi or realistically violent.
- **Gold is the hero sound.** Eating gold always gives a bite plus a coin ring. Small and medium nuggets
  ring short, large ones cascade, diamonds sparkle. When a miner banks gold you hear a muffled, sour
  cash register, so you know you lost it without looking.
- **Danger is readable.** A hook aimed at you clicks (a ratchet) when the red line appears, before it
  flies. A hit is a felt thwack plus your character's own yelp. A block is a springy *boing*.
- **Characters have voices.** Each of the six characters has a hurt sound and a happy sound (played
  on the pick screen).
- **Music is a dark-comic live band** (ElevenLabs Music, one song per place) at one shared loudness
  (-16 LUFS), sitting under the effects.
- **Story and dialogue scenes are silent on purpose.** Their animation is a separate pass that will
  bring its own sound.

## How it works

- `SoundDirector` creates itself when the game starts (`RuntimeInitializeOnLoadMethod`), so no scene or
  prefab was edited. It owns a pool of 24 voices, the music players, the ambience loop and the volume
  settings.
- `SoundCues` is the mix table: every cue's volume, pitch spread, minimum gap, maximum copies at once,
  bus (music / effects / menus), and how much it dips the music. Re-balance the mix here.
- `SoundWatcher` turns existing game events into sound without touching gameplay. It listens to
  `TutorialEvents`, `Treasure.Eaten` and `HookHealth.Destroyed`. It only reads state: the round clock,
  the money goal, and the player's `SkillActive`.
- One-line `Sfx.Play(...)` calls, marked `// sound`, cover moments that have no event: a hook firing,
  grabbing, being cut, hitting rock; a rock breaking; a miner banking gold; a life pickup; a shield
  absorbing a hit; pause; the store; portals; revive; IMGUI buttons. None of them changes behaviour.
- Takes: each cue folder holds 2–4 takes. A random one plays, never the same take twice in a row,
  with a small pitch spread. Randomness uses its own `System.Random`, so gameplay's
  `UnityEngine.Random` sequence is untouched (the same rule as the art layer).
- Every uGUI button and slider clicks (and gives a soft hover tick) automatically through the
  EventSystem. IMGUI buttons call `Sfx.Ui("ui_click")`.
- Import settings come from `Assets/Editor/AudioImportRules.cs`: effects are mono and decoded on load;
  music is streamed Vorbis.

## Music: the radio

The game is fast, so the music doesn't follow the scenes. One **radio** plays the songs back to back
through the menu, map, store and levels. It never restarts on a level change or a retry, and the next
session starts on the same song. **Survival** is the only exception: a run is long, so it loops its own
song, and the radio resumes where it stopped afterwards.

| Radio order | Title (file in Resources/Audio/Music) | Style | Store price |
|---|---|---|---|
| 1 | Under the Dirt Map (`under_the_dirt_map`) | saloon jazz | free |
| 2 | Hog Wild Swing (`hog_wild_swing`) | hot jazz | free |
| 3 | Tumbleweed Tunnels (`tumbleweed_tunnels`) | desert western | 1250 |
| 4 | Velvet Claws (`velvet_claws`) | villain big band | 1250 |
| 5 | Lucky Strike Polka (`lucky_strike_polka`) | oompah polka | 1500 |
| 6 | Lantern Light Shuffle (`lantern_light_shuffle`) | noir jazz | 1500 |
| 7 | Dig It Disco (`dig_it_disco`) | funk disco | 2000 |
| 8 | Nine Lives Tango (`nine_lives_tango`) | villain tango | 2000 |
| 9 | Evil Meowmeow (`evil_meowmeow`) | cabaret metal | 2250 |
| 10 / survival | Hook, Line and Oinky (`hook_line_and_oinky`) | surf rockabilly | 2250 |

Songs 1, 2, 4, 6, 9 and 10 come from the first six songs (the `audio/music` branch). Songs 3, 5, 7 and 8 were
made in the second pass (ElevenLabs Music v2.5, 2:30 each; flow "Gold Miner - music pass 2"). They are **sung**,
with original game lyrics and animal sounds on the beat:

- **Lucky Strike Polka:** a beer-hall gang chorus, "Oink oink, hey!", and pig oinks on the off-beats.
- **Tumbleweed Tunnels:** a gravelly cowboy baritone, coyote howls and a horse whinny.
- **Dig It Disco:** a soul diva with a "dig dig!" choir and cat meows as ad-libs.
- **Nine Lives Tango:** a villain cat singer, purrs and hisses, ending on "Meow."

A Scribe transcription confirmed the lyrics come through almost word for word. The lyrics, plus the
instrumental takes made before (`*_instrumental.mp3`), are in
`E:\Create with code\sound\elevenlabs\oinky_music_pass2`.

- Every song has its own title. The jazz versions used to share their originals' names.
- Songs crossfade into each other (3 s). The music steps back to half volume while paused, and dips
  under the win/lose and "target reached" jingles.
- **The music store** (world map, docs/music-store.md) is wired in. Its catalogue
  (`Resources/Music/music_store.json`) lists all ten songs at the prices above. Only the first two are free;
  buying a free song only lets you pin it.
  - Songs the player bought **join the radio**. Without any purchases the radio plays the two free songs.
  - A song **picked** in the store (`MusicLibrary.SelectedId`) plays on loop everywhere, **survival included**,
    because the player chose it. Picking "the game's music" resumes the radio where it stopped.
- A now-playing tag (and optional next/back controls) is UI work, requested in
  docs/audio-radio-ui-request.md. The sound side exposes `SoundDirector.Radio`, `NextSong / PreviousSong /
  PlaySong`, `NowPlaying` and `SongChanged`.
- The sources were trimmed and levelled to -16 LUFS. The untouched originals are in
  `E:\Create with code\sound\elevenlabs\oinky_music` and in the `audio/music` branch history.

## Ambience

`amb_mine` is a seamless cave room tone with drips and creaking beams. It plays quietly under every
level (louder in battles, softer on the world map) and stops in menus.

## Cue list

| Cue | When |
|---|---|
| hook_fire | a hook leaves its hole (`Hook.Fire`) |
| hook_aim_warn | a miner locks its aim on you (`hook_aim`) |
| hook_grab | a claw closes on treasure |
| hook_rock / rock_break | a hook hits a rock / the rock breaks; also the rhino smashing rocks |
| hook_blocked | a block, tail, charge or ram bounce (`hook_blocked`) |
| rope_cut | J cuts a rope |
| gold_dropped | treasure knocked off a hook (`treasure_stolen`) |
| miner_bank | a miner's hook gets treasure home |
| ice_break | frozen claws, rocks or treasure crack |
| eat_munch + coin_small / coin_large / diamond | the player eats treasure (`Treasure.Eaten`) |
| digest_full | trying to eat while still digesting |
| life_pickup | a heart is collected |
| hit_impact + hurt_<character> | the player is hit (`player_hit`) |
| shield_soak | the store shield absorbs a hit |
| skill_block / roll / tail / tongue / breath / charge | K skill starts, per character |
| roll_cry | layered on J.K. Rolling's roll: an excited cat battle cry ("mrrrow!") |
| skill_ready | the K skill's cooldown has run out (a soft marimba ding; not for hold skills) |
| enemy_hit + enemy_yowl | a Brawler is knocked back: bounced off the pig's block, or shoved by the rhino's charge |
| enemy_hit / enemy_down | a miner or walker takes damage / is destroyed |
| round_start | a timed money round begins |
| target_reached | the money goal is reached |
| clock_tick | each of the last 10 seconds (the last 5 higher) |
| round_won / round_lost | the round ends |
| unlock | a skill is unlocked, or a maze sector is cleared |
| portal_enter / portal_reveal / denied | portals: enter, reveal, locked |
| exit_open / squeeze_rumble / squeeze_slam / revive | the squeeze level and the world-map revive |
| ui_click / ui_hover / ui_pause | menus and the pause popup |
| buy / denied | the store and character unlocks |
| pick_confirm + happy_<character> | a character is picked |

## Sources and credits

- Effects were generated with ElevenLabs Sound Effects v3 (flow "Gold Miner - SFX pass 1"). The prompts
  are in the flow.
- The `happy_pig` oinks are cut from Freesound #518872 ("pig sounds" by SoundDesignXP), and the
  `happy_frog` croaks from Freesound #749487 (Mexican frog calls by wak4n). Credit: **yk**. Both will be
  listed properly once the game has a credits screen.
- Takes were chosen by measurement (`_claude_scratch/listen`: an AudioSet tagger plus a CLAP text-audio
  matcher, then levelled to the same loudness). Cues whose first takes missed were re-recorded once.
  The whole effects pass cost about 700 ElevenLabs credits.
- Music: ElevenLabs Music v2.5 (see the `audio/music` branch README).

## Volume settings

The API is ready (`SoundDirector.GetVolume / SetVolume / Muted`, saved in PlayerPrefs). The UI for it
is requested in docs/audio-settings-request.md.
