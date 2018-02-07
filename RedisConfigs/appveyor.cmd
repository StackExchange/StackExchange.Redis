@echo off
echo Starting Redis wrapper...
call %~dp0\start-all.cmd
echo Servers really started, we hope.
exit /b 0