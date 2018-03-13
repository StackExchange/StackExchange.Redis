#!/usr/bin/env bash
set -eux

mkdir -p /opt/bin
cp /dig-a /dig-srv /k8s-redis-ha-sentinel /opt/bin
cp /sentinel.template.conf /opt
chmod -R +x /opt/bin