FROM ruby:2

RUN curl -L http://download.redis.io/redis-stable/src/redis-trib.rb >/usr/local/bin/redis-trib && \
    chmod +x /usr/local/bin/redis-trib
RUN gem install redis -v 4.0.1

WORKDIR /usr/local/bin
ENTRYPOINT tail -f /dev/null