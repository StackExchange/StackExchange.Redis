namespace NRediSearch
{
    public class FieldName {
        private readonly string name;
        private string attribute;

        public FieldName(String name) {
            this(name, null);
        }

        public FieldName(String name, String attribute) {
            this.name = name;
            this.attribute = attribute;
        }

        public int AddCommandArguments(Collection<String> args) {
            args.add(name);
            if (attribute == null) {
                return 1;
            }

            args.add("AS".Literal());
            args.add(attribute);
            return 3;
        }

        public static FieldName Of(String name) {
            return new FieldName(name);
        }

        public FieldName As(String attribute) {
            this.attribute = attribute;
            return this;
        }

        public static FieldName[] convert(params String[] names) {
        if (names == null) return null;
        FieldName[] fields = new FieldName[names.length];
        for (int i = 0; i < names.length; i++)
            fields[i] = FieldName.of(names[i]);
        return fields;
        }
    }
}
