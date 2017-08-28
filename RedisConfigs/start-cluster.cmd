@echo off
pushd Cluster\7000
@start /min ..\..\3.0.503\redis-server.exe redis.conf
popd
pushd Cluster\7001
@start /min ..\..\3.0.503\redis-server.exe redis.conf
popd
pushd Cluster\7002
@start /min ..\..\3.0.503\redis-server.exe redis.conf
popd
pushd Cluster\7003
@start /min ..\..\3.0.503\redis-server.exe redis.conf
popd
pushd Cluster\7004
@start /min ..\..\3.0.503\redis-server.exe redis.conf
popd
pushd Cluster\7005
@start /min ..\..\3.0.503\redis-server.exe redis.conf
popd