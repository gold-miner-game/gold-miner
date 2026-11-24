# Art style: Papercraft Gold Rush

Gold Miner looks like a handmade diorama seen from above. Everything is layered cut paper and wool felt, with
stitching, soft contact shadows and visible fibre. The floor is a dark, warm mine. Gold is the brightest, glossiest
thing on screen.

![key frame](images/art-keyframe.jpg)

## The game now

Real screens from a build of this branch (captured with `tools/art_capture/capture.ps1`):

![screens](images/art-screens.jpg)

Before and after:

![before and after](images/art-before-after.jpg)

## Rules

1. **Readable first.** The floor is dark and low-contrast, and play pieces are lighter and saturated. A piece must
   read at 40 px on screen. No busy patterns under the playfield.
2. **One light.** Flat light from the top left, with a darker cut edge on the bottom right of shapes.
3. **Tints can be gameplay.** Some colours are rules, not decoration:
   - a green rock (`MapHub.GreenRock`) is rhino-proof;
   - map wall colours mark the mode areas.

   Anything whose colour code reads (`Rock`, `MapHub`) keeps its tint. Its art is light greyscale paper so the
   tint still colours it. Only art whose tint means nothing is shown in its own colours (`ArtLibrary.ApplyPainted`).
4. **Same footprint.** A new sprite covers exactly the box of the shape it replaces: 256 px = 1 unit, centred,
   edge to edge. Where gameplay reads a sprite's bounds (a carried treasure sits by them), the art is a full-rect
   sprite whose canvas is the old box (`ArtImportRules`). Colliders, scales and gameplay components are never
   changed for looks.
5. **Art has its own dice.** Effects draw from `ArtLibrary.Range/Value`, never `UnityEngine.Random`, so
   they can't shift what spawns or AI roll next.
6. **Art never changes gameplay.** No changes to rules, skills, timing, hitboxes or balance. If an effect has to
   hook into gameplay code, it only reads state, and it falls back to the old look when the art is missing.

## Characters

![character art bible](images/art-characters.jpg)

The heroes and the Evil Meowmeow are stitched felt. In the game they are still built from the circle, triangle,
capsule and square shapes, which are felt with a sewn seam, so every existing animation and skill works unchanged.
Pure-black enemy felt became charcoal (`#2B2426`) so it reads on the dark floor. The concept sheet above was made
with Meshy's Agent as the reference for future character art.

## Type

| Use | Font | Licence |
|---|---|---|
| HUD, buttons, dialogue, body text | Fredoka SemiBold | SIL Open Font License |
| Big titles | Luckiest Guy | Apache 2.0 |

Both fonts and their licences are in `Assets/Resources/Art/Fonts`. `PaperType` switches only texts still on the stock
fonts, so a font someone chose on purpose stays.

## Scope

Only what the shipped game reaches from the Boot scene: Boot → Menu → tutorial levels / world-map hub
(`world_map`) → levels, Select ("WHO ARE U"), Store, Survival. The card map (`Map.unity`), `RolePick.unity`
and the Dev scenes are not reached and are left as they are.

## Palette

| Role | Colour |
|---|---|
| Mine floor | `#3B2A1E` (felt umber) |
| Torn-paper strata | `#6E4B33`, `#A9772E` |
| Gold | `#F2C230` to `#B8860B` (gold leaf) |
| Stone | light paper grey, tinted per use |
| Ink and outlines | `#211F20` |
| Slow-time cue | the floor cools toward `#8CBFB8` |

## Where things live

| What | Where |
|---|---|
| Painted sprites (generated with Meshy, cut with `tools/art/slice_sheet.py`) | `Assets/Resources/Art/` |
| Import settings for that folder (applied automatically) | `Assets/Editor/Art/ArtImportRules.cs` |
| Lookup used by runtime code; falls back to the old drawing | `Assets/Scripts/Art/ArtLibrary.cs` |
| Mine floor behind every orthographic scene, no scene edits | `Assets/Scripts/Art/MineBackdrop.cs` |
| Primitive shapes (circle, square, triangle, hexagon) | `Assets/Sprites/` |
| Prefab sprite swaps (Tools > Gold Miner > Art > Apply Papercraft Reskin) | `Assets/Editor/Art/PapercraftReskin.cs` |
| Screenshots of real screens in an isolated copy (no analytics, no save changes) | `tools/art_capture/capture.ps1` |
| Moments a scene load never shows (`level_02:win`, `:fail`, `:crush`, `:reward`, `:pause`, `Menu:confirm`) | `-Scenes` entries `NAME:action`, see `tools/art_capture/ArtCaptureRunner.cs` |

Small pieces drawn by code use the same materials:

| Piece | Look |
|---|---|
| Lives over the head | felt pips sewn on a dark felt strip (`Fx/LifePip`, 9-slice `Slices/FeltStrip`); red / orange / dark tints unchanged |
| Hook claw arms | rolled tin wire (`Tiles/Wire`), tinted by the hook colour like the twine rope |
| SKILL / DIGEST pills | the fill is a paper strip in the same colour, inside the rounded mask |
| Survival grid | quilting seams sewn into the felt (`Tiles/QuiltSeam`), same cell size and tint |
| Hook HP bar | the same sliced felt strip as the Lives, same green / yellow / red |
| Hook claw | a riveted tin hub at the pivot of the line-drawn arms (`PaperClaws`); nothing drawn past the arms, icy while frozen |
| Rocks and walls | flat 2D cut-paper stones, two or three torn layers seen from above (`Objects/Stone1..9`, `PaperRocks`); the hexagon was only a level-design placeholder. Each stone boils on threes, keeps its tint (green = rhino-proof, map colours), and shrinks its drawing to 0.96 x the distance to its nearest rock so stones never overlap; colliders untouched |
| Hidden portals | drawn as the same stones while disguised (`PaperPortalStones`), with the rocks' shadow, boil and fit, so the secret holds; the hexagon returns as the reveal starts |
| Lamplight | warm pools on the floor only, never over a game piece: the miner's lamp around the player, a soft mine lamp from the top left (the cover's light) and each lantern prop; flicker on threes (`PaperLamps`) |
| Set props | flat cut-paper and felt (crate, lantern, moss, mushrooms, pebbles, plank, rail, rope), replacing the earlier photo-like props |
| Elsa's frost | the rim's noise is torn paper and fibre (`Tiles/PaperFrostNoise`, same mean and contrast) and its drift steps on twos; colour timeline, coverage and every freeze rule untouched. The breath flipbook is kept: its measured front is used by the freeze logic |
| Red Square | a red felt block with a stitched seam and an angry ink face (`Objects/RedSquare`), same one-unit bounds and red tint |
| Money target | felt pennant flag (`Fx/Pennant`); "past the target" stripes stay the miner's lavender and white, as felt and paper (`Tiles/PaperStripes`) |
| Map | paper star on portals, paper chevron on one-way doors, place names on felt tags (`PaperTags`) |
| IMGUI defaults | default-skin buttons are paper cards, default boxes are stitched felt (`PaperType`) |
| Titles | display font, gold leaf (if white), umber outline, paper drop shadow (`PaperType`) |
| "Digesting…" cue | a paper tag behind the label, same pulse |

Effect sprites are generated by `tools/art/make_fx.py` and short numpy snippets
(white or cream, so code tints still apply).

The primitive shapes were repainted in place with `tools/art/paperize.py`. Their files, alpha shapes and physics
outlines are unchanged, so every shape-built character, rock and map tile picks up the paper look automatically.

## Animation: the stop-motion rules

Research notes:

- Hand animation is shot **on twos**: each pose is held for 2 frames, so 12 poses per second at 24 fps. Ones are
  kept for fast, detailed action, and animators mix the two within a shot.
- Stepped motion only reads as crafted when the poses themselves ease, ramping into a move and settling out of it.
- A **boil** is 2–3 redrawings of the *same* registered pose. The silhouette stays put and only the surface and
  edges shimmer.
- In papercraft games (Tearaway, Blender Studio's papercraft devlog), the world animates "at a lower frame-rate
  akin to stop motion". Characters use swappable sticker parts (replacement animation) instead of rigs.

The rules that follow:

1. **Gameplay never steps.** Movement, hooks, collisions and timers stay smooth at full frame rate, because
   input feel is gameplay. Only art-owned motion is stepped: effects, UI transitions, idle life.
2. **One shared clock on twos.** Art motion samples `StopMotion.Time`, game time snapped to 12 fps, so every
   cut-out moves on the same beat, the way frames are shot on a stage.
3. **Poses, not jitter.** Every art move is anticipation, action, settle. Each has a held first and last pose.
4. **Boil keeps registration.** Boil frames may change surface and edge pixels only, never position, rotation
   or scale. They run on threes (8 fps), so the surface feels handled, not shaken.
5. **Pause means still.** Art motion uses the game's (scaled) time, so a paused game is a still photo of the set.

The first attempt at a treasure "boil" (random sprite jitter, removed) broke rules 3 and 4. It moved the whole
cut-out, so it read as shaking.

### Performances

The original game showed most states with a colour change. These performances were added on top. All are
art-only and posed on twos.

| Moment | Performance | Where |
|---|---|---|
| Idle | blinks every 2–5 s, an occasional ear flick | `PaperPuppet` |
| Moving | eyes look where it goes; ears trail the motion and overshoot when it stops | `PaperPuppet` |
| Eat | gulp (eyes pop wide), content squint, gold-leaf burst and ring | `PaperPuppet`, `PaperFx` |
| Digesting | chewing: ears wiggle alternately on threes, eyes half closed | `PaperPuppet` |
| Block (Mr. Hyde) | one-pose wind-up, then ears pinned back and eyes squeezed; dust ring | `PaperPuppet` |
| Roll (JK. Rolling) | dust kicked up behind the roll | `PaperPuppet` |
| Charge (Hulk) | ears flat back, felt steam puffs left behind | `PaperPuppet` |
| Swift | paper speed strips trail the runner | `PaperPuppet`, `PaperFx` |
| Shield | the bubble breathes on threes | `PaperPuppet` |
| Hit | flinch (eyes clench, ears flatten), felt heart scraps fly | `PaperPuppet` |
| Evil Meowmeow | eyes narrow in a glare, teeth chatter | `PaperPuppet` |
| Hook shot | dirt puff at the burrow | `PaperPuppet` |
| Treasure | registered boil on threes; one light-catch star at a time | `PaperShadows`, `PaperFx` |
| Life / reel-in / smash | felt-heart burst / dust puff / paper crumbs | `PaperFx` |
| HUD numbers | the label stamps: big, smaller, settled | `PaperHud` |
| Titles appear (results, WHO ARE U) | stamped in: big, smaller, small overshoot, settled (unscaled time, so paused result screens still play it) | `PaperType` |
| Round won | cut-paper confetti falls over the whole screen (result screen included) and sways; unscaled twos | `PaperConfetti` |
| Life lost / gained | the felt pip over the head pops (big, smaller, settled) as it empties or fills | `PlayerLifeBar` |
| Block lands | a pop-up card shield (three nested paper half discs) pops open, overshoots, holds, folds flat | `BlockFlash` |
| Attack (Mr. Hyde) | a torn-paper slash sweeps round; it leaves in two held steps | `AttackSwing` |
| Skill cooldown / Hulk charge | the tint fills the felt body up to a torn-paper edge; the charge spreads as a hand-cut disc | `PlayerCharacter`, `RhinoController` |
| Hook aim (telegraph) | a running stitch of twisted thread, whole line shown at once; stitches march toward the target on twos | `TelegraphLine` |
| Scene change | felt curtain holds, dips, is pulled away | `PaperCurtain` |
| Start / Continue | the felt sticker peels off right to left (suggested by the team); the scene load waits for the 0.5 s peel | `MainMenu` |

`PaperPuppet` works as an additive pose layer. Before any script's `FixedUpdate` or `Update`, every part it posed
is put back exactly as it was. After every `LateUpdate`, it re-poses them for the picture. Gameplay and the
existing skill animations always read and write the true values. Only child art parts move, never a root or
anything with a collider.
