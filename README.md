# botato route lab

**[Open the navigation sandbox](https://lolstar123.github.io/botato-navigation/)** | [poetato.app](https://poetato.app)

Botato plans movement through obstructed terrain. The public route lab lets you change that terrain while a little red-capped meowl follows its route.

![Botato route lab](examples/portfolio/preview.png)

## Try it

Click an open destination. Switch to **draw rocks** and drag across the path. The agent replans immediately; if you seal the route, it stops and says why. Erase a passage and it can continue. Try the quarry, ravine and ruins, change the preferred wall clearance, inspect the search area and export a complete route with its obstacle map.

## The actual navigation idea

The [original C# router](reference/Pathfinder.cs) computes distance from walls, runs eight-direction A* with a penalty near them, then smooths the route with collision-checked shortcuts. The [original smoother](reference/PathSmoother.cs) also limits the cost increase from shortcuts.

The browser implementation follows those stages on an 80 x 50 terrain. It rejects diagonal corner cuts and refuses blocked or unreachable destinations. A requested clearance is a preference, so a necessary narrow corridor can still be used. Its search metric and smoothing budget are visible in the source. The C# reference files depend on the full desktop project; they are source references, not a standalone build.

The maps are authored sandbox terrains, not exported game maps. The browser has no game-memory, account or input-control integration.

## Run and test

```sh
python -m http.server 8000 --directory examples/portfolio
node --test examples/portfolio/model.test.mjs
pip install playwright
python -m playwright install chromium
python tools/browser_audit.py
```

Open http://localhost:8000. [Routing and terrain](examples/portfolio/model.mjs), [interaction and movement](examples/portfolio/app.mjs), [provenance](PROVENANCE.md). Public browser checks run every four hours.
