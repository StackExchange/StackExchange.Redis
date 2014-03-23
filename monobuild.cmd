@rd /s /q StackExchange.Redis\bin\mono
@rd /s /q BasicTest\bin\mono
@md StackExchange.Redis\bin\mono
@md BasicTest\bin\mono
@echo Building StackExchange.Redis.dll ...
@call mcs -recurse:StackExchange.Redis\*.cs -out:StackExchange.Redis\bin\mono\StackExchange.Redis.dll -target:library -unsafe+ -o+ -r:System.IO.Compression.dll -d:MONO
@echo Building BasicTest.exe ...
@call mcs BasicTest\Program.cs -out:BasicTest\bin\mono\BasicTest.exe -target:exe -o+ -r:StackExchange.Redis\bin\mono\StackExchange.Redis.dll
@copy StackExchange.Redis\bin\mono\*.* BasicTest\bin\mono > nul
@echo .
@echo Running basic test (Mono) ...
@call mono BasicTest\bin\mono\BasicTest.exe 100000
@echo .
@echo Running basic test (.NET) ...
@call BasicTest\bin\mono\BasicTest.exe 100000
