@echo off
REM Builds the Unity assets from the game data in this folder.
REM Double-click it, or run from a terminal.  Any argument is passed through:
REM     import_to_unity.bat gamedata
REM     import_to_unity.bat --help
setlocal
set "HERE=%~dp0"

REM "py" is the Windows launcher; "python" is the usual direct install.
REM "python3" rarely exists on Windows, hence the order.
where py >nul 2>nul
if %errorlevel%==0 (
    py -3 "%HERE%import_to_unity.py" %*
    goto :done
)
where python >nul 2>nul
if %errorlevel%==0 (
    python "%HERE%import_to_unity.py" %*
    goto :done
)
where python3 >nul 2>nul
if %errorlevel%==0 (
    python3 "%HERE%import_to_unity.py" %*
    goto :done
)

echo.
echo Python was not found. Install Python 3 from https://python.org
echo and tick "Add python.exe to PATH" during setup, then re-run this file.
echo.
pause
exit /b 1

:done
echo.
echo Done. Open UnityProject in Unity and press Play.
pause
