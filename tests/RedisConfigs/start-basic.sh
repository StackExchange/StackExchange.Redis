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

echo Servers started.