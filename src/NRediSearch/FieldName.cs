using System.Collections.Generic;

namespace NRediSearch
{
    public class FieldName {
        private readonly string name;
        private string attribute;

        public FieldName(string name) : this(name, null) {

        }

        public FieldName(string name, string attribute) {
            this.name = name;
            this.attribute = attribute;
        }

        public int AddCommandArguments(List<object> args) {
            args.Add(name);
            if (attribute == null) {
                return 1;
            }

            args.Add("AS".Literal());
            args.Add(attribute);
            return 3;
        }

        public static FieldName Of(string name) {
            return new FieldName(name);
        }

        public FieldName As(string attribute) {
            this.attribute = attribute;
            return this;
        }

        public static FieldName[] convert(params string[] names) {
        if (names == null) return null;
        FieldName[] fields = new FieldName[names.Length];
        for (int i = 0; i < names.Length; i++)
            fields[i] = FieldName.Of(names[i]);
        return fields;
        }
    }
}
