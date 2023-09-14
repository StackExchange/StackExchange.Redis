@echo off
echo Starting Sentinel:
pushd %~dp0\SentinelTwoReplicas
echo   Targets: 7020-7022
@start "Redis (Sentinel-Target): 7020" /min ..\3.0.503\redis-server.exe redis-7020.conf
@start "Redis (Sentinel-Target): 7021" /min ..\3.0.503\redis-server.exe redis-7021.conf
@start "Redis (Sentinel-Target): 7022" /min ..\3.0.503\redis-server.exe redis-7022.conf
echo   Monitors: 26389-26391
@start "Redis (Sentinel): 26389" /min ..\3.0.503\redis-server.exe sentinel-26389.conf --sentinel
@start "Redis (Sentinel): 26390" /min ..\3.0.503\redis-server.exe sentinel-26390.conf --sentinel
@start "Redis (Sentinel): 26391" /min ..\3.0.503\redis-server.exe sentinel-26391.conf --sentinel
popd