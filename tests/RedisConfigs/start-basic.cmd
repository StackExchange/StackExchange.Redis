@echo off
echo Starting Basic: 
pushd %~dp0\Basic
echo   Master: 6379
@start "Redis (Master): 6379" /min ..\3.0.503\redis-server.exe master-6379.conf
echo   Slave: 6380
@start "Redis (Slave): 6380" /min ..\3.0.503\redis-server.exe slave-6380.conf
echo   Secure: 6381
@start "Redis (Secure): 6381" /min ..\3.0.503\redis-server.exe secure-6381.conf
popd