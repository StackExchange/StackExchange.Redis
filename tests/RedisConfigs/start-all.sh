INDENT='   '
echo "Starting Redis servers for testing..."

#Basic Servers
echo "Starting Basic: 6479-6482"
pushd Basic > /dev/null
echo "${INDENT}Master: 6479"
redis-server master-6479.conf &>/dev/null &
echo "${INDENT}Replica: 6480"
redis-server replica-6480.conf &>/dev/null &
echo "${INDENT}Secure: 6481"
redis-server secure-6481.conf &>/dev/null &
popd > /dev/null

#Failover Servers
echo Starting Failover: 6482-6483
pushd Failover > /dev/null
echo "${INDENT}Master: 6482"
redis-server master-6482.conf &>/dev/null &
echo "${INDENT}Replica: 6483"
redis-server replica-6483.conf &>/dev/null &
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
echo Starting Sentinel: 7010-7011,26479-26481
pushd Sentinel > /dev/null
echo "${INDENT}Targets: 7010-7011"
redis-server redis-7010.conf &>/dev/null &
redis-server redis-7011.conf &>/dev/null &
echo "${INDENT}Monitors: 26479-26481"
redis-server sentinel-26479.conf --sentinel &>/dev/null &
redis-server sentinel-26480.conf --sentinel &>/dev/null &
redis-server sentinel-26481.conf --sentinel &>/dev/null &
popd > /dev/null

echo Servers started.