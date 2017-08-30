@echo off
echo Starting Redis servers on 6379, 6780, 6381, and 7000-7005
@start "Redis (Master): 6379" /min %~dp0\3.0.503\redis-server.exe %~dp0\master.conf
@start "Redis (Slave): 6380" /min %~dp0\3.0.503\redis-server.exe %~dp0\slave.conf
@start "Redis (Secure): 6381" /min %~dp0\3.0.503\redis-server.exe %~dp0\secure.conf
call %~dp0\start-cluster.cmd
echo Servers started (minimized).
