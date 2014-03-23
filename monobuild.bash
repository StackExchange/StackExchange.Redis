rm -rf StackExchange.Redis/bin/mono
rm -rf BasicTest/bin/mono
mkdir StackExchange.Redis/bin/mono
mkdir BasicTest/bin/mono
mcs -recurse:StackExchange.Redis/*.cs -out:StackExchange.Redis/bin/mono/StackExchange.Redis.dll -target:library -unsafe+ -o+ -r:System.IO.Compression.dll -d:MONO
mcs BasicTest/Program.cs -out:BasicTest/bin/mono/BasicTest.exe -target:exe -o+ -r:StackExchange.Redis/bin/mono/StackExchange.Redis.dll
