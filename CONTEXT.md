# Diablo-like World Authoring

This context defines how commercial Diablo-like levels are divided between static world authoring and runtime gameplay authoring.

## Language

**Static World**:
The immutable spatial foundation of a level: terrain, water, cliffs, static decoration, regions, and spatial anchors. It contains no runtime behavior or mutable gameplay state.
_Avoid_: Map content, level logic

**Static World Editor**:
The authoring tool that exclusively owns the Static World. It does not own monsters, NPC behavior, quests, encounters, interactions, or scripts.
_Avoid_: External editor, terrain editor, gameplay editor
