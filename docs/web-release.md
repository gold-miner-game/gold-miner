# Browser release

The game is served at https://gold-miner-game.github.io/gold-miner/ from the
repository's `gh-pages` branch. The `gold-process` tag identifies the game source.
Each browser build includes `build-info.json` with its matching source commit.

`WebGLBuild.Run` builds every enabled scene, starting with Boot. It uses the
responsive GoldMiner template, hashed filenames, no asset compression, and no
threads, so GitHub Pages can serve the game without special response headers.
The build also works as a static HTML game archive for other hosts.

Run Unity 6000.3.22f1 with its Web build module installed:

```text
Unity.exe -batchmode -buildTarget WebGL -projectPath <isolated-project> -executeMethod WebGLBuild.Run -logFile <build.log>
```

Set `GOLD_MINER_WEB_OUTPUT` to the output directory and `GOLD_MINER_REVISION` to
the source commit. Use an isolated checkout so a build cannot disrupt an open
editor or pick up later, uncommitted edits. Serve the output over HTTP for local
browser checks; opening `index.html` directly from disk does not load WebAssembly.

After verification, publish the output at the root of `gh-pages`, retaining
`.nojekyll`. Commit and tag the matching source separately on `main`.
