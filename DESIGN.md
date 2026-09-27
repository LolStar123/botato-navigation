# Botato route lab design

The terrain is the interface. The demo should feel like a small tactics map, not a dashboard describing pathfinding.

- Put the editable world first and keep controls physically close to it.
- Let movement, replanning and combat explain the system in motion.
- Keep terrain, route and enemy colours quiet enough that the meowl and destination remain obvious.
- Preserve collision checks, corner-cut prevention and unreachable-state feedback.
- Keep route diagnostics available in settings rather than competing with the map.
- On phones, retain tap-to-move and terrain editing without horizontal page scroll.
