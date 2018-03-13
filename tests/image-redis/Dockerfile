FROM redis:4.0

ADD ["https://github.com/tarosky/dig-a/releases/download/v1.0.1/dig-a_linux_amd64", "/dig-a"]
ADD ["https://github.com/tarosky/dig-srv/releases/download/v1.0.0/dig-srv_linux_amd64", "/dig-srv"]
COPY ["redis.template.conf", "k8s-redis-ha-server", "run.sh", "/"]

CMD ["/run.sh"]

ENTRYPOINT ["/usr/bin/env", "bash", "-c"]