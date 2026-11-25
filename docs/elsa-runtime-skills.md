# Elsa's Hierarchy / Inspector controls

1. Enter Play Mode and select the spawned **Penguin** (Elsa) character in the **Hierarchy**.
2. In the **Inspector**, open **Runtime Skill Slot**. Change **K Skill** or clear it to remove the skill. **Restore default skill** restores the external loadout's choice.
3. Under **Live skill settings**, edit duration, cooldown, breath length, half-arc and mouth opening. Ice color, local position and local scale are below those settings. These values update the current character live.

There is no settings panel or F2 shortcut in the game. Unity discards live component edits when Play Mode stops.

**Skill Duration** controls the major breath wave (default 1.5 seconds). Elsa can move and turn throughout the cast; the breath follows her mouth and facing. Cooldown starts when this wave ends, defaults to **5 seconds**, and ignores level cooldown multipliers. **After Effect Duration** controls the trailing particles (default 0.5 seconds). During those particles cooldown is already running and no new frost is applied. The particles remain at the cast location.

For permanent skill defaults, edit `Assets/Resources/Skills/ColdBreath.asset` outside Play Mode. To change Elsa's default K skill, edit the `elsa` entry in `Assets/Resources/CharacterSkillLoadout.asset`. Ice geometry remains artwork and can be adjusted on the character's `Artwork / Blue Tongue` object.

The character prefab holds movement and artwork. `RuntimeSkillSlot` creates the selected skill component at runtime; no skill implementation is attached to the prefab. `ElsaColdBreathSkill` owns timing and effects, and `ElsaMouthPose` handles artwork. Future skills can derive from `CharacterSkillDefinition` and `CharacterSkill`. Other characters still use their existing controllers.
