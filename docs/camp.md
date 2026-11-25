# The camp: the new Pick Character

![camp sheet](images/camp/camp_design_sheet.png)

The world map's **Pick Character** node no longer opens the old "WHO ARE U" screen. It opens the
**camp**, a safe cave in the mine where the heroes live. You walk in as your current hero, meet
the others at their homes, talk to them, and swap to them, hire them, or hear that they are not
ready yet. There are no hooks, no clock and no run record here.

## Layout

`Assets/Resources/Hub/camp.json` is a "grid" level (like the world map) with `"camp": 1`:

- **Size:** 36 x 20 tiles at tile 1.0, about two screens each way. The camera shows one screen and
  follows you inside the cave, as Level 4 (the roam map) does.
- **Scale:** heroes stand at their battle sizes (each prefab's own scale, Mr. Hyde at 0.8), so they
  sit in a place that fits them instead of a screen-sized box.
- **Centre:** a campfire plaza, framed by four small rock outcrops.
- **Gate:** the hatch "Back to the Map" sits in the west notch, and you arrive just inside it.
- **Hulk's cave** is a real walled chamber in the south-east, with its mouth toward the fire.
- The walls are marked as stones Hulk can't break, so he can try his charge without wrecking the camp.

Each hero has a home that suits them, painted in the keyframe's cut-paper style (Meshy):

| Hero | Home | State on main today |
| --- | --- | --- |
| Mr. Hyde | mud wallow, hay bale, pink pillow | unlocked |
| JK. Rolling | cardboard box bed, ball of yarn | unlocked |
| Scrat | hollow stump, pile of acorns | not released: "Coming soon" |
| Rero Rero | lily pond with reeds | not released: "Coming soon" |
| Elsa | igloo on a patch of snow | for hire, $3,000 |
| Hulk | rocky cave mouth | for hire, $1,500 |

Your own home says "Your home". The others stand in front of theirs as friends: they sway on
the shared stop-motion beat and turn to face you. A hero for hire is a little dimmed, and an
unreleased one dimmer still. Name tags and states sit under them on the usual felt tags.

## What you can do

Walk up to a friend, within about 2 units, and a felt card opens on the side of the screen away from them:

- **[E] Talk:** three lines each, in character, cycling. Each line hints at the hero's real skill
  (block, roll, firm tail, tongue, frost breath, charge).
- **[Enter] / the card's button:**
  - unlocked hero: **play as them**. The camp re-opens after a short fade, with you standing at
    their home and your previous hero now at theirs.
  - for hire: **hire them** for the price, paid from the wallet (`CharacterUnlocks.TryPurchase`).
    The top bar shows the wallet.
  - not released: "not ready to join yet". You can still chat.
- **The hatch:** back to the map. It completes the Pick Character node, as picking did on the old
  screen. Leaving without a pick counts as picking whoever you are.

Picking keeps the old screen's rules: `ChampionSpawner.Chosen`, a fresh record that keeps store
goods, the save (`ProgressStore.SelectedCharacter`), analytics, and the tutorial events
`RolePickOpen`, `CharacterPicked` and `Picked(i)`.

## Code

- `CampHub.cs` builds the camp and runs it.
  - **Friends:** each is the playable prefab, instantiated while inactive and stripped of every
    script, collider and rigidbody before it wakes, so nothing can mistake it for the player. It
    then gets a small static body. The squirrel keeps `SquirrelTailHeading`, with simulation off,
    so its tail stays in its standing pose.
  - **Homes:** each has a solid footprint.
- `LevelLoader.SetUpHub` adds `CampHub` instead of `MapHub` for a camp level, and skips the world
  map's saved spot there.
- `GameFlow` opens the camp for the Pick Character node, and the old Select scene is the fallback
  when `Hub/camp` is missing. `GameFlow.InHub` now includes the camp, so the HUD treats it as a
  hub: no clock, the wallet on the bar.
- Art: `tools/art/make_camp.py` builds `Art/Camp/Campfire_1..3` (the fire flickers on the boil
  clock and casts a lamp pool) and `Art/Camp/Home_*` from `docs/images/camp/`.
- Capture: `tools/art_capture/capture.ps1 -Scenes "camp,camp:talk,camp:hire,camp:soon,camp:switch,camp:gate"`.

Checked in real builds: arriving, talking, the hire card, the coming-soon card, switching
(the camp re-opens as the new hero) and the hatch (back to the world map as the new hero).
Not exercised: a hire that succeeds, because the capture wallet held $300.
