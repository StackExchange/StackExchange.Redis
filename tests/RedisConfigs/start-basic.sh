INDENT='   '
echo "Starting Redis servers for testing..."

#Basic Servers
echo "Starting Basic: 6379-6382"
pushd Basic > /dev/null
echo "${INDENT}Primary: 6379"
redis-server primary-6379.conf &>/dev/null &
echo "${INDENT}Replica: 6380"
redis-server replica-6380.conf &>/dev/null &
echo "${INDENT}Secure: 6381"
redis-server secure-6381.conf &>/dev/null &
echo "${INDENT}Tls: 6384"
redis-server tls-ciphers-6384.conf &>/dev/null &
popd > /dev/null

echo Servers started.