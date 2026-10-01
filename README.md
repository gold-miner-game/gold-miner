# Gold Miner (Reverse)

A 2D Unity game with a twist on the classic Gold Miner: the miner fires its hook on its own,
and **you** play a small character on the field. Your job is to help the miner hit its target
score before time runs out by dragging minerals into the hook's path. Just don't get caught
by the hook yourself.

## Requirements

- Unity **6000.3.22f1** (Unity 6.3)
- Input System package (already included in `Packages/manifest.json`)

Open the project in Unity Hub, load `Assets/Scenes/SampleScene.unity`, and press Play.

## Controls

| Key | Action |
|---|---|
| W / A / S / D | Move (8 directions) |
| Hold Space | Grab the mineral you are touching and drag it. Release to let go |
| Enter | Go to the next level (after a level is cleared) |
| R | Retry the level (after losing) |

## Rules

- The miner shoots its hook in a random downward direction every second. The hook catches the
  first mineral it touches and pulls it back. Heavier minerals come back slower.
- Each mineral the miner brings back adds to its score.
- **Win:** when the timer reaches 0, the miner's score is at or above the target, and you were
  never caught. Reaching the target early does **not** end the level, so you can keep helping
  the miner score more.
- **Lose:** the hook catches you (GAME OVER), or time runs out below the target (TIME UP).

### Dragging and pushing

- You can only grab a mineral you are touching (within `grabRange`).
- Speed while dragging = `inputForce / total weight`, capped at your normal move speed.
- A dragged mineral pushes any other minerals it runs into, and those push the next ones in a
  chain. The whole chain stops when it hits something that can't be pushed. Minerals never
  overlap.
- With empty hands you can't push minerals. You bump into them and stop.

### Scoring

Mineral value and weight both scale with size:

```
volume = scale.x * scale.y
value  = valuePerVolume * volume
weight = densityPerVolume * volume
```

| Resource | valuePerVolume | densityPerVolume | Scale in scene | Value | Weight |
|---|---|---|---|---|---|
| Gold | 100 | 30 | 1 x 1 | 100 | 30 |
| Rock | 10 | 20 | 1 x 1 (x2) | 10 each | 20 each |
| Diamond | 2000 | 1 | 0.3 x 0.3 | 180 | 0.09 |

That makes 300 points on the field in total.

### Levels

| Level | Target | Time |
|---|---|---|
| 1 | 200 | 60 s |
| 2 | 250 | 50 s |
| 3 | 280 | 45 s |

"Next level" reloads the same scene with the next target and time limit. After the last level,
Enter starts again from level 1. You can edit the levels on the `LevelManager` object in the
Inspector.

## Project structure

```
Assets/
  Scenes/SampleScene.unity     Main scene
  prefebs/                     MinerHook and fetchable prefabs
  scripts/
    Level/LevelManager.cs      Score, timer, win/lose, level switching, HUD (OnGUI)
    miner/MinerHook.cs         Hook state machine: wait -> shoot -> retract
    miner/HookCatcher.cs       Trigger on the hook head; reports minerals and the player it touches
    objects/object.cs          GrabbableObject: value/weight from size, follows the hook when caught
    objects/minerals.cs        ResourceType ScriptableObject (Gold / Rock / Diamond assets)
    Player/player.cs           WASD movement via Rigidbody2D, flips when turning left/right
    Player/fetch.cs            Hold Space to drag minerals, chain-pushing, collision checks
    Player/die.cs              Freezes the player when caught by the hook
```

## Notes

- The HUD and end-of-level messages are drawn with `OnGUI` on purpose, to keep the MVP light.
  There is no Canvas UI yet.
- Make sure the target score for each level is reachable with the minerals placed in the scene.
