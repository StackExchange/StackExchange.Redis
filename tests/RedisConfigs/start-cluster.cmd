@echo off
echo Starting Cluster: 7000-7005
pushd %~dp0\Cluster
@start "Redis (Cluster): 7000" /min ..\3.0.503\redis-server.exe cluster-7000.conf
@start "Redis (Cluster): 7001" /min ..\3.0.503\redis-server.exe cluster-7001.conf
@start "Redis (Cluster): 7002" /min ..\3.0.503\redis-server.exe cluster-7002.conf
@start "Redis (Cluster): 7003" /min ..\3.0.503\redis-server.exe cluster-7003.conf
@start "Redis (Cluster): 7004" /min ..\3.0.503\redis-server.exe cluster-7004.conf
@start "Redis (Cluster): 7005" /min ..\3.0.503\redis-server.exe cluster-7005.conf
popd