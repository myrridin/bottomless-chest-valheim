@echo off
REM Local dedicated server for BottomlessChest development.
REM Join from the client with: Join IP -> 127.0.0.1:2456
REM
REM -savedir keeps the server's world (and its .bottomless.dat) separate from the
REM client's, so the two copies can be compared when something looks wrong.

set SERVER=C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server
set SAVEDIR=C:\valheim_mods\server-save

if not exist "%SAVEDIR%" mkdir "%SAVEDIR%"
cd /d "%SERVER%"

echo Starting BottomlessDev on 127.0.0.1:2456 (password: devpassword)
valheim_server.exe -nographics -batchmode ^
  -name "BottomlessDev" ^
  -port 2456 ^
  -world "bottomlessdev" ^
  -password "devpassword" ^
  -savedir "%SAVEDIR%" ^
  -crossplay
pause
