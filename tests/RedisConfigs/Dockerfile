FROM redis:7.2-rc1

COPY Basic /data/Basic/
COPY Failover /data/Failover/
COPY Cluster /data/Cluster/
COPY Sentinel /data/Sentinel/
COPY Certs /Certs/

RUN chown -R redis:redis /data
RUN chown -R redis:redis /Certs

COPY Docker/docker-entrypoint.sh /usr/local/bin/
RUN chmod +x /usr/local/bin/docker-entrypoint.sh

RUN apt-get -y update && apt-get install -y git gcc make supervisor

COPY Docker/install-envoy.sh /usr/local/bin
RUN sh /usr/local/bin/install-envoy.sh

RUN apt-get clean

COPY Envoy/envoy.yaml /envoy/envoy.yaml
ADD Docker/supervisord.conf /etc/

ENTRYPOINT ["docker-entrypoint.sh"]

EXPOSE 6379 6380 6381 6382 6383 6384 7000 7001 7002 7003 7004 7005 7010 7011 7015 26379 26380 26381
