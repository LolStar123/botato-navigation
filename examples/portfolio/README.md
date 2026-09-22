# botato: working example

Add obstacles, move the destination and recompute an A* route without cutting corners.

**[Open the demo](https://lolstar123.github.io/botato-navigation/)** · [Calculation / workflow code](model.mjs) · [Checks](model.test.mjs)

![Example output](preview.png)

## Run it

From the repository root, with Python 3 and Node.js 22:

```sh
python -m http.server 8000 --directory examples/portfolio
```

Open http://localhost:8000. Change an input, or edit the JSON fixture, then export the computed result as JSON or CSV.

```sh
node --test examples/portfolio/model.test.mjs
```

## What it does

Read the current terrain and target, choose a traversable route and advance along it. If an obstacle changes the route, recalculate before moving; navigation feeds the wider automation loop.

## Scope and source

A standalone grid planner. No game process, memory bridge or account is required.

Botato navigation workflow and personal-site/dist/rocky-path.js obstacle-aware route demonstration.

`model.mjs` is the small public implementation. `app.mjs` connects its inputs and outputs to the browser. No package install or network key is needed to run the example. GitHub Pages runs the same files after the checks pass.
