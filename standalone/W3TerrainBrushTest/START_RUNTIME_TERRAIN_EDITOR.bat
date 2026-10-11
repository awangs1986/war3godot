@echo off
setlocal EnableExtensions EnableDelayedExpansion
rem Runtime terrain editor: plain F5-style scene, no Godot EditorPlugin involved.
for %%I in ("%~dp0.") do set "PROJECT=%%~fI"
for %%I in ("%~dp0..\..\.tools\godot-4.7.1-rc2-mono\Godot_v4.7.1-rc2_mono_win64\Godot_v4.7.1-rc2_mono_win64.exe") do set "GODOT=%%~fI"
if not exist "%GODOT%" (
  echo Godot .NET executable was not found:
  echo %GODOT%
  pause
  exit /b 1
)
start "W3 Runtime Terrain Editor" "%GODOT%" --path "%PROJECT%" res://RuntimeTerrainEditor.tscn
