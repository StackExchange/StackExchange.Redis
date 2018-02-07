dotnet msbuild "/t:Restore;Build;Pack" "/p:NuGetBuildTasksPackTargets='000'" "/p:PackageOutputPath=nupkgs" "/p:Configuration=Release"
