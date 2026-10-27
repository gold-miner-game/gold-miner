# Progress Report 10/15

## Game Mechanics
- With WASD as moving
- Gain more and more gold or dimond for money
- Win by gaining more gold than miner
- Using "Skiils" to survive


## Game Genre Design
- We are currently move from level based to Rogue Like with store
- Notice, one has to going through several tutorial level to understand the basic skills
- Notice that one might change the skills, for example: rush(cat), hook(frog).
- Notice that, one might create a kind of survival mode for stay alive while keeping gold for as long as possible
- Addition to that, one can spend money on tools that is easily consumed. Such as life, shield, bomb, and so on... 

## To-Do List
- Cross Level Status Recorder (Top Priority, the memory for player, Zhan Li, merge to main, 10/17/2026, this means a base data class, everyone can inherent)
- UI redesign (Doeun Kim 10/17/2026)
- First design a linear like tutorial (Penny 10/17/2026)
- The menu for choosing different level (Penny 10/17/2026)
- The store for buying (Zhan Li, 10/18/2026) 
- Different skill set attach to new characters (Yukun Li 10/17/2026)
- Survival Mode (Butian Xiong 10/17/2026)

<img width="1696" height="1498" alt="image" src="https://github.com/user-attachments/assets/4d8e2c8d-f9e0-4f3a-9d52-e244e2fe6d73" />
Map Design Concepts

<img width="2182" height="1514" alt="image" src="https://github.com/user-attachments/assets/07d28617-8d5d-457f-8f44-8f76a9d9b8cb" />
UI Redesign

## Current Status
![Level 4 in play](figures/example_level.png)

A snapshot of level 4, a few seconds after the round started.

> The screenshot predates the HUD redesign (PRs #15 and #19), so it still shows the old money bar and the bottom-left dev label. The tables below describe the current HUD.

The screen is split in three: a **top bar** (8% of the height), a **sidebar** on the right (14.3% of the width), and the **arena** in the rest. The camera fits the whole arena into that space, so the HUD never covers a hook or a treasure.

#### Top bar: this round

| Where | What it shows |
|---|---|
| Left | **Level**: the map node's name (e.g. `LEVEL 4`) when the round was opened from the World Map, or `LV 4/10` when the Game scene is played in the editor (N / P / R load the next or previous level, or restart). |
| Centre | **`$0 VS $0`**: the pig's money on the left, the miners' on the right. The side ahead is bold and the side behind is dimmed. A label flashes gold and a `+$600` popup rises when money comes in. |
| Right | **Timer** (`1:27` of the 1:30 round). It turns red under 10 seconds and pulses every second under 5. |
| Next to the timer | **Pause**: freezes the round and opens a *Quit Game?* popup. *Cancel* resumes, *Quit* goes back to the World Map. Only when the round was opened from the map. |

#### Sidebar: the pig and the run

A section with nothing to show is hidden (the tutorial has no wallet or items, for example), and dividers only appear between visible sections.

| Section | What it shows |
|---|---|
| **LIFE** | `x3`: lives left. Every level starts with 3, and the hit that takes the last one ends the round. Flashes red on a hit, green on a Life pickup. Hidden on rounds without lives. |
| **WALLET** | The run's money, carried between levels (and saved) and spent in the Store. From Level 1 on, a win adds how much the pig beat the miner by, and a loss takes that same gap away, so it can go below $0 (`-$150`). |
| **SKILL** | The champion's Space skill (block, roll, tail, tongue): `Active` while it runs, then the seconds until it is ready again with a bar filling up, then `Ready`. |
| **DIGEST** | `Ready` when the pig can eat, else the seconds left of digesting, in the digest's green. |
| **ITEMS** | One row per Store item held (`Shield x1`), including one that is armed or running, or `None`. The items are Swift, Ration, Shield and Brace. |

When the round ends, a panel covers only the arena and shows **YOU WIN** or **YOU LOSE**, the reason, the final `Player $ vs Miner $` score (with *"the miner wins ties"* on a tie), and what happens next (*Press R to retry*, *Back to the map...*). The top bar and sidebar stay visible.

In the tutorial, the stage's guide is drawn at the top of the arena with its progress (e.g. `Blocked 2 / 3`), and shrinks to its first line after 8 seconds.

#### Arena

| Where | What it shows |
|---|---|
| Centre | **The pig** (the player, moved with W A S D). Pink means it can eat and block. It turns green while digesting and blue while blocking, and the color drains from the ears down as the cooldown runs out. |
| Yellow circles | **Gold**: small $50, medium $100, large $250. This level has 3 large, 1 medium and 2 small. |
| Light-blue shape (bottom left) | **Diamond**, $600: the most valuable item on the level. |
| White squares | **The miners**, one per hook. Each hook's rope starts at its miner. |
| Yellow lines with a claw | **Hooks in flight**. Yellow means the hook is going for treasure. A red hook is aimed at the pig. |
| Dotted line | **Predicted path of the next hook**, shown before it fires. Here, the miner at the bottom right is about to go for the diamond (yellow dots). Red dots mean the next shot aims at the pig, so move or block. |

Level 4 has 7 treasures and 3 hooks. The simulator balanced it so the pig wins about 57% of the time. There are 10 levels: 8 normal levels and 2 tutorials (eat, then block/steal).

All the treasure on this level is worth $1,550. The round ends when the time runs out, all the treasure is gone, or the pig loses its last life. The pig wins with more money than the miner.

### When a hook aims at the pig
![A miner aiming at the pig](figures/aiming_player.png)

Each shot has a 30% chance to aim at the pig instead of treasure (never twice in a row). The miner then shows its path as **red dots**: it is trying to kill the pig. Each hit costs a life, and the third one ends the round.

- **The aim is locked when the red dots appear.** The miner at the top aimed at the spot where the pig was, and the dots run a little past it. The hook will fly along that line no matter where the pig goes.
- **Two ways to survive:** move out of the red line before the hook fires, as the pig here has already started to do, or press **Space** as the hook arrives to block it (the pig turns blue and the hook bounces off).
- **Yellow dots** are ordinary shots at treasure. Here, the left miner is going for a large gold and the bottom-right miner for the diamond. They aren't aimed at the pig, but any hook that touches it still costs a life, so don't stand in a yellow path either. If a hook drags treasure past you on the way back, press Space to knock it loose and eat it.
