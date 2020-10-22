If you're using WSL2, then the WSL2 instance is now a full VM with a separate IP address rather than being part of the current machine;
this means that you may need to use:

``` txt
~$ ip addr show eth0 | grep -oP '(?<=inet\s)\d+(\.\d+){3}'
```

to get the server's address, and update the entries in `TestConfig.json`, for example if the IP address is `172.17.168.110`:

``` json
{
  "MasterServer": "172.17.168.110",
  "ReplicaServer": "172.17.168.110",
  "SecureServer": "172.17.168.110",
  "FailoverMasterServer": "172.17.168.110",
  "FailoverReplicaServer": "172.17.168.110",
  "RediSearchServer": "172.17.168.110",
  "RemoteServer": "172.17.168.110",
  "SentinelServer": "172.17.168.110",
  "ClusterServer": "172.17.168.110",
  "IPv4Server": "172.17.168.110"
}
```