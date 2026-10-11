# war3godot — World Authoring

war3godot provides Warcraft III terrain rendering, map editing, model animation,
and day/night lighting for Godot. The next game's gameplay is still undecided.
This context separates static world authoring from runtime gameplay authoring.

## Language

**Static World**:
The immutable spatial foundation of a level: terrain, water, cliffs, static decoration, regions, and spatial anchors. It contains no runtime behavior or mutable gameplay state.
_Avoid_: Map content, level logic

**Static World Editor**:
The authoring tool that exclusively owns the Static World. It does not own monsters, NPC behavior, quests, encounters, interactions, or scripts.
_Avoid_: External editor, terrain editor, gameplay editor
