# Level Map & Tutorial Design / 关卡地图与教程设计

> Owner: Penny · Status: Alpha · Depends on: `CrossLevelStatus` (PR #11, merged)
>
> 负责人：Penny · 阶段：Alpha · 依赖：`CrossLevelStatus`（PR #11，已合并）

This document is the single source of truth for the level-select map and the linear
tutorial. Teammates building characters (Yukun), the store (Zhan Li), and survival mode
(Butian) only need the small contracts in sections 5–7.

本文档是**关卡选择地图**与**线性教程**的唯一事实来源。做角色（Yukun）、商店（Zhan Li）、
生存模式（Butian）的队友只需看第 5–7 节的小契约即可对接。

---

## 1. Goals / 目标

- A candy-crush-style **world map**: fixed icons on a path; clearing a node unlocks the next.
  一张糖果传奇式的**世界地图**：路径上有固定图标，通关一个节点解锁下一个。
- Flow: **Tutorial → Role Pick → Battles (rising difficulty) + Store**.
  流程：**教程 → 选角色 → 正式战斗（难度递增）+ 商店**。
- Built to **extend easily and split cleanly across teammates**; Alpha UI stays simple.
  设计目标是**易扩展、好分工**；Alpha 阶段 UI 保持简单。

---

## 2. Scene architecture / 场景结构

Multiple scenes, one owner each, so Unity `.unity` merge conflicts stay near zero.

多场景，每人一块，Unity 场景文件几乎不会合并冲突。

| Scene / 场景 | Owner / 负责人 | Role / 作用 |
|---|---|---|
| `Boot` | Penny | Create persistent singletons, then load `Map` / 建常驻单例后跳 `Map` |
| `Map` | Penny | Level-select map and the hub you return to / 关卡地图与返回大厅 |
| `RolePick` | Penny shell, Yukun fills | Pick a character, default pig / 选角色，默认猪 |
| `Game` (exists) | shared + Penny | Battles and tutorials via `LevelLoader` / 用 `LevelLoader` 跑战斗与教程 |
| `Store` | Zhan Li | Buy skills; reads `CrossLevelStatus.wallet` / 买技能，读钱包 |

Scenes never reference each other directly. All transitions go through the persistent
`GameFlow` singleton.

场景之间不互相硬引用，所有跳转统一走常驻的 `GameFlow` 单例。

---

## 3. Core pieces / 核心新物件 (Penny)

### 3.1 `GameFlow` — persistent singleton / 常驻单例

The traffic hub. Lives across scene loads (`DontDestroyOnLoad`). Holds the `WorldMap` and
the `ProgressStore`, remembers the selected node, and drives scene transitions.

交通枢纽。跨场景存活。持有 `WorldMap` 和 `ProgressStore`，记住当前选中节点，驱动场景切换。

```
EnterNode(MapNode node)   // load the right scene for node.type / 按类型加载场景
CompleteCurrentNode()     // mark complete, unlock next, return to Map / 标记通关、解锁、回地图
ReturnToMap()             // go back to Map without completing / 不通关直接回地图
```

### 3.2 `WorldMap` — ScriptableObject / 地图清单

An ordered list of nodes, editable in the Inspector. Adding a level = add a node + drop a
level JSON. No code change.

Inspector 里可编辑的有序节点列表。加一关 = 加一个节点 + 丢一个关卡 JSON，**无需改代码**。

```
MapNode:
  id            stable string, e.g. "tut_basics" / 稳定字符串
  type          Tutorial | RolePick | Battle | Store | (future Survival/Boss)
  levelAsset    TextAsset, for Tutorial/Battle only / 仅教程、战斗用
  displayName   label on the map / 地图上的名字
  mapPosition   icon position on the map (normalised) / 图标坐标（归一化）
  prerequisites parent node ids / 父节点 id（解锁前置）
```

Unlocking is a **tree**: a node is open when it has no parents (a start node), is already
cleared, or **any** of its `prerequisites` is cleared. "Any" is what lets one node branch into
two children (both list the same parent) and two paths merge back into one (one node lists two
parents). The map is drawn top-down, one row per depth, with edges from each parent's bottom to
each child's top so a line never crosses a label.

解锁是**树形**的：一个节点在「没有父节点（起点）」「自己已通关」或「**任意**一个 `prerequisites`
已通关」时开放。正因为是「任意」，所以一个节点可以**分叉**成两个子节点（两个子节点填同一个父
id），两条路也能**合流**回一个节点（一个节点填两个父 id）。地图自上而下绘制，一层一行，连线从
父节点底边连到子节点顶边，所以线不会穿过标签。

### 3.3 `ProgressStore` — PlayerPrefs wrapper / 存档

Meta progression that survives app restarts: which nodes are completed, and the chosen
character id.

跨程序重启的元进度：哪些节点已通关、选了哪个角色。

```
IsCompleted(id) / MarkCompleted(id)
SelectedCharacter / set SelectedCharacter
ResetAll()   // dev helper / 开发用重置
```

---

## 4. Two layers of state / 两层状态

Keep them separate or the roguelike + store logic gets tangled.

分清楚，否则 roguelike + 商店逻辑会乱。

| Layer / 层 | Lifetime / 生命周期 | Holds / 内容 | Who / 谁用 |
|---|---|---|---|
| `CrossLevelStatus` (PR #11) | one run, in memory / 一次 run，内存 | wallet, speed, digest, block, lives, skills | `GameManager`, `PlayerCharacter`, `LevelLoader`, Store |
| `ProgressStore` (new) | across app restarts / 跨重启 | completed nodes, character id | `GameFlow`, Map, RolePick |

---

## 5. Role pick contract / 选角色契约 (Yukun)

PR #12 landed the character system in `main`, so `RolePick` now rides on it instead of a
separate registry. Each character is a `PlayerCharacter` subclass (`MrHydeController`,
`CatController`, `SquirrelController`, `FrogController`) with its own `CrossLevelStatus`
subclass (`PigStatus`, `CatStatus`, …). `ChampionSpawner` holds the ordered list and swaps the
chosen prefab into the Game scene. Adding a character = a new controller + status + prefab, wired
into `ChampionSpawner` — no changes to the map code.

PR #12 已经把角色系统合进 `main`，所以 `RolePick` 直接复用它，而不再单独做注册表。每个角色是
`PlayerCharacter` 的子类（`MrHydeController`、`CatController`、`SquirrelController`、
`FrogController`），各自带一个 `CrossLevelStatus` 子类（`PigStatus`、`CatStatus`…）。
`ChampionSpawner` 持有有序列表，并把选中的 prefab 换进 Game 场景。加角色 = 新建控制器 + 状态 +
prefab，接到 `ChampionSpawner`，**不碰地图代码**。

```csharp
// RolePickController.Pick(index) — same order as ChampionSpawner
ChampionSpawner.Chosen = index;
CrossLevelStatus.StartFresh(ChampionSpawner.RecordType(index)); // fresh run record
ProgressStore.SelectedCharacter = index.ToString();
GameFlow.Instance.CompleteCurrentNode();
```

`RolePick` lists the champions from `ChampionSpawner` (default index 0 = Mr. Hyde, the pig).
Picking one starts a fresh run record and stores the index in `ProgressStore`.

`RolePick` 从 `ChampionSpawner` 列出角色（默认 index 0 = Mr. Hyde 猪）。选中后开一个全新的 run
记录，并把 index 存进 `ProgressStore`。

Role pick happens **once**, right after the tutorial.

角色在**教程之后选一次**，之后固定。

---

## 6. Store contract / 商店契约 (Zhan Li)

The `Store` node sits in the **top-left corner** of the map, off the level path (no connecting
line). It still unlocks from Pick Character (`prerequisites: [role_pick]`), so it opens as soon as
the player has a character and is never gated behind a battle — the player can drop in to spend
the wallet whenever. The store only needs to:

`Store` 节点单独放在地图**左上角**，不接在关卡线路上（没有连线）。解锁仍跟着选角色
（`prerequisites: [role_pick]`），选完角色就开放，**不卡在任何战斗关之后**，玩家随时可以进去花钱。商店只需做到：

1. Read / write `CrossLevelStatus.Active` — `getWallet()`, `addWallet()`, `addSkill()`.
   读写 `CrossLevelStatus.Active` 的钱包与技能。
2. On "Leave", call `GameFlow.Instance.ReturnToMap()`.
   点"离开"时调 `GameFlow.Instance.ReturnToMap()`。

A placeholder `Store` scene with a Leave button ships first; Zhan Li replaces its contents.

先放一个带"离开"按钮的占位 `Store` 场景，Zhan Li 之后替换内容即可。

---

## 7. Survival mode hook / 生存模式接入 (Butian)

Survival is a node after Level 8 (`id: survival`, `prerequisites: [battle_08]`). The map
already opens `Survival.unity`. That scene is a placeholder (`SurvivalPlaceholder`); replace
its contents with the real mode. On leave, call `GameFlow.Instance.ReturnToMap()`, or
`CompleteCurrentNode()` once a run can actually be won. The picked champion's record is
already active when the scene loads.

生存模式是 Level 8 后面的一个节点（`id: survival`，`prerequisites: [battle_08]`）。地图已经会打开
`Survival.unity`。这个场景现在是占位（`SurvivalPlaceholder`），把内容换成真正的模式即可。离开时调
`GameFlow.Instance.ReturnToMap()`；等一局真的能赢了再改调 `CompleteCurrentNode()`。进场景时，
选中角色的记录已经在了。

---

## 8. Linear tutorial / 线性教程

Three nodes at the front of the map, each teaching one thing, each gated by a goal that
must be met before continuing. The existing `LevelData.stages` + `goal` system is the base.

地图最前面三个节点，一步教一个机制，每步都要**达成目标**才能继续。地基是现有的
`LevelData.stages` + `goal` 系统。

| Node / 节点 | Teaches / 教什么 | How / 做法 |
|---|---|---|
| **1. Basics / 基础** | move + eat + **digest** | WASD to a few scattered golds; then two golds placed close so you **must wait out the green digest** before eating the second. A **"Digesting" label floats over the pig's head** (next to the existing green tint). / 先散放金子教移动与吃，再在近处放两个金子，吃完第一个**必须等绿色退完**才能吃第二个；猪头顶出现 **"Digesting" 标签**（与变绿并存） |
| **2. Dodge / 躲红钩** | dodge the red hook | miners aim only at the pig (red). Goal `survive`: stay alive N seconds. Getting hit loses a life and you retry — which teaches dodging. / 矿工只射红钩，目标 `survive`：存活 N 秒。被击中掉命重来，自然学会躲 |
| **3. Block & Steal / 格挡与打落** | Space to block + knock gold loose | reuse the existing `level_01b_block.json` (two internal stages: `block` then `steal`). Steal text stresses cause/effect: "Block a hook that's dragging gold home and the gold drops." / 复用现有 `level_01b_block.json`（内部两 stage：先 `block` 后 `steal`）。steal 文案强调因果 |

### Why digest was not landing / 消化之前没教会的原因

`level_00_eat.json` already said "green = digesting", but its golds were spread across the
map, so digestion always finished during travel and the player was **never actually
blocked**. The fix is a **close pair of golds that forces one real wait**, plus the
head label for instant feedback.

`level_00_eat.json` 本来就写了"绿色=消化"，但金子散落各处，玩家走过去的路上消化早就结束了，
**从来没真正被卡住**。修法是**近距离一对金子强制等一次** + 头顶标签即时反馈。

### New goal: `survive` / 新目标 `survive`

Added to `LevelData` and `LevelLoader` only (not Hook/MinerAI/GameManager, to keep
teammates' files untouched). `GoalCount("survive")` returns seconds elapsed in the stage;
the stage ends when `count` seconds pass.

只改 `LevelData` 与 `LevelLoader`（不动 Hook/MinerAI/GameManager，以免碰队友文件）。
`GoalCount("survive")` 返回本 stage 已过秒数，达到 `count` 秒即过关。

---

## 9. `LevelLoader` integration / 整合 (minimal / 最小改动)

- New static entry `LevelLoader.Requested` (a `TextAsset`): when set, the loader builds
  exactly that level instead of walking its index list. `GameFlow` sets it before loading
  the `Game` scene.
  新增静态入口 `LevelLoader.Requested`（一个 `TextAsset`）：设了就只加载这一关，不走索引列表。
  `GameFlow` 在加载 `Game` 场景前设置它。
- On a win, `GameFlow` sets `LevelLoader.WaitToAdvance = true` so the loader does **not**
  auto-advance to the next file; `GameFlow` marks the node complete and returns to the map
  after the result shows.
  通关时 `GameFlow` 设 `LevelLoader.WaitToAdvance = true`，让 loader **不**自动进下一个文件；
  由 `GameFlow` 标记通关并在结算后回地图。
- `N` / `P` / `R` dev shortcuts stay for in-editor testing.
  `N` / `P` / `R` 调试快捷键保留供编辑器内测试。

---

## 10. One full session / 一次完整流程

```
Boot → Map
  ├ Tutorial node 1 (Basics)  → Game → win → back to Map (unlock node 2)
  ├ Tutorial node 2 (Dodge)   → Game → win → back to Map
  ├ Tutorial node 3 (Block)   → Game → win → back to Map
  ├ RolePick node             → choose character → back to Map
  ├ Battle nodes (easy→hard)  → Game → win → back to Map
  └ Store node                → Store → spend wallet → back to Map
```

---

## 11. Division of labour / 分工

- **Penny**: `Boot`/`Map`/`RolePick` scenes, `GameFlow`, tree `WorldMap` SO + `WorldMapController`,
  `ProgressStore`, tutorial JSON, `survive` goal, Digesting label, `LevelLoader` entry.
- **Zhan Li**: `CrossLevelStatus` (done), `Store` scene (section 6 contract).
- **Yukun**: character subclasses via `ChampionSpawner` (section 5).
- **Butian**: survival mode as a new node type (section 7).

`GameFlow` and the `WorldMap` asset are the only shared surface, maintained by Penny.

`GameFlow` 和 `WorldMap` 资产是唯一的公共接口，由 Penny 守门。

---

## 12. Alpha notes / Alpha 说明

- Map icons, the role list, and the Digesting label all use plain primitives (colored
  circles / stars / text) via `OnGUI`, matching how `HUDController` and `LevelLoader`
  already draw. No art assets.
  地图图标、角色列表、Digesting 标签都用 `OnGUI` 的纯色图元/文字（与现有 `HUDController`、
  `LevelLoader` 画法一致），不碰美术资源。
- Scenes are generated by an Editor builder (`Tools > Gold Miner > Build Flow Scenes`),
  the same pattern as `GameSceneBuilder` and `HUDBuilder`.
  场景由 Editor 构建脚本（`Tools > Gold Miner > Build Flow Scenes`）生成，与
  `GameSceneBuilder`、`HUDBuilder` 同一套路。
