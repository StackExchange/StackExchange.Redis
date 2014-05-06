rm -rf StackExchange.Redis/bin/mono
rm -rf BasicTest/bin/mono
mkdir -p StackExchange.Redis/bin/mono
mkdir -p BasicTest/bin/mono
echo -e "Building StackExchange.Redis.dll ..."
mcs -recurse:StackExchange.Redis/*.cs -out:StackExchange.Redis/bin/mono/StackExchange.Redis.dll -target:library -unsafe+ -o+ -r:System.IO.Compression.dll
echo -e "Building BasicTest.exe ..."
mcs BasicTest/Program.cs -out:BasicTest/bin/mono/BasicTest.exe -target:exe -o+ -r:StackExchange.Redis/bin/mono/StackExchange.Redis.dll
cp StackExchange.Redis/bin/mono/*.* BasicTest/bin/mono/
echo -e "Running basic test ..."
mono BasicTest/bin/mono/BasicTest.exe 10000
