@rd /s /q StackExchange.Redis\bin\Release 1>nul 2>nul
@rd /s /q BasicTest\bin\Release 1>nul 2>nul
@md StackExchange.Redis\bin\Release 1>nul 2>nul
@md BasicTest\bin\Release 1>nul 2>nul
@echo Building StackExchange.Redis.dll ...
@call csc /out:StackExchange.Redis\bin\Release\StackExchange.Redis.dll /target:library /unsafe+ /o+ /r:System.IO.Compression.dll /recurse:StackExchange.Redis\*.cs 
@echo Building BasicTest.exe ...
@call csc /out:BasicTest\bin\Release\BasicTest.exe /target:exe -o+ /r:StackExchange.Redis\bin\Release\StackExchange.Redis.dll BasicTest\Program.cs 
@copy StackExchange.Redis\bin\Release\*.* BasicTest\bin\Release > nul
@echo .
@echo Running basic test (.NET) ...
@call BasicTest\bin\Release\BasicTest.exe 100000