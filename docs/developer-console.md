# Developer console

Press the backquote/tilde key (below Esc on most keyboards) to toggle the console in any scene. Type `unlock` and press Enter. Esc closes it too. Gameplay pauses and game input is suppressed while typing; closing restores the previous pause state.

`unlock` skips the initial tutorial, opens the world map, and saves a global unlock override. It bypasses map prerequisites, coming-soon feature locks, paid character locks, and block/rope-cut access. The two hidden characters (Scrat and Rero Rero) remain masked, locked and unavailable to select or purchase, including with old saved unlock flags. It reveals portals immediately without marking every level as won. New Game clears the override. It does not grant currency, purchase store stock, remove cooldowns, or create unfinished content.

`pass` wins the active level or tutorial, collects all remaining treasure (including treasure carried by hooks), and banks the full collected amount once. It immediately follows the normal progression to the next tutorial or back to the map. It does nothing on the map, in menus/store, or after a round has ended.

`good <item_name>` grants a store item for free using the same effect rules as a purchase. Names are case insensitive; spaces, underscores, and hyphens are accepted between words: `good Swift`, `good Ration`, `good Shield`, `good Life`, and `good Time Machine` (also `good Time_Machine` or `good TimeMachine`). Internal item IDs such as `brace` and `time_machine` also work. In a playing level the effect activates immediately; on the world map, in tutorials, or outside a round it arms for the next playable level. The feedback states whether the item is active, reserved for the next level, or added to spare stock. Repeated grants follow normal purchase behavior (spare stock if the effect is already active/armed). This does not spend currency or change store stock.

`money <amount>` adds a whole-number amount to the saved wallet, for example `money 1000`. Negative amounts subtract money; `money 0` shows the current wallet without changing it. Missing/invalid amounts and changes that would overflow the wallet are rejected. This changes the spendable wallet rather than the current round's collected treasure score.

`checkmode True` enables **Space** pause/continue during active gameplay, including tutorials; `checkmode False` disables it and releases any checkmode pause. The argument is required and case insensitive; repeating the same value keeps that setting. Its current status appears in blue in the console as `checkmode: TRUE` or `checkmode: FALSE`. It defaults to `FALSE` each game session and keeps its setting between scenes. Close the console before using Space. Pausing adds no text or overlay to the game screen. Other pause controls stay available. Changing scenes clears the pause.

Press **Tab** to complete an unambiguous command/item prefix: `good ti` becomes `good Time_Machine`, `ch` becomes `checkmode `, and `checkmode t` becomes `checkmode True`. Ambiguous prefixes complete their shared prefix and show the matching choices without choosing one. Press Enter to execute. Spaces separate the command and its argument; multiword item names can use spaces or underscores.

## Adding future locks

All feature access decisions, including UI visibility/interactability and the action itself, must use `GameUnlocks.IsUnlocked(normalUnlockCondition)`. Example:

```csharp
public bool CanUseNewFeature => GameUnlocks.IsUnlocked(ProgressStore.IsCompleted("new_feature"));
```

This automatically honors `unlock`, including for features added after the override was saved. Do not read a saved lock flag directly in UI or action handlers. Keep resource costs, cooldowns, and missing-content checks separate from feature access. Normal completion history remains available through `ProgressStore.IsCompleted`.

