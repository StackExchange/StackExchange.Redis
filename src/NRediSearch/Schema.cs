// .NET port of https://github.com/RedisLabs/JRediSearch/

using System;
using System.Collections.Generic;

namespace NRediSearch
{
    /// <summary>
    /// Schema abstracts the schema definition when creating an index.
    /// Documents can contain fields not mentioned in the schema, but the index will only index pre-defined fields
    /// </summary>
    public sealed class Schema
    {
        public enum FieldType
        {
            FullText,
            Geo,
            Numeric,
            Tag
        }

        public class Field
        {
            public string Name { get; }
            public FieldType Type { get; }
            public bool Sortable { get; }
            public bool NoIndex { get; }

            internal Field(string name, FieldType type, bool sortable, bool noIndex = false)
            {
                Name = name;
                Type = type;
                Sortable = sortable;
                NoIndex = noIndex;
            }

            internal virtual void SerializeRedisArgs(List<object> args)
            {
                static object GetForRedis(FieldType type)
                {
                    switch (type)
                    {
                        case FieldType.FullText: return "TEXT".Literal();
                        case FieldType.Geo: return "GEO".Literal();
                        case FieldType.Numeric: return "NUMERIC".Literal();
                        case FieldType.Tag: return "TAG".Literal();
                        default: throw new ArgumentOutOfRangeException(nameof(type));
                    }
                }
                args.Add(Name);
                args.Add(GetForRedis(Type));
                if (Sortable) { args.Add("SORTABLE".Literal()); }
                if (NoIndex) { args.Add("NOINDEX".Literal()); }
            }
        }

        public class TextField : Field
        {
            public double Weight { get; }
            public bool NoStem { get; }

            public TextField(string name, double weight = 1.0, bool sortable = false, bool noStem = false, bool noIndex = false) : base(name, FieldType.FullText, sortable, noIndex)
            {
                Weight = weight;
                NoStem = noStem;
            }

            internal override void SerializeRedisArgs(List<object> args)
            {
                base.SerializeRedisArgs(args);
                if (Weight != 1.0)
                {
                    args.Add("WEIGHT".Literal());
                    args.Add(Weight);
                }
                if (NoStem) args.Add("NOSTEM".Literal());
            }
        }

        public List<Field> Fields { get; } = new List<Field>();

        /// <summary>
        /// Add a field to the schema.
        /// </summary>
        /// <param name="field">The <see cref="Field"/> to add.</param>
        /// <returns>The <see cref="Schema"/> object.</returns>
        public Schema AddField(Field field)
        {
            Fields.Add(field ?? throw new ArgumentNullException(nameof(field)));
            return this;
        }

        /// <summary>
        /// Add a text field to the schema with a given weight.
        /// </summary>
        /// <param name="name">The field's name.</param>
        /// <param name="weight">Its weight, a positive floating point number.</param>
        /// <returns>The <see cref="Schema"/> object.</returns>
        public Schema AddTextField(string name, double weight = 1.0)
        {
            Fields.Add(new TextField(name, weight));
            return this;
        }

        /// <summary>
        /// Add a text field that can be sorted on.
        /// </summary>
        /// <param name="name">The field's name.</param>
        /// <param name="weight">Its weight, a positive floating point number.</param>
        /// <returns>The <see cref="Schema"/> object.</returns>
        public Schema AddSortableTextField(string name, double weight = 1.0)
        {
            Fields.Add(new TextField(name, weight, true));
            return this;
        }

        /// <summary>
        /// Add a numeric field to the schema.
        /// </summary>
        /// <param name="name">The field's name.</param>
        /// <returns>The <see cref="Schema"/> object.</returns>
        public Schema AddGeoField(string name)
        {
            Fields.Add(new Field(name, FieldType.Geo, false));
            return this;
        }

        /// <summary>
        /// Add a numeric field to the schema.
        /// </summary>
        /// <param name="name">The field's name.</param>
        /// <returns>The <see cref="Schema"/> object.</returns>
        public Schema AddNumericField(string name)
        {
            Fields.Add(new Field(name, FieldType.Numeric, false));
            return this;
        }

        /// <summary>
        /// Add a numeric field that can be sorted on.
        /// </summary>
        /// <param name="name">The field's name.</param>
        /// <returns>The <see cref="Schema"/> object.</returns>
        public Schema AddSortableNumericField(string name)
        {
            Fields.Add(new Field(name, FieldType.Numeric, true));
            return this;
        }

        public class TagField : Field
        {
            public string Separator { get; }
            internal TagField(string name, string separator = ",") : base(name, FieldType.Tag, false)
            {
                Separator = separator;
            }

            internal override void SerializeRedisArgs(List<object> args)
            {
                base.SerializeRedisArgs(args);
                if (Separator != ",")
                {
                    args.Add("SEPARATOR".Literal());
                    args.Add(Separator);
                }
            }
        }

        /// <summary>
        /// Add a TAG field.
        /// </summary>
        /// <param name="name">The field's name.</param>
        /// <param name="separator">The tag separator.</param>
        /// <returns>The <see cref="Schema"/> object.</returns>
        public Schema AddTagField(string name, string separator = ",")
        {
            Fields.Add(new TagField(name, separator));
            return this;
        }
    }
}
