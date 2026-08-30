@echo off
setlocal
cd /d "%~dp0"

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

echo Starting OpenFront for LAN play.
echo Open http://localhost:9000 on this PC. Other devices on your network can use this computer's IP on port 9000.
call npm run dev:host
