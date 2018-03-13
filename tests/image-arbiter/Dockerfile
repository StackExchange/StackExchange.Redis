# From https://github.com/tarosky/k8s-redis-ha/blob/master/images/sword/Dockerfile

FROM debian:jessie

RUN apt-get update && apt-get install -y bc dnsutils redis-tools

COPY ["run.sh", "/"]

CMD ["/run.sh"]

ENTRYPOINT ["/bin/bash", "-c"]