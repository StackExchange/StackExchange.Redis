using Xunit;

namespace NRediSearch.Test
{
    public class SchemaTest
    {
        [Fact]
        public void PrintSchemaTest()
        {
            var sc = new Schema()
                .AddTextField("title", 5.0)
                .AddSortableTextField("plot", 1.0)
                .AddSortableTagField("genre", ",")
                .AddSortableNumericField("release_year")
                .AddSortableNumericField("rating")
                .AddSortableNumericField("votes");

            var schemaPrint = sc.ToString();

            Assert.StartsWith("Schema{fields=[TextField{name='title'", schemaPrint);
            Assert.Contains("{name='release_year', type=Numeric, sortable=True, noindex=False}", schemaPrint);
        }
    }
}
