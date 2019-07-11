using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Improbable.CSharpCodeGen;
using Improbable.Schema.Bundle;
using static Improbable.CSharpCodeGen.Types;
using static Improbable.CSharpCodeGen.Case;
using ValueType = Improbable.Schema.Bundle.ValueType;

namespace Improbable.Postgres.CSharpCodeGen
{
    public class ComponentGenerator : ICodeGenerator
    {
        public string Generate(TypeDescription type)
        {
            if (!HasAnnotation(type, WellKnownAnnotations.CreateTableAttribute))
            {
                return string.Empty;
            }

            var setupCommands = type.Annotations.GetAnnotationStrings(WellKnownAnnotations.CreateTableAttribute, 0);

            var primaryKeyFields = type.Fields.WithAnnotation(WellKnownAnnotations.PrimaryKeyAttribute).ToList();
            if (primaryKeyFields.Count == 0)
            {
                throw new Exception($"{type.QualifiedName} is exposed to the database, but no fields are marked with [{WellKnownAnnotations.PrimaryKeyAttribute}]");
            }

            var columnCreator = CreateColumns(type.QualifiedName, type.Fields);

            var primaryKeyColumnNames = string.Join(", ", primaryKeyFields.Select(f => $@"{f.Name}"));
            var primaryKey = $"PRIMARY KEY ({primaryKeyColumnNames})";

            var selectClause = string.Join(", ", type.Fields.Select(f => $@"{f.Name}{PostgresTypeConversion(f)}"));
            var ordinal = 0;

            var indexFields = type.Fields.WithAnnotation(WellKnownAnnotations.IndexAttribute);

            var typeName = $"global::{CapitalizeNamespace(type.QualifiedName)}";

            return $@"public static string CreateTypeTable(string tableName)
{{
    return $@""CREATE TABLE {{tableName}} (
{Indent(2, columnCreator.TrimEnd())}
{Indent(2, primaryKey)}
    );"";
}}

public static {typeName} FromQuery(global::Npgsql.NpgsqlDataReader reader)
{{
    return new {typeName} (
{Indent(2, CreateReader(type, type.Fields)).TrimEnd()}
    );
}}

public const string SelectClause = ""{selectClause}"";

{string.Join(Environment.NewLine, type.Fields.Select(f => $"public const int {SnakeCaseToPascalCase(f.Name)}Ordinal = {ordinal++};"))}

public struct DatabaseChangeNotification
{{
    public {CapitalizeNamespace(type.QualifiedName)}? Old {{ get; set; }}

    public {CapitalizeNamespace(type.QualifiedName)} New {{ get; set; }}
}}

public static string InitializeDatabase(string tableName)
{{
    return $@""
{string.Join(Environment.NewLine, setupCommands)}

{{CreateTypeTable(tableName)}}

{string.Join(Environment.NewLine, indexFields.Select(f => f.Annotations.GetAnnotationString(WellKnownAnnotations.IndexAttribute, 0).Replace("{fieldName}", f.Name)))}

-- Setup change notifications. This maps to the DatabaseChangeNotification class.
CREATE OR REPLACE FUNCTION notify_{{tableName}}() RETURNS TRIGGER AS $$
    BEGIN
        PERFORM pg_notify( '{{tableName}}'::text, json_build_object( 'old', row_to_json(OLD), 'new', row_to_json(NEW) )::text);
        RETURN NEW;
    END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER notify_{{tableName}}_tgr
    AFTER INSERT OR UPDATE on {{tableName}}
    FOR EACH ROW EXECUTE PROCEDURE notify_{{tableName}}();"";
}}
";
        }

        private static string CreateReader(TypeDescription type, IReadOnlyList<FieldDefinition> fields)
        {
            var sb = new StringBuilder();
            foreach (var field in fields)
            {
                var ordinal = $"{SnakeCaseToPascalCase(field.Name)}Ordinal";

                switch (field.TypeSelector)
                {
                    case FieldType.Option:
                        sb.AppendLine($"reader.IsDBNull({ordinal}) ? null : ({GetFieldTypeAsCsharp(type, field)}) reader.{Types.SchemaToReaderMethod[field.OptionType.InnerType.Primitive]}({ordinal}),");
                        break;
                    case FieldType.Singular:
                        sb.AppendLine($"reader.{Types.SchemaToReaderMethod[field.SingularType.Type.Primitive]}({ordinal}),");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return sb.ToString().TrimEnd().TrimEnd(',');
        }

        private static string PostgresTypeConversion(FieldDefinition f)
        {
            switch (f.TypeSelector)
            {
                case FieldType.Option:
                    switch (f.OptionType.InnerType.ValueTypeSelector)
                    {
                        case ValueType.Primitive:
                            if (f.OptionType.InnerType.Primitive == PrimitiveType.String)
                            {
                                return "::text";
                            }

                            break;
                        default:
                            throw new InvalidOperationException("Don't know how to convert this type from Postgres");
                    }

                    break;
                case FieldType.Singular:
                    switch (f.SingularType.Type.ValueTypeSelector)
                    {
                        case ValueType.Primitive:
                            if (f.SingularType.Type.Primitive == PrimitiveType.String)
                            {
                                return "::text";
                            }

                            break;
                        default:
                            throw new InvalidOperationException("Don't know how to convert this type from Postgres");
                    }

                    break;
                default:
                    return string.Empty;
            }

            return string.Empty;
        }

        private static string CreateColumns(string outerType, IReadOnlyList<FieldDefinition> fields)
        {
            var columnCreator = new StringBuilder();

            foreach (var field in fields)
            {
                var databaseType = field.Annotations.GetAnnotationString(WellKnownAnnotations.FieldTypeAttribute, 0);
                if (string.IsNullOrEmpty(databaseType))
                {
                    switch (field.TypeSelector)
                    {
                        case FieldType.Option:
                            switch (field.OptionType.InnerType.ValueTypeSelector)
                            {
                                case ValueType.Enum:
                                    databaseType = "integer";
                                    break;
                                case ValueType.Primitive:
                                    databaseType = Types.SchemaToPostgresTypes[field.OptionType.InnerType.Primitive];
                                    break;
                                case ValueType.Type:
                                    throw new Exception("Compound types are not supported");
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }

                            break;

                        case FieldType.List:
                            switch (field.ListType.InnerType.ValueTypeSelector)
                            {
                                case ValueType.Enum:
                                    databaseType = "integer[] not null";
                                    break;
                                case ValueType.Primitive:
                                    databaseType = $"{Types.SchemaToPostgresTypes[field.ListType.InnerType.Primitive]}[] not null";
                                    break;
                                case ValueType.Type:
                                    throw new Exception("Compound types are not supported");
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }

                            break;

                        case FieldType.Map:
                            throw new InvalidOperationException($"{outerType}.{field.Name}: Maps are not supported.");

                        case FieldType.Singular:
                            switch (field.SingularType.Type.ValueTypeSelector)
                            {
                                case ValueType.Enum:
                                    databaseType = "integer";
                                    break;
                                case ValueType.Primitive:
                                    databaseType = Types.SchemaToPostgresTypes[field.SingularType.Type.Primitive];
                                    break;
                                case ValueType.Type:
                                    throw new Exception("Compound types are not supported");
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }

                            databaseType += " not null";
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                databaseType = databaseType.Replace("{fieldName}", SnakeCaseToPascalCase(field.Name));

                if (string.IsNullOrEmpty(databaseType))
                {
                    columnCreator.AppendLine($"/* {field.Name} Skipped: \"\"{field.FieldId}\"\" integer */");
                }
                else
                {
                    columnCreator.AppendLine($"{field.Name} {databaseType},");
                }
            }

            return columnCreator.ToString().TrimEnd();
        }
    }
}
