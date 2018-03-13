#!/usr/bin/env bash
set -eux

# From https://github.com/tarosky/k8s-redis-ha/tree/master/images/server
mkdir -p /opt/bin
cp /dig-a /dig-srv /k8s-redis-ha-server /opt/bin
cp /redis.template.conf /opt