# HiveWE validation port notice

This directory is a temporary, isolated workflow-validation port based on:

- HiveWE `src/brush/brush.cpp`
- HiveWE `src/brush/terrain_brush.cpp`
- HiveWE `src/brush/terrain_operators.cpp`
- HiveWE `src/main_window/glwidget.cpp`

Source project: <https://github.com/stijnherfst/HiveWE>

Source revision used for this validation: `2f001ee16421dc9a9d6ff744dd032900e934f082`.

HiveWE is distributed under the GNU Affero General Public License v3.0.
`HiveWeTerrainBrushPort.cs` is the temporary direct behavioural port currently
wired into the standalone Godot **W3 Terrain** panel. It remains isolated in this
directory so it can be removed and replaced by project-owned C# after the user has
validated the workflow.
