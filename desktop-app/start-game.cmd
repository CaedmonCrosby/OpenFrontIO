@echo off
setlocal
cd /d "%~dp0\.."
set "PATH=%LOCALAPPDATA%\Programs\nodejs;%ProgramFiles%\nodejs;%PATH%"
set SKIP_BROWSER_OPEN=true
set GAME_ENV=dev
set NUM_WORKERS=1
set DOMAIN=localhost
set GIT_COMMIT=DEV
set TURNSTILE_SITE_KEY=1x00000000000000000000AA
set API_KEY=WARNING_DEV_API_KEY_DO_NOT_USE_IN_PRODUCTION
set ADMIN_BOT_API_KEY=WARNING_DEV_ADMIN_BOT_KEY_DO_NOT_USE_IN_PRODUCTION
start "OpenFrontVite" /B node "%CD%\node_modules\vite\bin\vite.js" --port 9000 --strictPort --host 127.0.0.1
start "OpenFrontServer" /B node "%CD%\node_modules\tsx\dist\cli.mjs" src\server\Server.ts
exit /b 0
