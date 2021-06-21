@echo off
echo Starting Failover:
pushd %~dp0\Failover
echo   Master: 6382
@start "Redis (Failover Master): 6382" /min ..\3.0.503\redis-server.exe master-6382.conf
echo   Replica: 6383
@start "Redis (Failover Replica): 6383" /min ..\3.0.503\redis-server.exe replica-6383.conf
popd