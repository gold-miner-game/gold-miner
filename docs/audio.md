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

## Music map

| Place | Track (Resources/Audio/Music) | Song |
|---|---|---|
| Main menu | `title` | 01 Under the Dirt Map: saloon jazz, whistled melody |
| Store, character pick, card map | `shop` | 01, jazz version: noir combo |
| World map hub (the evil cat's lair) | `lair` | 03 Evil Meowmeow, jazz version: villain big band |
| Levels and tutorials | `battle` | 02 Hook Line and Oinky, jazz version: hot jazz |
| Survival and roam (endless waves) | `endless` | 02 Hook Line and Oinky: surf-rockabilly |
| Level 8 (the last level) | `finale` | 03 Evil Meowmeow: cabaret metal |

- Songs crossfade on scene changes. A restart (R) keeps the song playing.
- Each song wraps around with a 3-second crossfade, so its ending never cuts.
- The music steps back to half volume while paused, and dips under the win/lose jingles and the
  "target reached" jingle.
- The source files were trimmed (start and end silence) and levelled to -16 LUFS. The untouched
  originals are in `E:\Create with code\sound\elevenlabs\oinky_music` and in the `audio/music` branch history.

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
