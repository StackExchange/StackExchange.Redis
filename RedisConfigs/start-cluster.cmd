@echo off
pushd Cluster\7000
@start "Redis (Cluster): 7000" /min ..\..\3.0.503\redis-server.exe redis.conf
popd
pushd Cluster\7001
@start "Redis (Cluster): 7001" /min ..\..\3.0.503\redis-server.exe redis.conf
popd
pushd Cluster\7002
@start "Redis (Cluster): 7002" /min ..\..\3.0.503\redis-server.exe redis.conf
popd
pushd Cluster\7003
@start "Redis (Cluster): 7003" /min ..\..\3.0.503\redis-server.exe redis.conf
popd
pushd Cluster\7004
@start "Redis (Cluster): 7004" /min ..\..\3.0.503\redis-server.exe redis.conf
popd
pushd Cluster\7005
@start "Redis (Cluster): 7005" /min ..\..\3.0.503\redis-server.exe redis.conf
popd