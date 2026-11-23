# Art style: Papercraft Gold Rush

Gold Miner looks like a handmade diorama seen from above. Everything is layered cut paper and wool felt, with
stitching, soft contact shadows and visible fibre. The floor is a dark, warm mine. Gold is the brightest, glossiest
thing on screen.

![key frame](images/art-keyframe.jpg)

## Rules

1. **Readable first.** The floor is dark and low-contrast, and play pieces are lighter and saturated. A piece must
   read at 40 px on screen. No busy patterns under the playfield.
2. **One light.** Flat light from the top left, with a darker cut edge on the bottom right of shapes.
3. **Tints can be gameplay.** Some colours are rules, not decoration:
   - a green rock (`MapHub.GreenRock`) is rhino-proof;
   - map wall colours mark the mode areas.

   Anything whose colour code reads (`Rock`, `MapHub`) keeps its tint. Its art is light greyscale paper so the
   tint still colours it. Only art whose tint means nothing is shown in its own colours (`ArtLibrary.ApplyPainted`).
4. **Same footprint.** A new sprite covers exactly the area of the shape it replaces: 256 px = 1 unit, centred,
   edge to edge. Colliders, scales and gameplay components are never changed for looks.
5. **Art never changes gameplay.** No changes to rules, skills, timing, hitboxes or balance. If an effect has to
   hook into gameplay code, it only reads state, and it falls back to the old look when the art is missing.

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

The primitive shapes were repainted in place with `tools/art/paperize.py`. Their files, alpha shapes and physics
outlines are unchanged, so every shape-built character, rock and map tile picks up the paper look automatically.

## Parked for the animation pass

These ideas are worth doing, but they belong in a dedicated animation pass, after researching how stop-motion
animators actually do them. That means holds, animating on twos, replacement animation, anticipation and
settle. Random jitter isn't enough.

- **Treasure "boil":** a first try with random sprite jitter was removed because it did not read as stop motion.
- **Glints on gold and gems.**
- **Paper-curtain scene reveal:** a torn felt sheet lifted away in stepped frames.
