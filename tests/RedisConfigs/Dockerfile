FROM redis:5-alpine

COPY Basic /data/Basic/
COPY Failover /data/Failover/
COPY Cluster /data/Cluster/
COPY Sentinel /data/Sentinel/

RUN chown -R redis:redis /data

COPY Docker/docker-entrypoint.sh /usr/local/bin/

RUN apk add --update supervisor && rm  -rf /tmp/* /var/cache/apk/*

ADD Docker/supervisord.conf /etc/

ENTRYPOINT ["docker-entrypoint.sh"]

EXPOSE 6379 6380 6381 6382 6383 7000 7001 7002 7003 7004 7005 7010 7011 26379 26380 26381
