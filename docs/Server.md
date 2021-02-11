Running a Redis Server
===

StackExchange.Redis is a client library that connects to an existing redis server. So; how do you *get* a running redis server? The good news is that it isn't tricky.

## Linux

The main redis build targets linux, so you can simply download, make, and run redis from there; follow the instructions [here](https://redis.io/download#installation)

## Windows

There are multiple ways of running redis on windows:

- [Memurai](https://www.memurai.com/) : a fully supported, well-maintained port of redis for Windows (this is a commercial product, with a free developer version available, and free trials)
  - previous to Memurai, MSOpenTech had a Windows port of linux, but this is no longer maintained and is now very out of date; it is not recommended, but: [here](https://www.nuget.org/packages/redis-64/)
- WSL/WSL2 : on Windows 10, you can run redis for linux in the Windows Subsystem for Linux; note, however, that WSL may have some significant performance implications, and WSL2 appears as a *different* machine (not the local machine), due to running as a VM

## Docker

If you are happy to run redis in a container, [an image is available on Docker Hub](https://hub.docker.com/_/redis/)

## Cloud

If you don't want to run your own redis servers, multiple commercial cloud offerings are available, including

- RedisLabs
- Azure Redis Cache
- AWS ElastiCache for Redis