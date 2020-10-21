using System;
using Xunit;

[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly, DisableTestParallelization = true),
    CollectionDefinition(DisableParallelization = true)]

namespace NRediSearch.Test
{

    public class AssemblyInfo
    {
        public AssemblyInfo()
        {
        }
    }
}
