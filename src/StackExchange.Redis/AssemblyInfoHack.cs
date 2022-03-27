// Yes, this is embarrassing. However, in .NET Core the including AssemblyInfo (ifdef'd or not) will screw with
// your version numbers. Therefore, we need to move the attribute out into another file...this file.
// When .csproj merges in, this should be able to return to Properties/AssemblyInfo.cs
using System;

[assembly: CLSCompliant(true)]
