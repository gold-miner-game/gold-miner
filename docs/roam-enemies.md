# Roam level villains: the Evil Meowmeow's henchmen

![design sheet](images/roam-enemies/roam_enemies_design_sheet.png)

All three are felt Meowmeows from the same family as the miner cats (charcoal felt, dashed stitches,
triangle ears, glowing red or gold eyes), painted with Meshy image-to-image from `Miner/MinerGlare.png`
or `Miner/MinerEager.png`. They are art only: gameplay, colliders, sizes, speeds and timing stay as they are.

## Red Square: "Brawler Meowmeow" (built)

The Red Square rams the player, so it is drawn as a fighter rather than a block. It is a stocky,
wide-shouldered cat with big red felt boxing gloves, a red headband with flying tails, a scarred
cheek and a fang. A cream cut-paper margin keeps the charcoal felt readable on the dark mine floor.
The square's 1x1 box collider is unchanged; the figure is drawn about 1.5 world units across.

| Pose | When | Sprite |
| --- | --- | --- |
| Guard | asleep, standing, backing off after a hit | `Enemies/Brawler_Guard` |
| Walk | moving: tipped one way, Guard, tipped the other way, Guard, one picture per pose beat | `Brawler_Guard_w1/_w2` |
| Wind-up | hunting and within 1.8 units of the player: crouched, glove cocked back | `Brawler_Windup` |
| Punch | the frame a ram lands, held 0.3 s: glove thrust at the camera, paper speed strips | `Brawler_Punch` |
| Bonk | the ram was blocked (Mr. Hyde's block), or it is shoved by the pig's attack: dizzy, stars | `Brawler_Bonk` |

`PaperBrawler` swaps the square's own root sprite and its flipX, so it faces the player. Hits change the
picture at once; every other change waits for the next stop-motion pose. HookHealth's hit blink and
FrostBody2D's frost keep working because they read that same renderer. The only gameplay-file edits
are read-only state getters (`RedSquare.Rammed / Recovering / LastRamBlocked / Target`,
`RedEnemy.BeingPushed`) and the line that adds the art component.

Regenerate the sprites with `python tools/art/make_brawler.py` from the Meshy sources in
`docs/images/roam-enemies/brawler_*.png`. Check them in game with
`tools/art_capture/capture.ps1 -Scenes "level_roam:brawler" -Frames 24`. The `brawler` capture
action puts the player three steps from the nearest Red Square and wakes it.

## Red Round: "Burrow Bandit" (designed, parked, not built)

The walking red hook hole is a Meowmeow that digs its own shaft as it goes. It rises out of a round
mound of torn paper soil, wearing a red bandana, with glowing red eyes.
Poses: Dig/Walk (ducked, so only the ears and eyes show; torn-paper crumbs fly up and are left on the
floor), Aim (pops up with a red glare on the telegraph), Reel (straining on the twine), Hit/KO (X eyes,
felt fluff pops off, then the mound collapses).
Note from the game-scale mock-up: rust-red soil blends with red-tinted wall rocks. When this is built,
use umber soil and keep the red to a stitched felt ring, the bandana and the eyes.
Sources: `images/roam-enemies/red_round_*.png`.

## Gold Round: "Greedy Digger" (designed, parked, not built)

The gold-collecting walker is the same burrowing cat in a gold-sand mound. It has gold-coin eyes with
stitched dollar signs, a greedy grin and a burlap sack, a cousin of the Greedy Meowmeow.
Poses: Dig/Walk (ducked, gold crumbs fly), Aim (eyes on the gold), Bank (hugs the nugget, sparkle eyes).
Sources: `images/roam-enemies/gold_round_*.png`.
