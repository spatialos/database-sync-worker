using System;
using System.Linq;
using System.Text;
using Improbable.CSharpCodeGen;
using Improbable.Schema.Bundle;
using static Improbable.DatabaseSync.CSharpCodeGen.WellKnownAnnotations;
using static Improbable.CSharpCodeGen.Case;
using static Improbable.CSharpCodeGen.Types;
using ValueType = Improbable.Schema.Bundle.ValueType;

namespace Improbable.DatabaseSync.CSharpCodeGen
{
    public class DatabaseSyncGenerator : ICodeGenerator
    {
        private const string DatabaseSyncItemClass = "global::Improbable.DatabaseSync.DatabaseSyncItem";

        public string Generate(TypeDescription type)
        {
            var exposedFields = type.Fields.Where(Types.IsFieldExposedToDatabase).ToList();
            if (!exposedFields.Any())
            {
                return string.Empty;
            }

            var profileIdFields = type.Fields.Where(f => Annotations.HasAnnotations(f, ProfileIdAnnotation)).ToArray();
            if (!profileIdFields.Any())
            {
                throw new InvalidOperationException($"{type.QualifiedName} is missing a string field annotated with {ProfileIdAnnotation}");
            }

            if (profileIdFields.Length != 1)
            {
                throw new InvalidOperationException($"{type.QualifiedName} has multiple fields annotated with {ProfileIdAnnotation}. Only one is allowed.");
            }

            var profileIdField = profileIdFields[0];
            if (profileIdField.TypeSelector != FieldType.Singular || profileIdField.SingularType.Type.ValueTypeSelector != ValueType.Primitive || profileIdField.SingularType.Type.Primitive != PrimitiveType.String)
            {
                throw new InvalidOperationException($"{profileIdField.Name} is annotated with {ProfileIdAnnotation}, which requires it to be a string type.");
            }

            var profileFieldName = SnakeCaseToPascalCase(profileIdField.Name);

            var sb = new StringBuilder();

            // Provide shortcuts
            foreach (var field in exposedFields)
            {
                sb.AppendLine($"public const string {DatabaseSyncFieldName(field)} = \"{type.QualifiedName}.{field.Name}\";");
                sb.AppendLine($"public const string {PathFieldName(field)} = \"{type.ComponentId}_{field.FieldId}\";");
            }

            // Use the static Linq methods below to avoid needing to import System.Linq.
            const string enumerable = "global::System.Linq.Enumerable";
            const string isImmediateChild = "global::Improbable.DatabaseSync.DatabaseSync.IsImmediateChild";
            const string insertStatement = "insert into {databaseName} (path, name, count)";

            var valueFields = exposedFields.WithAnnotation(ValueAnnotation).ToList();
            var listFields = exposedFields.WithAnnotation(ValueListAnnotation).ToList();

            sb.AppendLine($@"[global::Improbable.DatabaseSync.Hydrate({type.ComponentId})]
public static {SchemaComponentUpdate} Hydrate({GenericIEnumerable}<{DatabaseSyncItemClass}> items, string profilePath)
{{
    var update = new Update();

    foreach(var item in items)
    {{
        // Update count and list fields.
        switch(item.Name)
        {{
{string.Join("\n", valueFields.Select(field => Indent(3, $"case {DatabaseSyncFieldName(field)}: update.Set{FieldName(field)}(item.Count); break;")))}
{string.Join("\n", listFields.Select(field => Indent(3, $@"case {DatabaseSyncFieldName(field)}:
update.Set{FieldName(field)}({enumerable}.ToArray({enumerable}.Where(items,  i => {isImmediateChild}(i.Path, item.Path))));
break;")))}
        }}
    }}

    return update.ToSchemaUpdate();
}}

[global::Improbable.DatabaseSync.ProfileIdFromSchemaData]
public static string GetProfileIdFromComponentData(global::Improbable.Worker.CInterop.SchemaObject fields)
{{
    return fields.GetString({profileIdField.FieldId});
}}

public static string ComponentToDatabase(string databaseName, in global::{CapitalizeNamespace(type.QualifiedName)} item)
{{
    if (string.IsNullOrEmpty(item.{profileFieldName}))
    {{
        throw new global::System.ArgumentNullException(nameof(item.{profileFieldName}));
    }}
    return $@""{string.Join("\n", valueFields.Select(field => $"{insertStatement} values('{{item.{profileFieldName}}}.{{{PathFieldName(field)}}}', '{{{DatabaseSyncFieldName(field)}}}', {{item.{FieldName(field)}}});"))}
{string.Join("\n", listFields.Select(field => $"{insertStatement} values('{{item.{profileFieldName}}}.{{{PathFieldName(field)}}}', '{{{DatabaseSyncFieldName(field)}}}', 0);"))}"";
}}

[global::Improbable.DatabaseSync.ProfileId]
public string DatabaseSyncProfileId => {profileFieldName};

{
                    string.Join("\n", exposedFields.Select(field => $@"public string {FieldName(field)}Path()
{{
    return {profileFieldName} + ""."" + {PathFieldName(field)};
}}
"))
                }");
            return sb.ToString().TrimEnd();
        }

        public static string JsonPropertyDecorator(FieldDefinition field)
        {
            return $"[global::Newtonsoft.Json.JsonProperty(\"{field.Name}\")]";
        }

        private static string FieldName(FieldDefinition field)
        {
            return $"{SnakeCaseToPascalCase(field.Name)}";
        }

        private static string DatabaseSyncFieldName(FieldDefinition field)
        {
            return $"DatabaseSync{FieldName(field)}";
        }

        private static string PathFieldName(FieldDefinition field)
        {
            return $"{DatabaseSyncFieldName(field)}Path";
        }
    }
}
