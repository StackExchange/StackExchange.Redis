@start "Redis (Master): 6379" /min .\3.0.503\redis-server.exe master.conf
@start "Redis (Slave): 6380" /min .\3.0.503\redis-server.exe slave.conf
@start "Redis (Secure): 6381" /min .\3.0.503\redis-server.exe secure.conf
call start-cluster.cmd
