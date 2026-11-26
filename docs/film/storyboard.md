# Gold Miner: in-game screenings

Director: Claude · Branch: `film/screening` · Made in TapNow (LA AI Filmathon Team 13 workspace)

Two films replace the only Boot-reachable story dialogues: the world map's first-visit Story and the store's first-visit StoreIntro. The plot comes from how the game plays, not from the old dialogue lines.

## Look
- Papercraft Gold Rush: cut paper + felt, stitched edges, stop-motion timing (12 fps feel), warm lantern light in a brown cave.
- Characters follow the in-game figures (docs/images/art-characters.jpg) so players recognise them:
  - **Pig** (the hero you play first): round pink felt pig, black bead eyes, snout.
  - **Evil Meowmeow**: black felt cat, red triangle eyes, jagged red zigzag grin, curled tail.
  - **Cat miners**: little black cats in wooden mine carts with brass hooks on ropes.
  - **J.K. Rolling** (the only hero playable at the start besides the pig): round cream felt cat, whiskers, pink ears; she rolls.
  - **For hire at camp** (locked at first, bought with gold): Scrat the squirrel with acorn, Rero Rero the green frog, Elsa the penguin with blue scarf, Hulk the blue rhino.
- 16:9, 1920x1080 master (the web build plays it at 960x540). English voices, new music and sound.

## Voices (from the camp dialogue's personalities)
- Pig (Hyde): earnest, kind, a little self-forgetting; soft young voice.
- J.K. Rolling: proud, competitive, cares but won't admit it; bright, sharp young voice.
- Evil Meowmeow: theatrical, greedy, purring showman (he has his own jazz theme); smooth, sly voice.

## What the game is, as a story
Evil Meowmeow owns the mountain. His cat miners sit in mine carts and fish for gold with hooks. The pig doesn't mine; it eats the gold right off the floor, so the hooks come for the pig instead. The world map is the cat's own face carved in rock, with portals for levels. The pig can't win alone: J.K. Rolling joins at camp, the other heroes are for hire once you have the gold, and the cat even runs the store, selling gear for the gold you earn.

### Film 1: "The Mountain Is a Cat" (world map, first visit after the tutorial, ~25 s)
Plays instead of the hub's Story step. When it ends it fires `point_picking`, so the Pick Character pointer still runs.

| # | Shot | Sound / line |
|---|---|---|
| 1 | Pig waddles out of a tunnel, cheeks stuffed, swallows a gold nugget with a satisfied gulp. | Gulp, happy snort |
| 2 | A hook on a rope drops right behind the pig and misses; the pig scurries. | Hook whoosh, clank |
| 3 | Crane up: the cave's rocks form a huge cat face; the two eye-hollows glow green; cat miners in carts sit in the eyes. | Low rumble, music sting |
| 4 | Evil Meowmeow looms over the rock face, red eyes flaring, grin widening. The pig, tiny below, raises a trotter. | Evil Meowmeow (purring, theatrical): "Ohhh, a little piggy snacking on MY gold? Then I'll go fishing... for pork." · Pig (earnest, small): "I... could share?" |
| 5 | Torches light a camp. J.K. Rolling (the cream cat) rolls in like a ball, pops up beside the pig, chin high. Behind her, the squirrel, frog, penguin and rhino sit dim in the shadows, each with a little "For hire" paper tag. | J.K. Rolling (proud, pretending not to care): "Ugh, you're hopeless. Fine, I'll come. Not for you! The others? They'll want paying." |

### Film 2: "Gold Back to the Cat" (store, first visit, ~20 s)
Plays instead of the store's StoreIntro step. The store is usable when it ends.

| # | Shot | Sound / line |
|---|---|---|
| 1 | Door bell jingles; pig steps into a felt shop: paw-print wallpaper, bunting, candles, three goods on a shelf. | Bell |
| 2 | Evil Meowmeow pops up behind the counter in a tiny shopkeeper apron, rubbing his paws. | Evil Meowmeow (sing-song): "Welcome, welcome! Every coin in this mountain finds its way home... to ME." |
| 3 | Pig pats its round belly, hiccups, and coughs gold coins onto the counter. | Hiccup, coin clatter · Pig (apologetic): "Sorry, it's all I've got. It's, um... still warm." |
| 4 | The cat scoops them greedily; behind him the shelf goods glint and spin. | Evil Meowmeow (giggling): "Warm gold spends just the same. Shop till you drop, piggy!" |

## In game
- Played full screen through `StreamingAssets` + `VideoPlayer` (WebGL needs a URL). Skippable after a moment.
- Each film runs inside its existing TutorialStep, so "only first time", freezing and the events after it keep working.
