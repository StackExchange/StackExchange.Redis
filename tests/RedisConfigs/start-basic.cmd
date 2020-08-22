@echo off
echo Starting Basic: 
pushd %~dp0\Basic
echo   Master: 6479
@start "Redis (Master): 6479" /min ..\3.0.503\redis-server.exe master-6479.conf
echo   Replica: 6480
@start "Redis (Replica): 6480" /min ..\3.0.503\redis-server.exe replica-6480.conf
echo   Secure: 6481
@start "Redis (Secure): 6481" /min ..\3.0.503\redis-server.exe secure-6481.conf
popd