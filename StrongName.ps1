$key = Import-StrongNameKeyPair -KeyFile StackExchange.Redis.snk
dir StackExchange.Redis*/bin/Release/StackExchange.Redis.dll | Set-StrongName -KeyPair $key -Verbose -NoBackup -Force
nuget pack StackExchange.Redis.StrongName.nuspec