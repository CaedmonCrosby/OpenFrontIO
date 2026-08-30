@echo off
setlocal
cd /d "%~dp0"

if exist "desktop-app\OpenFront.exe" (
  start "" "%~dp0desktop-app\OpenFront.exe"
  exit /b 0
)

where node >nul 2>&1
if errorlevel 1 (
  echo Node.js is required. Install it from https://nodejs.org/ then run this again.
  pause
  exit /b 1
)

if not exist "node_modules\" (
  echo Installing dependencies. This only happens once...
  call npm run inst
  if errorlevel 1 (
    echo Install failed.
    pause
    exit /b 1
  )
)

echo Starting OpenFront at http://localhost:9000
echo Singleplayer works offline. Close this window to stop the game.
call npm run dev
