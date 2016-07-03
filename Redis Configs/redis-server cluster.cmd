@echo off
pushd Cluster\7000
@start /min ..\..\..\packages\Redis-64.3.0.503\redis-server.exe redis.conf --port 7000
popd
pushd Cluster\7001
@start /min ..\..\..\packages\Redis-64.3.0.503\redis-server.exe redis.conf --port 7001
popd
pushd Cluster\7002
@start /min ..\..\..\packages\Redis-64.3.0.503\redis-server.exe redis.conf --port 7002
popd
pushd Cluster\7003
@start /min ..\..\..\packages\Redis-64.3.0.503\redis-server.exe redis.conf --port 7003
popd
pushd Cluster\7004
@start /min ..\..\..\packages\Redis-64.3.0.503\redis-server.exe redis.conf --port 7004
popd
pushd Cluster\7005
@start /min ..\..\..\packages\Redis-64.3.0.503\redis-server.exe redis.conf --port 7005
popd