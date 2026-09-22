# Working on botato

Read PROVENANCE.md and examples/portfolio/README.md first.
Keep calculation and decision logic in model.mjs, independently runnable in Node.
Run `node --test examples/portfolio/model.test.mjs` after changes.
Keep generated fixtures labelled; never present sample outcomes as measured production results.
Preserve the project's workflow: Read the current terrain and target, choose a traversable route and advance along it. If an obstacle changes the route, recalculate before moving; navigation feeds the wider automation loop.
Add regression checks for changed decisions, including missing or invalid inputs.
Do not add credentials, user records or runtime account integrations to the demo.
