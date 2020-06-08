INDENT='   '
echo "Starting Redis servers for testing..."

#Basic Servers
echo "Starting Basic: 6379-6382"
pushd Basic > /dev/null
echo "${INDENT}Master: 6379"
redis-server master-6379.conf &>/dev/null &
echo "${INDENT}Replica: 6380"
redis-server replica-6380.conf &>/dev/null &
echo "${INDENT}Secure: 6381"
redis-server secure-6381.conf &>/dev/null &
popd > /dev/null

#Failover Servers
echo Starting Failover: 6382-6383
pushd Failover > /dev/null
echo "${INDENT}Master: 6382"
redis-server master-6382.conf &>/dev/null &
echo "${INDENT}Replica: 6383"
redis-server replica-6383.conf &>/dev/null &
popd > /dev/null

# Cluster Servers
echo Starting Cluster: 7000-7005
pushd Cluster > /dev/null
redis-server cluster-7000.conf &>/dev/null &
redis-server cluster-7001.conf &>/dev/null &
redis-server cluster-7002.conf &>/dev/null &
redis-server cluster-7003.conf &>/dev/null &
redis-server cluster-7004.conf &>/dev/null &
redis-server cluster-7005.conf &>/dev/null &
popd > /dev/null

#Sentinel Servers
echo Starting Sentinel: 7010-7011,26379-26381
pushd Sentinel > /dev/null
echo "${INDENT}Targets: 7010-7011"
redis-server redis-7010.conf &>/dev/null &
redis-server redis-7011.conf &>/dev/null &
echo "${INDENT}Monitors: 26379-26381"
redis-server sentinel-26379.conf --sentinel &>/dev/null &
redis-server sentinel-26380.conf --sentinel &>/dev/null &
redis-server sentinel-26381.conf --sentinel &>/dev/null &
popd > /dev/null

echo Servers started.