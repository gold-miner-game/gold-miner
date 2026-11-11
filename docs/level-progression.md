# Current level sequence

| Former name | Current name | Existing level asset | Stable save ID |
| --- | --- | --- | --- |
| Level 1 | Tutorial | level_03 | battle_03 |
| Level 2 | Level 1 | level_01 | battle_06 |
| Level 3 | Level 2 | level_02 | battle_02 |
| Level 4 | Level 3 | level_04 | battle_04 |

Finishing the tutorial returns to the hub, revealing the new Level 1 with the
existing portal spotlight animation. Character selection also opens then. Level 2
requires only winning Level 1. All level reveals depend on clearing the previous
level, with no margin or earnings requirements. Winning Level 2 opens the store.
Leaving the store reveals Level 3; buying an item is optional. The tutorial remains
replayable through its portal.

Internal level files and save IDs stay the same. Legacy tutorial completion and
completion of the former Level 1 both satisfy the tutorial prerequisite.

`LevelProgressionSmokeTest.Run` verifies the sequence, win-only unlocks,
eligibility for the reveal animation, and legacy saves. It restores the preference
keys it uses after testing.
