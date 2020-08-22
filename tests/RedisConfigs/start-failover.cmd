@echo off
echo Starting Failover:
pushd %~dp0\Failover
echo   Master: 6482
@start "Redis (Failover Master): 6482" /min ..\3.0.503\redis-server.exe master-6482.conf
echo   Replica: 6483
@start "Redis (Failover Replica): 6483" /min ..\3.0.503\redis-server.exe replica-6483.conf
popd