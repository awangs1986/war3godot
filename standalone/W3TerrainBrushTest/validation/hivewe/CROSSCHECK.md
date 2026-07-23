# Brush/input cross-check

## HiveWE

- Its GL widget sends camera input and brush input to a dedicated map canvas.
- Placement mode draws the brush; only selection mode draws a selection rectangle.
- Left press calls `apply_begin()` and performs one immediate `apply(0.5)`.
- Left drag applies on every mouse-move event; left release calls `apply_end()`.
- Terrain brushes snap differently for odd/even corner-brush sizes.
- A complete corner/pathing snapshot is taken at stroke start and one undo group is
  finalized at stroke end.

## Warsmash

- `WarsmashGdxTerrainEditor` also owns a dedicated LibGDX canvas and routes wheel
  input directly to its camera manager.
- Its terrain editor's `touchDown`, `touchUp`, `touchDragged`, and `mouseMoved`
  methods are currently stubs, so it provides no conflicting placement-brush
  algorithm to prefer over HiveWE.
- It initializes the editor clear color to black and marks that background with a
  TODO. That confirms the black area is canvas/camera/background behavior, not a
  Warcraft terrain material rule.

## Previous local implementation

- The brush was forwarded through Godot's built-in 3D editor viewport, where the
  editor selection tool and private editor camera state still participated.
- It applied strokes only after a custom minimum-spacing threshold and attempted
  to force the editor camera by synthetic shortcut events.

## Validation decision

Use the independent Godot W3 Terrain canvas with `HiveWeTerrainBrushPort.cs`: it
translates HiveWE's snapping, press/move/release state machine and terrain
operators. Leave ordinary wheel and RMB/MMB events to the dedicated camera. The
port is intentionally isolated until it is replaced with project-owned C# code.

## Fixed cliff-grade compatibility override

- HiveWE's `raise1`/`raise2` calculate the target from the current corner layer,
  so repeated strokes stack layer height.
- The requested Warcraft Editor validation behaviour is fixed grade 1/2: applying
  either tool to an already grade-2 corner must not raise it further.
- Warsmash has no terrain-brush implementation to contradict this choice. The
  temporary port therefore keeps all other HiveWE flow but uses `max(current, 1)`
  and `max(current, 2)` for the two raise tools.
