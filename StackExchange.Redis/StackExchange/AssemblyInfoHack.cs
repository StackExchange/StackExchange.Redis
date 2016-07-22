// Yes, this is embarassing. However, in .NET Core the including AssemblyInfo (ifdef'd or not) will screw with
// your version numbers. Therefore, we need to move the attribute out into another file...this file.
// When .csproj merges in, this should be able to return to Properties/AssemblyInfo.cs
#if !STRONG_NAME
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("StackExchange.Redis.Tests")]
#endif