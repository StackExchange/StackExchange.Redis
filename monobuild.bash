rm -rf StackExchange.Redis/bin/mono
mkdir StackExchange.Redis/bin/mono
mcs -recurse:StackExchange.Redis/*.cs -out:StackExchange.Redis/bin/mono/StackExchange.Redis.dll -target:library -unsafe+ -r:System.IO.Compression.dll -d:MONO
