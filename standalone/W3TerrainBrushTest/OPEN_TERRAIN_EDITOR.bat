@echo off
setlocal EnableExtensions EnableDelayedExpansion
for %%I in ("%~dp0.") do set "PROJECT=%%~fI"
for %%I in ("%~dp0..\..\.tools\godot-4.7.1-rc2-mono\Godot_v4.7.1-rc2_mono_win64\Godot_v4.7.1-rc2_mono_win64.exe") do set "GODOT=%%~fI"
if not exist "%GODOT%" (
  echo Godot .NET executable was not found:
  echo %GODOT%
  pause
  exit /b 1
)
if /I "%~1"=="--check" (
  for %%I in ("%~dp0..\..\.tools\appdata") do set "APPDATA=%%~fI"
  for %%I in ("%~dp0..\..\.tools\localappdata") do set "LOCALAPPDATA=%%~fI"
  for %%I in ("%~dp0..\..\.tools\godot-user") do set "USERPROFILE=%%~fI"
  "%GODOT%" --headless --path "%PROJECT%" --script res://tools/terrain_setup_smoke.gd
  exit /b !ERRORLEVEL!
)
if /I "%~1"=="--runtime" (
  start "W3 Terrain Brush Test - HiveWE Validation" "%GODOT%" --path "%PROJECT%"
  exit /b 0
)
start "W3 Terrain Editor" "%GODOT%" --editor --path "%PROJECT%" res://NativeTerrainEditorTest.tscn
