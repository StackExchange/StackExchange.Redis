INDENT='   '
echo "Starting Redis servers for testing..."

#Basic Servers
echo "Starting Basic: 6379-6382"
pushd Basic > /dev/null
echo "${INDENT}Master: 6379"
redis-server master-6379.conf &>/dev/null &
echo "${INDENT}Slave: 6380"
redis-server slave-6380.conf &>/dev/null &
echo "${INDENT}Secure: 6381"
redis-server secure-6381.conf &>/dev/null &
popd > /dev/null

echo Servers started.