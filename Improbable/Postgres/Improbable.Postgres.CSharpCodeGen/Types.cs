using System.Collections.Generic;
using Improbable.Schema.Bundle;

namespace Improbable.Postgres.CSharpCodeGen
{
    public static class Types
    {
        public static Dictionary<PrimitiveType, string> SchemaToPostgresTypes = new Dictionary<PrimitiveType, string>
        {
            {PrimitiveType.Double, "double precision"},
            {PrimitiveType.Float, "real"},
            {PrimitiveType.Int32, "integer"},
            {PrimitiveType.Int64, "bigint"},
            {PrimitiveType.Uint32, "integer"},
            {PrimitiveType.Uint64, "bigint"},
            {PrimitiveType.Sint32, "integer"},
            {PrimitiveType.Sint64, "bigint"},
            {PrimitiveType.Fixed32, "integer"},
            {PrimitiveType.Fixed64, "bigint"},
            {PrimitiveType.Sfixed32, "integer"},
            {PrimitiveType.Sfixed64, "bigint"},
            {PrimitiveType.Bool, "boolean"},
            {PrimitiveType.String, "text"},
            // ReSharper disable once StringLiteralTypo
            // https://www.postgresql.org/docs/11/datatype-binary.html
            {PrimitiveType.Bytes, "bytea"},
            {PrimitiveType.EntityId, "bigint"}
        };

        public static Dictionary<PrimitiveType, string> SchemaToReaderMethod = new Dictionary<PrimitiveType, string>
        {
            {PrimitiveType.Double, "GetDecimal"},
            {PrimitiveType.Float, "GetFloat"},
            {PrimitiveType.Int32, "GetInt32"},
            {PrimitiveType.Int64, "GetInt64"},
            {PrimitiveType.Uint32, "GetInt32"},
            {PrimitiveType.Uint64, "GetInt64"},
            {PrimitiveType.Sint32, "GetInt32"},
            {PrimitiveType.Sint64, "GetInt64"},
            {PrimitiveType.Fixed32, "GetInt32"},
            {PrimitiveType.Fixed64, "GetInt64"},
            {PrimitiveType.Sfixed32, "GetInt32"},
            {PrimitiveType.Sfixed64, "GetInt64"},
            {PrimitiveType.Bool, "GetBool"},
            {PrimitiveType.String, "GetString"},
            {PrimitiveType.Bytes, "GetBytes"},
            {PrimitiveType.EntityId, "GetInt64"}
        };
    }
}
