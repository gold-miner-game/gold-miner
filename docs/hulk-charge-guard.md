# Hulk charge guard

K keeps the accelerating charge for **0.8 seconds**, followed by a **5-second base
cooldown**. Existing level cooldown multipliers still apply to Hulk.

`HulkChargeGuard` protects a **front 60-degree sector** from the instant K activates
until the charge expires. It follows the collider centre and charge direction.
Its radius is **0.75 times the largest world-space body collider dimension**:
the default Rhino prefab is approximately **0.646 world units**. Scaling the
character also scales the sector. Side and rear attacks remain dangerous.
The pale outline uses the same centre, angle and radius as the gameplay checks.

- Claws entering the sector retract empty and drop any carried treasure. Swept
  tests prevent fast claws from skipping the guard. Both hook damage entry points
  also check the guard before removing HP. K does not cut distant ropes; J remains
  independent during the charge and cooldown.
- Mobile enemies touching the sector are pushed outward **2 world units over
  0.2 seconds**, at most once per charge, without damage to their HP. Walls can
  stop knockback. The contact attack entry point checks protection before HP
  loss. Fixed hook spawns do not move.
- Ordinary rocks immediately lose collision and shatter. Frozen rocks lose ice
  first, then shatter. Green barriers protecting locked regions stay intact.
  Solid walls prevent the guard from affecting rocks or enemies behind them.

Run `CombatGuardPlayTest.Run`, `HulkChargePlayTest.Run`, `ElsaSkillPlayTest.Run`
and `FrostFieldPlayTest.Run` in an isolated Unity editor project to verify the
sector, actual knockback, damage order, frozen enemies and shared skill behavior.
