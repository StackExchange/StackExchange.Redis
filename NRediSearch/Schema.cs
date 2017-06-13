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
            Numeric
        }

        public class Field
        {
            public String Name { get; }
            public FieldType Type { get; }
            public bool Sortable {get;}

            internal Field(string name, FieldType type, bool sortable)
            {
                Name = name;
                Type = type;
                Sortable = sortable;
            }

            internal virtual void SerializeRedisArgs(List<object> args)
            {
                object GetForRedis(FieldType type)
                {
                    switch (type)
                    {
                        case FieldType.FullText: return "TEXT".Literal();
                        case FieldType.Geo: return "GEO".Literal();
                        case FieldType.Numeric: return "NUMERIC".Literal();
                        default: throw new ArgumentOutOfRangeException(nameof(type));
                    }
                }
                args.Add(Name);
                args.Add(GetForRedis(Type));
                if(Sortable){args.Add("SORTABLE");}
            }
        }
        public class TextField : Field
        {
            public double Weight { get; }
            internal TextField(string name, double weight = 1.0) : base(name, FieldType.FullText, false)
            {
                Weight = weight;
            }
            internal TextField(string name, bool sortable, double weight = 1.0) : base(name, FieldType.FullText, sortable)
            {
                Weight = weight;
            }
            internal override void SerializeRedisArgs(List<object> args)
            {
                base.SerializeRedisArgs(args);
                if (Weight != 1.0)
                {
                    args.Add("WEIGHT".Literal());
                    args.Add(Weight);
                }
            }
        }

        public List<Field> Fields { get; } = new List<Field>();

        /// <summary>
        /// Add a text field to the schema with a given weight
        /// </summary>
        /// <param name="name">the field's name</param>
        /// <param name="weight">its weight, a positive floating point number</param>
        /// <returns>the schema object</returns>
        public Schema AddTextField(string name, double weight = 1.0)
        {
            Fields.Add(new TextField(name, weight));
            return this;
        }

        /// <summary>
        /// Add a text field that can be sorted on
        /// </summary>
        /// <param name="name">the field's name</param>
        /// <param name="weight">its weight, a positive floating point number</param>
        /// <returns>the schema object</returns>
        public Schema AddSortableTextField(string name, double weight = 1.0)
        {
            Fields.Add(new TextField(name, true, weight));
            return this;
        }

        /// <summary>
        /// Add a numeric field to the schema
        /// </summary>
        /// <param name="name">the field's name</param>
        /// <returns>the schema object</returns>
        public Schema AddGeoField(string name)
        {
            Fields.Add(new Field(name, FieldType.Geo, false));
            return this;
        }

        /// <summary>
        /// Add a numeric field to the schema
        /// </summary>
        /// <param name="name">the field's name</param>
        /// <returns>the schema object</returns>
        public Schema AddNumericField(string name)
        {
            Fields.Add(new Field(name, FieldType.Numeric, false));
            return this;
        }

        /// <summary>
        /// Add a numeric field that can be sorted on
        /// </summary>
        /// <param name="name">the field's name</param>
        /// <returns>the schema object</returns>
        public Schema AddSortableNumericField(string name)
        {
            Fields.Add(new Field(name, FieldType.Numeric, true));
            return this;
        }

    }
}
