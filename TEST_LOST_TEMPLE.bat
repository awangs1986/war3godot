@echo off
setlocal EnableExtensions
rem Manual test: load & display the Lost Temple map (war3\maps\LostTemple\lost_temple.res)
rem using the main project's preview scene with the C# Warsmash terrain path.
for %%I in ("%~dp0.") do set "PROJECT=%%~fI"
for %%I in ("%~dp0.tools\godot-4.7.1-rc2-mono\Godot_v4.7.1-rc2_mono_win64\Godot_v4.7.1-rc2_mono_win64.exe") do set "GODOT=%%~fI"
if not exist "%GODOT%" (
  echo Godot .NET executable was not found:
  echo %GODOT%
  pause
  exit /b 1
)
start "W3 Lost Temple Test" "%GODOT%" --path "%PROJECT%" res://scenes/w3_lost_temple_preview.tscn
