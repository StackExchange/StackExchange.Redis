@echo off
echo Starting Redis servers for testing...
call %~dp0\start-basic.cmd
call %~dp0\start-failover.cmd
call %~dp0\start-cluster.cmd
call %~dp0\start-sentinel.cmd
call %~dp0\start-sentinel2.cmd
echo Servers started (minimized).
exit /b 0