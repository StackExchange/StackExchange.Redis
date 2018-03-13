#!/usr/bin/env bash
set -eux


# From https://github.com/tarosky/k8s-redis-ha/blob/master/images/sword/run.sh
namespace="$(< /var/run/secrets/kubernetes.io/serviceaccount/namespace)"
readonly namespace
readonly service_domain="_$SERVICE_PORT._tcp.$SERVICE.$namespace.svc.cluster.local"
readonly service_sentinel_domain="_$SENTINEL_PORT._tcp.$SENTINEL.$namespace.svc.cluster.local"

redis_info () {
  set +e
  timeout 10 redis-cli -h "$1" -a "$service_domain" info replication
  set -e
}

redis_info_role () {
  echo "$1" | grep -e '^role:' | cut -d':' -f2 | tr -d '[:space:]'
}

server_domains () {
  dig +noall +answer srv "$1" | awk '{print $NF}' | sed 's/\.$//g'
}

sentinel_master () {
  redis-cli -p 26379 --raw sentinel master redis-testing
}

reset_sentinel () {
  redis-cli -p 26379 --raw sentinel reset redis-testing
}

domain_ip () {
  dig +noall +answer a "$1" | head -1 | awk '{print $NF}'
}

sentinel_num_slaves () {
  echo "$1" | awk '/^num-slaves$/{getline; print}'
}

sentinel_num_sentinels () {
  echo "$1" | awk '/^num-other-sentinels$/{getline; print}'
}

sentinel_master_down () {
  set +e
  echo "$1" | awk '/^flags$/{getline; print}' | grep -e '[so]_down' > /dev/null
  local -r res="$?"
  set -e
  if [ "$res" = '0' ]; then
    echo 'true'
  else
    echo 'false'
  fi
}

reflect_recreated_servers () {
  # Wait enough and recheck the current state since this could happen during
  # restart of a pod as well.
  sleep 30

  # If the state has changed, never do anything because it is just a
  # transition state and sentinels will find the next Master themselves.
  # Restarting them is just harmful.
  local master
  master="$(sentinel_master)"
  readonly master
  local num_slaves
  num_slaves="$(sentinel_num_slaves "$master")"
  readonly num_slaves
  local master_down
  master_down="$(sentinel_master_down "$master")"
  readonly master_down
  if [ "$num_slaves" != '0' ] || [ "$master_down" != 'true' ]; then
    return 0
  fi

  local servers
  servers="$(server_domains "$service_domain")"
  readonly servers

  local s
  for s in $servers; do
    local s_ip
    s_ip="$(domain_ip "$s")"

    if [ -z "$s_ip" ]; then
      >&2 echo "Failed to resolve: $s"
      continue
    fi

    local i
    i="$(redis_info "$s_ip")"
    if [ -n "$i" ]; then
      if [ "$(redis_info_role "$i")" = 'master' ]; then
        redis-cli -p 26379 shutdown nosave
        return 0
      fi
    else
      >&2 echo "Unable to get Replication INFO: $s ($s_ip)"
      continue
    fi
  done

  >&2 echo "Master not found."
  return 1
}

reflect_scale_in () {
  # Resetting during failover causes disastrous result.
  # Be sure to wait enough and once again confirm running Master exists.
  sleep 10

  local master
  master="$(sentinel_master)"
  readonly master
  local master_down
  master_down="$(sentinel_master_down "$master")"
  readonly master_down

  if [ "$master_down" = 'false' ]; then
    reset_sentinel
  fi
}

run () {
  local master
  master="$(sentinel_master)"
  readonly master
  local num_slaves
  num_slaves="$(sentinel_num_slaves "$master")"
  readonly num_slaves
  local master_down
  master_down="$(sentinel_master_down "$master")"
  readonly master_down
  local srv_count
  srv_count="$(server_domains "$service_domain" | wc -l)"
  readonly srv_count
  local num_sentinels
  num_sentinels="$(sentinel_num_sentinels "$master")"
  readonly num_sentinels
  local srv_sentinel_count
  srv_sentinel_count="$(server_domains "$service_sentinel_domain" | wc -l)"
  readonly srv_sentinel_count

  if [ "$num_slaves" = '0' ] && [ "$master_down" = 'true' ]; then
    # If the Redis server StatefulSet is once deleted and created again,
    # Sentinel can't recognize it because Master server the Sentinel knows
    # has now disappeared.
    # To let the Sentinel find the
    reflect_recreated_servers
  elif [ "$(echo "$srv_count - 1" | bc)" -lt "$num_slaves" ]; then
    # If Sentinel recognizes more Slaves than what really exist, the Sentinel
    # might have stale data.
    # This happens when the Redis server StatefulSet is scaled in,
    # such as from 5 replicas to 3 replicas.
    # Sentinel thinks this descrease as failure.
    # To tell that this is not a failure but a scale in, resetting is needed.
    reflect_scale_in
  elif [ "$(echo "$srv_sentinel_count - 1" | bc)" -lt "$num_sentinels" ]; then
    reflect_scale_in
  fi
}

while true; do
  sleep 60
  run
done

