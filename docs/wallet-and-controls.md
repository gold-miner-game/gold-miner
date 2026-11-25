# Wallet and movement

Won levels add the full collected player gold to the shared wallet once per
round. Tutorials and replays also pay on a win. Lost rounds pay nothing and do not
deduct existing savings, including survival runs that end in death. Leaving an
endless survival run while still alive keeps its existing cash-out behavior. The miner's
score and previous best margins do not reduce the payout. The level settlement and
map transition share a guard on `GameManager` so they cannot credit the same round
twice. Maze gold continues to credit on pickup; it is not credited again at exit.

All four characters accept WASD and the arrow keys through the same movement
reader. Matching keys, such as D and Right Arrow, count as one direction. Opposing
directions cancel; diagonal movement keeps the existing normalization.

`WalletAndMovementSmokeTest.Run` checks full payouts, duplicate-call protection,
replays, zero payouts on losses and survival death, persistence, and the real input reader on all six character
prefabs. It restores the preference and input state used by the check.
