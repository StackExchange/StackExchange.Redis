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
            public FieldName FieldName { get; }
            public string Name { get; }
            public FieldType Type { get; }
            public bool Sortable { get; }
            public bool NoIndex { get; }
            public bool Unf { get; }

            internal Field(string name, FieldType type, bool sortable, bool noIndex = false, bool unf = false)
            : this(FieldName.Of(name), type, sortable, noIndex, unf)
            {
                Name = name;
            }

            internal Field(FieldName name, FieldType type, bool sortable, bool noIndex = false, bool unf = false)
            {
                FieldName = name;
                Type = type;
                Sortable = sortable;
                NoIndex = noIndex;
                Unf = unf;
            }

            internal virtual void SerializeRedisArgs(List<object> args)
            {
                static object GetForRedis(FieldType type) => type switch
                {
                    FieldType.FullText => "TEXT".Literal(),
                    FieldType.Geo => "GEO".Literal(),
                    FieldType.Numeric => "NUMERIC".Literal(),
                    FieldType.Tag => "TAG".Literal(),
                    _ => throw new ArgumentOutOfRangeException(nameof(type)),
                };
                FieldName.AddCommandArguments(args);
                args.Add(GetForRedis(Type));
                if (Sortable) { args.Add("SORTABLE".Literal()); }
                if (NoIndex) { args.Add("NOINDEX".Literal()); }
                if (Unf) args.Add("UNF".Literal());
            }
        }

        public class TextField : Field
        {
            public double Weight { get; }
            public bool NoStem { get; }

            public TextField(string name, double weight = 1.0, bool sortable = false, bool noStem = false, bool noIndex = false, bool unf = false)
            : base(name, FieldType.FullText, sortable, noIndex, unf)
            {
                Weight = weight;
                NoStem = noStem;
            }

            public TextField(FieldName name, double weight = 1.0, bool sortable = false, bool noStem = false, bool noIndex = false, bool unf = false)
            : base(name, FieldType.FullText, sortable, noIndex, unf)
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
        /// Add a text field to the schema with a given weight.
        /// </summary>
        /// <param name="name">The field's name.</param>
        /// <param name="weight">Its weight, a positive floating point number.</param>
        /// <returns>The <see cref="Schema"/> object.</returns>
        public Schema AddTextField(FieldName name, double weight = 1.0)
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
        public Schema AddSortableTextField(string name, double weight = 1.0, bool unf = false)
        {
            Fields.Add(new TextField(name, weight, true, unf: unf));
            return this;
        }

        /// <summary>
        /// Add a text field that can be sorted on.
        /// </summary>
        /// <param name="name">The field's name.</param>
        /// <param name="weight">Its weight, a positive floating point number.</param>
        /// <returns>The <see cref="Schema"/> object.</returns>
        public Schema AddSortableTextField(FieldName name, double weight = 1.0, bool unf = false)
        {
            Fields.Add(new TextField(name, weight, true, unf: unf));
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
        public Schema AddGeoField(FieldName name)
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
        /// Add a numeric field to the schema.
        /// </summary>
        /// <param name="name">The field's name.</param>
        /// <returns>The <see cref="Schema"/> object.</returns>
        public Schema AddNumericField(FieldName name)
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

        /// <summary>
        /// Add a numeric field that can be sorted on.
        /// </summary>
        /// <param name="name">The field's name.</param>
        /// <returns>The <see cref="Schema"/> object.</returns>
        public Schema AddSortableNumericField(FieldName name)
        {
            Fields.Add(new Field(name, FieldType.Numeric, true));
            return this;
        }

        public class TagField : Field
        {
            public string Separator { get; }

            internal TagField(string name, string separator = ",", bool sortable = false, bool unf = false)
            : base(name, FieldType.Tag, sortable, unf: unf)
            {
                Separator = separator;
            }

            internal TagField(FieldName name, string separator = ",", bool sortable = false, bool unf = false)
            : base(name, FieldType.Tag, sortable, unf: unf)
            {
                Separator = separator;
            }

            internal override void SerializeRedisArgs(List<object> args)
            {
                base.SerializeRedisArgs(args);
                if (Separator != ",")
                {
                    if (Sortable) args.Remove("SORTABLE");
                    args.Add("SEPARATOR".Literal());
                    args.Add(Separator);
                    if (Sortable) args.Add("SORTABLE".Literal());
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

        /// <summary>
        /// Add a TAG field.
        /// </summary>
        /// <param name="name">The field's name.</param>
        /// <param name="separator">The tag separator.</param>
        /// <returns>The <see cref="Schema"/> object.</returns>
        public Schema AddTagField(FieldName name, string separator = ",")
        {
            Fields.Add(new TagField(name, separator));
            return this;
        }

        /// <summary>
        /// Add a sortable TAG field.
        /// </summary>
        /// <param name="name">The field's name.</param>
        /// <param name="separator">The tag separator.</param>
        /// <returns>The <see cref="Schema"/> object.</returns>
        public Schema AddSortableTagField(string name, string separator = ",", bool unf = false)
        {
            Fields.Add(new TagField(name, separator, sortable: true, unf: unf));
            return this;
        }

        /// <summary>
        /// Add a sortable TAG field.
        /// </summary>
        /// <param name="name">The field's name.</param>
        /// <param name="separator">The tag separator.</param>
        /// <returns>The <see cref="Schema"/> object.</returns>
        public Schema AddSortableTagField(FieldName name, string separator = ",", bool unf = false)
        {
            Fields.Add(new TagField(name, separator, sortable: true, unf: unf));
            return this;
        }
    }
}
