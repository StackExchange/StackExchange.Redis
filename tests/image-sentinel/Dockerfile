# https://redis.io/topics/sentinel
FROM debian:jessie

ADD ["https://github.com/tarosky/dig-a/releases/download/v1.0.1/dig-a_linux_amd64", "/dig-a"]
ADD ["https://github.com/tarosky/dig-srv/releases/download/v1.0.0/dig-srv_linux_amd64", "/dig-srv"]
COPY ["sentinel.template.conf", "k8s-redis-ha-sentinel", "run.sh", "/"]

CMD ["/run.sh"]

ENTRYPOINT ["bash", "-c"]