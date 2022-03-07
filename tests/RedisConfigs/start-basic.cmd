@echo off
echo Starting Basic: 
pushd %~dp0\Basic
echo   Primary: 6379
@start "Redis (Primary): 6379" /min ..\3.0.503\redis-server.exe primary-6379.conf
echo   Replica: 6380
@start "Redis (Replica): 6380" /min ..\3.0.503\redis-server.exe replica-6380.conf
echo   Secure: 6381
@start "Redis (Secure): 6381" /min ..\3.0.503\redis-server.exe secure-6381.conf
popd