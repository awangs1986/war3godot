@echo off
setlocal EnableExtensions
rem W3 editor (Avalonia). Always build Release + FBX plugin, then start.
set "ROOT=%~dp0"
set "PROJ=%ROOT%standalone\W3Editor\W3Editor.csproj"
set "PLUGIN=%ROOT%plugins\FbxImportPlugin\FbxImportPlugin.csproj"
set "EXE=%ROOT%standalone\W3Editor\bin\Release\net8.0\W3Editor.exe"

echo Building W3Editor (Release)...
dotnet build "%PROJ%" -c Release -v quiet
if errorlevel 1 (
  echo Build failed.
  pause
  exit /b 1
)

if exist "%PLUGIN%" (
  echo Building FbxImportPlugin...
  dotnet build "%PLUGIN%" -c Release -v quiet
  if errorlevel 1 (
    echo Plugin build failed.
    pause
    exit /b 1
  )
)

start "W3 Editor" "%EXE%"
