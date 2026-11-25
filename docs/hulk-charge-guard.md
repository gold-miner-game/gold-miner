# Hulk charge guard

K keeps the accelerating charge for **0.8 seconds**, followed by a **5-second base
cooldown**. Level 4 and Survival use the normal cooldown multiplier of 1.

`HulkChargeGuard` protects a **360-degree circle** from the instant K activates
until the charge expires. Rock-breaking attacks use a separate **front 60-degree
sector**. Both follow the collider centre; the attack follows charge direction.
Its radius is **0.75 times the largest world-space body collider dimension**:
the default Rhino prefab is approximately **0.646 world units**. Scaling the
character also scales both ranges. Side and rear claws and contact attacks are
protected too. A pale cyan circle shows protection, while a gold front sector
shows the attack. Line widths keep a minimum screen-pixel thickness for the
following camera, without increasing either gameplay range.

- Claws entering the circle retract empty and drop any carried treasure. Swept
  tests prevent fast claws from skipping the guard. Both hook damage entry points
  also check the guard before removing HP. K does not cut distant ropes; J remains
  independent during the charge and cooldown.
- Mobile enemies touching the circle are pushed outward **2 world units over
  0.2 seconds**, at most once per charge, without damage to their HP. Walls can
  stop knockback. The contact attack entry point checks protection before HP
  loss. Fixed hook spawns do not move. Each repelled enemy produces the same
  shield impact flash as a blocked claw, once per charge; this feedback is shared
  by regular levels, Level 4 and Survival and requires no store item.
- Ordinary rocks in the front 60-degree attack immediately lose collision and shatter. Frozen rocks lose ice
  first, then shatter. Green barriers protecting locked regions stay intact.
  Solid walls prevent the guard from affecting rocks or enemies behind them.

Run `CombatGuardPlayTest.Run`, `HulkChargePlayTest.Run`, `ElsaSkillPlayTest.Run`
and `FrostFieldPlayTest.Run` in an isolated Unity editor project to verify the
sector, actual knockback, damage order, frozen enemies and shared skill behavior.
