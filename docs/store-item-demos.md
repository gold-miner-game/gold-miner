# Store hover demos

Each hover shows two separate tips: a square 4.5-second, 20 fps recording above
the item, and its name and description below the item. Leaving the
item stops playback; returning restarts it. The player cannot interact with the
recorded scene, and playback does not touch the real game's state.

The clips use the actual `Teams/MrHyde`, `Object/Gold_Small`, and
`Resources/Prefabs/Miner` template together with the real `HookHole.Build` setup
used by compete levels. `StoreDemoCaptureDriver` builds a real scene,
feeds keyboard movement through the Input System, activates the actual
`CrossLevelStatus` items, and records their normal gameplay scripts. Speed and
digest settings come from `level_01`, including the game's 0.8 speed multiplier.
Shield and Brace demonstrations use actual hook collisions and `PlayerLifeBar`;
Time Machine uses `TimeMovesWithPlayer`. Artwork is not redrawn for the demos.

The 90 frames are packed into each 10-by-9 PNG atlas under
`Assets/Resources/StoreDemos`. `StoreItemDemo` displays the appropriate atlas cell
using unscaled playback time, so tutorial pauses cannot freeze the preview.

## Regenerate after prefab or behavior changes

Run `tools/store_demos/record.ps1 -UnityEditor <path-to-Unity.exe>`.
It makes a disposable project in the system temporary directory, gives it separate
save settings, and disables its analytics endpoint before opening Unity. It does
not alter the active editor scene or real save. The capture project is retained
for inspection, and the resulting PNGs and behavior reports are written to
`Temp/StoreDemoCaptures`. Inspect them, then copy the PNGs over the matching
`Assets/Resources/StoreDemos` files, retaining the existing import settings.

The recorder is editor-only and is excluded from game builds. The recorded demo
duration does not change the duration of the actual store effects.
