@rd /s /q StackExchange.Redis\bin\mono
@md StackExchange.Redis\bin\mono
@call mcs -recurse:StackExchange.Redis\*.cs -out:StackExchange.Redis\bin\mono\StackExchange.Redis.dll -target:library -unsafe+ -r:System.IO.Compression.dll -d:MONO