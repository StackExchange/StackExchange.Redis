FROM redis:5

COPY Basic /data/Basic/
COPY Failover /data/Failover/
COPY Cluster /data/Cluster/
COPY Sentinel /data/Sentinel/

RUN chown -R redis:redis /data

COPY Docker/docker-entrypoint.sh /usr/local/bin/
RUN chmod +x /usr/local/bin/docker-entrypoint.sh

RUN apt-get -y update && apt-get install -y git gcc make supervisor && apt-get clean

ADD Docker/supervisord.conf /etc/

ENTRYPOINT ["docker-entrypoint.sh"]

EXPOSE 6479 6480 6481 6482 6483 7000 7001 7002 7003 7004 7005 7010 7011 26479 26480 26481
