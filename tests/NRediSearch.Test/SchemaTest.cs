using Xunit;

namespace NRediSearch.Test
{
    public class SchemaTest
    {
        [Fact]
        public void PrintSchemaTest()
        {
            var sc = new Schema();

            sc.AddTextField("title", 5.0);
            sc.AddSortableTextField("plot", 1.0);
            sc.AddSortableTagField("genre", ",");
            sc.AddSortableNumericField("release_year");
            sc.AddSortableNumericField("rating");
            sc.AddSortableNumericField("votes");

            var schemaPrint = sc.ToString();

            Assert.StartsWith("Schema{fields=[TextField{name='title'", schemaPrint);
            Assert.Contains("{name='release_year', type=Numeric, sortable=true, noindex=false}", schemaPrint);
        }
    }
}
