# Production log (TapNow)

Canvas project: `dea94f19-2166-4c3f-8683-d9ccd15584c7` · https://app.tapnow.ai/canvas/dea94f19-2166-4c3f-8683-d9ccd15584c7
Workspace: LA AI Filmathon Team 13 (start balance 7,759 Tapies). Review copies: `E:\Create with code\_claude_scratch\film\assets`.

## Pipeline
1. Style frame (nano-banana-pro) → locked.
2. Assets: Midjourney v8.2 HD (4 candidates each), best picked; off-model bits cropped; shop redone in nano-banana-pro (MJ added inset panels).
3. Keyframe per shot: nano-banana-pro 2K 16:9, references = locked assets.
4. Video: Seedance 2.5 draft (480p) per shot from the keyframe → final with draft_id (1080p).
5. Voices: create_audio speech (elevenlabs-v3); music (sonilo); edit + mix in ffmpeg.

## Locked assets (image_id)
| Asset | image_id |
|---|---|
| Style frame B | bf8d8700-d739-4c46-b1c1-8800e4b71dee |
| Pig (Hyde) sheet | 1fe11500-bdef-4a1b-b2eb-c6d82538bf6d |
| Evil Meowmeow sheet | f96875ad-04d2-417a-9d4f-744dc0fbd8fa |
| J.K. Rolling sheet | 64118c7d-1ada-48d9-ae6f-c6a812b4349c |
| Cat miner + cart + hook | ffd382a2-77b0-4d92-ab40-ffb144ccc39a |
| Cat-face mountain | 53695607-f122-4d92-9719-ba6f7739d6f6 |
| Camp + for-hire crew | 2d7c7441-d00a-46dd-9107-222faade936a |
| Shop interior | (v2 node 453c78cd-944f-4da0-8825-04455931479e) |
| Game refs | characters c9f7ee9d-…, keyframe 73691997-…, screens 75824c70-… |

## Film 1 keyframe nodes
S1 1a236f5e-4de2-46ba-a2ca-89450dd36a25 · S2 a80e908d-b5c9-42ce-94cf-babc0a2395ed · S3 a1ccd315-bdae-4a15-a0a0-1c763020b3c9 · S4 07f5c94d-cfbc-40a1-be37-c6e8d433bb41 · S5 1abfefeb-49a0-4b80-a842-7b416f3d5df6

## Shots used (Seedance 2.5 draft → 1080p final via draft_id)
| Shot | Keyframe image_id | Draft node | Final node |
|---|---|---|---|
| F1-S1 pig gulps gold (5 s) | ea077bea-… | 6e47c3d8-18ae-4b5e-b11a-eca148bfb388 | d25552d5-2d87-4fa6-aeb1-e73aca4ade7b |
| F1-S2 hook slams (4 s, jump cut 2.35→2.75) | 82a9f096-… | 14433c03-f886-4e13-8701-3115be568251 (v2) | ce1a7c54-f6e7-4201-9ac6-1ad6ad5fc461 |
| F1-S3 mountain is a cat (5 s) | 620a9ad3-… (v3B, bars removed) | 8f91e9e8-acd4-4196-ab61-1dab1a04dcf7 (v2) | 6df99e06-b523-4fb5-8512-f649d377f21b |
| F1-S4 Meowmeow taunts (8 s, played at 1.3x length) | c31dc5f0-… | ca2b311a-ab62-429a-b876-eeedf7d9d18c | 3ec1585c-6f57-41ed-bf7b-c6d86f57be26 |
| F1-S5 camp, Rolling joins (8 s) | 4db0c171-… (v2B, crew dimmed) | 5c493fea-6fa5-4cd9-b8b3-4e4f6e704506 | 5f085483-78e4-4edb-9dea-d8def41e73c1 |
| F2-S1 pig enters shop (4 s) | 64465fa1-… | e83dc84b-bbbc-4792-9256-a2b724cc2c53 | 328770f0-8d56-447e-8c98-aa251822b159 |
| F2-S2 shopkeeper welcome (6 s, 1.15x) | 7ec51e31-… | 502fa896-1fe6-4d4a-8aa2-096293caa7dc | e5693720-9ef1-44d0-8473-8d9b791d888e |
| F2-S3 hiccup coins (6 s) | d29ea96d-… | 46d73696-d2d3-4627-9615-c83aee91e12a | 54a47c11-cf0d-4de1-a78d-88afcdb316dc |
| F2-S4 greedy scoop (6 s) | f9a8be49-… | d27d8139-2f13-45f9-a600-6df4cd12df65 | d5a8ce6f-a702-4086-88fc-0c45ed0dd06f |

Rejected takes: F1-S2 v1 (hook never slams), F1-S3 v1 (no crane, carts melted, pig turned into a dark cat silhouette), F1-S5 v1 key (crew not dimmed), MJ shop (inset panels), F2-S3 key B (shopkeeper became a tabby).

## Sound
- Voices, elevenlabs-v3: Evil Meowmeow = harry, Pig (male) = will, J.K. Rolling = sarah. Shot sound effects come from Seedance; no generated speech in shots.
- Score, sonilo: F1 "The Mountain Is a Cat" (32 s), F2 "Gold Back to the Cat" (24 s, sly jazz).
- Mix: `_claude_scratch/film/edit.py` (ffmpeg 3.4): score ducked under voices (sidechain), shot SFX at 0.7, loudnorm −16 LUFS, fades.

## Delivered
- `Assets/StreamingAssets/Films/map_intro.mp4`: Film 1, 32.3 s, 1920x1080 24 fps H.264/AAC, 11.8 MB.
- `Assets/StreamingAssets/Films/store_intro.mp4`: Film 2, 23.1 s, 6.8 MB.
- Spend: 7,759 → 619 Tapies left (7,140 used; 1080p finals ≈ 100 Tapies/s).
