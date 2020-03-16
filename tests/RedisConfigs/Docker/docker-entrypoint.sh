#!/bin/sh

if [ "$#" -ne 0 ]; then
    exec "$@"
else
    mkdir -p /data/Temp/master-6379
    mkdir -p /data/Temp/secure-6381
    mkdir -p /data/Temp/slave-6380
    mkdir -p /data/Temp/master-6382
    mkdir -p /data/Temp/slave-6383

    mkdir -p /data/Temp/cluster-7000
    mkdir -p /data/Temp/cluster-7001
    mkdir -p /data/Temp/cluster-7002
    mkdir -p /data/Temp/cluster-7003
    mkdir -p /data/Temp/cluster-7004
    mkdir -p /data/Temp/cluster-7005

    mkdir -p /data/Temp/redis-7010
    mkdir -p /data/Temp/redis-7011
    mkdir -p /data/Temp/sentinel-26379
    mkdir -p /data/Temp/sentinel-26380
    mkdir -p /data/Temp/sentinel-26381

    mkdir -p /var/log/supervisor

    supervisord -c /etc/supervisord.conf
    sleep 3

    tail -f /var/log/supervisor/*.log
fi