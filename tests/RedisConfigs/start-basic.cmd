@echo off
echo Starting Basic: 
pushd %~dp0\Basic
echo   Primary: 6379
@start "Redis (Primary): 6379" /min ..\3.0.503\redis-server.exe primary-6379-3.0.conf
echo   Replica: 6380
@start "Redis (Replica): 6380" /min ..\3.0.503\redis-server.exe replica-6380.conf
echo   Secure: 6381
@start "Redis (Secure): 6381" /min ..\3.0.503\redis-server.exe secure-6381.conf
@REM TLS config doesn't work in 3.x - don't even start it
@REM echo   TLS: 6384
@REM @start "Redis (TLS): 6384" /min ..\3.0.503\redis-server.exe tls-ciphers-6384.conf
popd