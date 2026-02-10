FROM redislabs/client-libs-test:custom-21860421418-debian-amd64

COPY --from=configs ./Basic /data/Basic/
COPY --from=configs ./Failover /data/Failover/
COPY --from=configs ./Cluster /data/Cluster/
COPY --from=configs ./Sentinel /data/Sentinel/
COPY --from=configs ./Certs /Certs/

RUN chown -R redis:redis /data
RUN chown -R redis:redis /Certs

COPY docker-entrypoint.sh /usr/local/bin/
RUN chmod +x /usr/local/bin/docker-entrypoint.sh

RUN apt-get -y update && apt-get install supervisor -y

RUN apt-get clean

ADD supervisord.conf /etc/

ENTRYPOINT ["docker-entrypoint.sh"]

EXPOSE 6379 6380 6381 6382 6383 6384 7000 7001 7002 7003 7004 7005 7010 7011 26379 26380 26381
