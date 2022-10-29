@echo off
echo Starting Sentinel:
pushd %~dp0\Sentinel
echo   Targets: 7010-7011
@start "Redis (Sentinel-Target): 7010" /min ..\3.0.503\redis-server.exe redis-7010.conf
@start "Redis (Sentinel-Target): 7011" /min ..\3.0.503\redis-server.exe redis-7011.conf
echo   Monitors: 26379-26381
@start "Redis (Sentinel): 26379" /min ..\3.0.503\redis-server.exe sentinel-26379.conf --sentinel
@start "Redis (Sentinel): 26380" /min ..\3.0.503\redis-server.exe sentinel-26380.conf --sentinel
@start "Redis (Sentinel): 26381" /min ..\3.0.503\redis-server.exe sentinel-26381.conf --sentinel
popd