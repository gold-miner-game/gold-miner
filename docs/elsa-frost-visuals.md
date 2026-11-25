# Elsa frost

Elsa's cold breath applies the imported `FrostBody2D` artwork to any visible world
sprite or line that overlaps its cone. There is no enemy/treasure/rock type check;
new world objects using these renderers are picked up automatically. The caster's
own artwork and generated frost overlays are excluded.

The cone starts at the breath effect's mouth position and uses its live forward
direction, length, and half arc. Length is measured forward from the mouth. The
effect is clipped to small surface patches instead of tinting a whole object when
its center enters the cone. It also catches the middle of a rope whose endpoints
are outside the cone.

Each touched patch lasts for `FrostField2D.FrostDuration` (**2.5 seconds**) after its most recent exposure. Coverage
is stored in object/surface coordinates, so it travels with moving treasure or
hooks, and newly exposed patches do not refresh untouched patches. Game pauses
also pause expiration. The effect fades gently near the end of its lifetime.

## Frozen object behavior

- A frozen miner or survival hook controller cannot aim, fire, or move its hook.
  Windup and cooldown waits pause until the controller thaws. Ice on a rope or
  claw alone does not freeze its miner.
- Frozen mobile enemies (Red Square, Red Round and Gold Round) also stop moving.
  A frozen Red Square cannot inflict contact damage.
- Rope still pays out normally. During retraction, unfrozen rope reels in until
  the first icy section reaches the miner; reeling then waits for that section
  to thaw. Frozen rope material moves with paying out/reeling rather than stretching.
- A frozen claw drops anything it carries. Its next impact breaks the claw's ice
  and sends it home without collecting, damaging the player, or breaking a rock.
- Frozen treasure cannot be collected by ordinary characters, tongues, or hooks.
  Elsa can eat it normally. A hook striking frozen treasure breaks the ice and
  returns empty; a later shot may collect it.
- Frozen rocks stay solid and resist hook destruction. A hook impact removes their
  ice and returns without breaking the rock. Hulk's active guard breaks the ice
  and immediately smashes ordinary rocks; locked green barriers remain intact.
- Rope cutting remains available, including Elsa cutting frozen rope sections.

Ice breaks before normal collision/block reactions, so an ice-breaking impact
does not count as a successful player block or award treasure money. Continued
breath exposure can freeze a cleared patch again.

Hulk's charge guard can repel a frozen claw even while its controller is frozen.
The repelled hook drops its treasure and returns empty without waiting for its
rope or controller to thaw. The shooter itself remains frozen. A mobile enemy
repelled by the guard loses its ice first, then receives the physical knockback.

The source package was imported from `E:/wotr/ColdBreath2D_Import/FrostBody2D.unitypackage`
into `Assets/FrostBody2D`, preserving its material, noise texture, and GUIDs. A
resource prefab at `Assets/Resources/Effects/FrostBody2D.prefab` keeps the effect
available in player builds. The imported shader has been extended with a coverage
mask and native line support. The original standalone sprite effect still works
without a coverage mask.
