@echo off
echo Starting Sentinel:
pushd %~dp0\Sentinel
echo   Targets: 7010-7011
@start "Redis (Sentinel-Target): 7010" /min ..\3.0.503\redis-server.exe redis-7010.conf
@start "Redis (Sentinel-Target): 7011" /min ..\3.0.503\redis-server.exe redis-7011.conf
echo   Monitors: 26479-26481
@start "Redis (Sentinel): 26479" /min ..\3.0.503\redis-server.exe sentinel-26479.conf --sentinel
@start "Redis (Sentinel): 26480" /min ..\3.0.503\redis-server.exe sentinel-26480.conf --sentinel
@start "Redis (Sentinel): 26481" /min ..\3.0.503\redis-server.exe sentinel-26481.conf --sentinel
popd