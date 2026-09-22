# Navigation provenance

Original sources copied from the author's Botato project on 22 September 2026: Bot.Core/World/Pathfinder.cs and Botato.Navigation/PathSmoother.cs. Their comments and implementation establish the three-stage wall-clearance, weighted-cost A* and collision-checked smoothing workflow.

The browser model is a standalone JavaScript implementation of that workflow. It uses an orthogonal wall-distance field, an admissible Euclidean search heuristic and a 3% smoothing-cost tolerance. It is not a byte-for-byte port of all production routing behavior. Terrain examples and the small meowl are authored for this public sandbox. No game binaries, maps, process access, credentials or account data are included.
