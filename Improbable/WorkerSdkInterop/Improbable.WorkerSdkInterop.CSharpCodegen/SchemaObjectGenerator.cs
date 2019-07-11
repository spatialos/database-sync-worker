using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Improbable.CSharpCodeGen;
using Improbable.Schema.Bundle;
using static Improbable.CSharpCodeGen.Case;
using static Improbable.CSharpCodeGen.Types;
using ValueType = Improbable.Schema.Bundle.ValueType;

namespace Improbable.WorkerSdkInterop.CSharpCodeGen
{
    public class SchemaObjectGenerator : ICodeGenerator
    {
        private readonly Bundle bundle;

        public SchemaObjectGenerator(Bundle bundle)
        {
            this.bundle = bundle;
        }

        public string Generate(TypeDescription type)
        {
            var typeName = GetPascalCaseNameFromTypeName(type.QualifiedName);

            var content = new StringBuilder();
            var commandTypes = bundle.CommandTypes;

            content.AppendLine(GenerateSchemaConstructor(type, type.Fields, bundle).TrimEnd());
            content.AppendLine(GenerateApplyToSchemaObject(type.Fields).TrimEnd());
            content.AppendLine(GenerateUpdaters(type.Fields).TrimEnd());
            content.AppendLine(GenerateSchemaConstructor(type, type.Fields));

            if (type.ComponentId.HasValue)
            {
                content.AppendLine(GenerateFromUpdate(type, type.ComponentId.Value, bundle).TrimEnd());
                content.AppendLine(GenerateCreateGetEvents(type.Events).TrimEnd());
                content.AppendLine(GenerateUpdateStruct(type, type.Fields));
            }

            if (commandTypes.Contains(type.QualifiedName))
            {
                content.AppendLine($@"public static {typeName} Create(global::Improbable.Worker.CInterop.SchemaCommandRequest? fields)
{{
    return fields.HasValue ? new {typeName}(fields.Value.GetObject()) : new {typeName}();
}}");
            }

            return content.ToString();
        }

        private static string GenerateSchemaConstructor(TypeDescription type, IReadOnlyList<FieldDefinition> fields, Bundle bundle)
        {
            var typeName = GetPascalCaseNameFromTypeName(type.QualifiedName);

            var text = new StringBuilder();
            var sb = new StringBuilder();

            foreach (var field in fields)
            {
                var fieldName = SnakeCaseToPascalCase(field.Name);

                var output = GetAssignmentForField(type, field, fieldName, bundle);
                sb.AppendLine(output);
            }

            text.AppendLine($@"
internal {typeName}(global::Improbable.Worker.CInterop.SchemaObject fields)
{{
{Indent(1, sb.ToString().TrimEnd())}
}}");

            return text.ToString();
        }

        private static string GenerateApplyToSchemaObject(IReadOnlyList<FieldDefinition> fields)
        {
            var update = new StringBuilder();

            foreach (var field in fields)
            {
                var fieldName = SnakeCaseToPascalCase(field.Name);
                var fieldAddMethod = GetFieldAddMethod(field);

                string output;
                switch (field.TypeSelector)
                {
                    case FieldType.Option:
                        switch (field.OptionType.InnerType.ValueTypeSelector)
                        {
                            case ValueType.Enum:
                                output = $"if ({fieldName}.HasValue) {{ fields.{fieldAddMethod}({field.FieldId}, (uint){fieldName}.Value); }}";
                                break;
                            case ValueType.Primitive:
                                switch (field.OptionType.InnerType.Primitive)
                                {
                                    case PrimitiveType.Bytes:
                                    case PrimitiveType.String:
                                        output = $"if ({fieldName} != null ) {{ fields.{fieldAddMethod}({field.FieldId}, {fieldName}); }}";
                                        break;
                                    case PrimitiveType.EntityId:
                                        output = $"if ({fieldName}.HasValue) {{ fields.{fieldAddMethod}({field.FieldId}, {fieldName}.Value.Value); }}";
                                        break;
                                    default:
                                        output = $"if ({fieldName}.HasValue) {{ fields.{fieldAddMethod}({field.FieldId}, {fieldName}.Value); }}";
                                        break;
                                }

                                break;
                            case ValueType.Type:
                                output = $"if ({fieldName}.HasValue) {{ {fieldName}.Value.ApplyToSchemaObject(fields.{fieldAddMethod}({field.FieldId})); }}";
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }

                        break;
                    case FieldType.List:
                        string addType;
                        switch (field.ListType.InnerType.ValueTypeSelector)
                        {
                            case ValueType.Enum:
                                addType = $"fields.AddEnum({field.FieldId}, (uint)value);";
                                break;
                            case ValueType.Primitive:
                                switch (field.ListType.InnerType.Primitive)
                                {
                                    case PrimitiveType.EntityId:
                                        addType = $"fields.Add{field.ListType.InnerType.Primitive}({field.FieldId}, value.Value);";
                                        break;
                                    default:
                                        addType = $"fields.Add{field.ListType.InnerType.Primitive}({field.FieldId}, value);";
                                        break;
                                }

                                break;
                            case ValueType.Type:
                                addType = $"value.ApplyToSchemaObject(fields.AddObject({field.FieldId}));";
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }

                        output =
                            $@"if ( {fieldName} != null)
{{
    foreach(var value in {fieldName})
    {{
        {addType}
    }}
}}";

                        break;
                    case FieldType.Map:
                        string setKeyType;
                        string setValueType;

                        switch (field.MapType.KeyType.ValueTypeSelector)
                        {
                            case ValueType.Enum:
                                setKeyType = "kvPair.AddEnum(global::Improbable.Worker.CInterop.SchemaObject.SchemaMapKeyFieldId, (uint)kv.Key);";
                                break;
                            case ValueType.Primitive:
                                switch (field.MapType.KeyType.Primitive)
                                {
                                    case PrimitiveType.EntityId:
                                        setKeyType = $"kvPair.Add{field.MapType.KeyType.Primitive}(global::Improbable.Worker.CInterop.SchemaObject.SchemaMapKeyFieldId, kv.Key.Value);";
                                        break;
                                    default:
                                        setKeyType = $"kvPair.Add{field.MapType.KeyType.Primitive}(global::Improbable.Worker.CInterop.SchemaObject.SchemaMapKeyFieldId, kv.Key);";
                                        break;
                                }

                                break;
                            case ValueType.Type:
                                setKeyType = "kv.Key.ApplyToSchemaObject(kvPair.AddObject(global::Improbable.Worker.CInterop.SchemaObject.SchemaMapKeyFieldId));";
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }

                        switch (field.MapType.ValueType.ValueTypeSelector)
                        {
                            case ValueType.Enum:
                                setValueType = "kvPair.AddEnum(global::Improbable.Worker.CInterop.SchemaObject.SchemaMapValueFieldId, (uint)kv.Value);";
                                break;
                            case ValueType.Primitive:
                                switch (field.MapType.ValueType.Primitive)
                                {
                                    case PrimitiveType.EntityId:
                                        setValueType = $"kvPair.Add{field.MapType.ValueType.Primitive}(global::Improbable.Worker.CInterop.SchemaObject.SchemaMapValueFieldId, kv.Value.Value);";
                                        break;
                                    default:
                                        setValueType = $"kvPair.Add{field.MapType.ValueType.Primitive}(global::Improbable.Worker.CInterop.SchemaObject.SchemaMapValueFieldId, kv.Value);";
                                        break;
                                }

                                break;
                            case ValueType.Type:
                                setValueType = "kv.Value.ApplyToSchemaObject(kvPair.AddObject(global::Improbable.Worker.CInterop.SchemaObject.SchemaMapValueFieldId));";
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }

                        output =
                            $@"if ( {fieldName} != null)
{{
    foreach(var kv in {fieldName})
    {{
        var kvPair = fields.AddObject({field.FieldId});
        {setKeyType}
        {setValueType}
    }}
}}";
                        break;
                    case FieldType.Singular:
                        switch (field.SingularType.Type.ValueTypeSelector)
                        {
                            case ValueType.Enum:
                                output = $"fields.{fieldAddMethod}({field.FieldId}, (uint) {fieldName});";
                                break;
                            case ValueType.Primitive:
                                switch (field.SingularType.Type.Primitive)
                                {
                                    case PrimitiveType.EntityId:
                                        output = $"fields.{fieldAddMethod}({field.FieldId}, {fieldName}.Value);";
                                        break;
                                    default:
                                        output = $"fields.{fieldAddMethod}({field.FieldId}, {fieldName});";
                                        break;
                                }

                                break;
                            case ValueType.Type:
                                output = $"{fieldName}.ApplyToSchemaObject(fields.{fieldAddMethod}({field.FieldId}));";
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }

                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                update.AppendLine(output);
            }

            return $@"
internal void ApplyToSchemaObject(global::Improbable.Worker.CInterop.SchemaObject fields)
{{
{Indent(1, update.ToString().TrimEnd())}
}}";
        }

        private static string GetAssignmentForField(TypeDescription type, FieldDefinition field, string fieldName, Bundle bundle)
        {
            var fieldAccessor = GetFieldGetMethod(field);
            var fieldCount = GetFieldCountMethod(field);
            string output;

            switch (field.TypeSelector)
            {
                case FieldType.Option:
                    switch (field.OptionType.InnerType.ValueTypeSelector)
                    {
                        case ValueType.Enum:
                            output = $"{fieldName} = ({CapitalizeNamespace(field.OptionType.InnerType.Enum)})fields.{fieldAccessor}({field.FieldId});";
                            break;
                        case ValueType.Primitive:
                            output = $"{fieldName} = fields.{fieldAccessor}({field.FieldId});";
                            break;
                        case ValueType.Type:
                            var objectType = CapitalizeNamespace(field.OptionType.InnerType.Type);
                            output =
                                $@"{fieldName} = new {objectType}(fields.{fieldAccessor}({field.FieldId}));";
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    output = $@"if (fields.GetObjectCount({field.FieldId}) > 0)
{{
    {output}
}}
else
{{
    {fieldName} = null;
}}";

                    output = $@"if (fields.GetObjectCount({field.FieldId}) > 0)
{{
    {output}
}}
else
{{
    {fieldName} = null;
}}";
                    break;
                case FieldType.List:
                    switch (field.ListType.InnerType.ValueTypeSelector)
                    {
                        case ValueType.Enum:
                            var name = CapitalizeNamespace(field.ListType.InnerType.Enum);
                            output =
                                $@"{{
    var elements = fields.{fieldAccessor}({field.FieldId});
    {fieldName} = global::System.Collections.Immutable.ImmutableArray<{name}>.Empty;
    for (uint i = 0; i < elements.Length; i++)
    {{
        {fieldName} = {fieldName}.Add(({name}) elements[i]);
    }}
}}";
                            break;
                        case ValueType.Primitive:
                            switch (field.ListType.InnerType.Primitive)
                            {
                                case PrimitiveType.Bytes:
                                    output =
                                        $@"{{
    var count = fields.GetBytesCount({field.FieldId});
    {fieldName} = global::System.Collections.Immutable.ImmutableArray<byte[]>.Empty;
    for (uint i = 0; i < count; i++)
    {{
        {fieldName} = {fieldName}.Add(fields.IndexBytes({field.FieldId}, i));
    }}
}}";
                                    break;
                                case PrimitiveType.String:
                                    output =
                                        $@"{{
    var count = fields.GetStringCount({field.FieldId});
    {fieldName} = global::System.Collections.Immutable.ImmutableArray<string>.Empty;

    for (uint i = 0; i < count; i++)
    {{
        {fieldName} = {fieldName}.Add(fields.IndexString({field.FieldId}, i));
    }}
}}";
                                    break;
                                case PrimitiveType.EntityId:
                                    output =
                                        $@"{{
    var count = fields.GetEntityIdCount({field.FieldId});
    {fieldName} = global::System.Collections.Immutable.ImmutableArray<{EntityIdType}>.Empty;

    for (uint i = 0; i < count; i++)
    {{
        {fieldName} = {fieldName}.Add(new {EntityIdType}(fields.IndexEntityId({field.FieldId}, i)));
    }}
}}";
                                    break;
                                default:
                                    output = $"{fieldName} = {GetEmptyFieldInstantiationAsCsharp(type, field)}.AddRange(fields.{fieldAccessor}({field.FieldId}));";
                                    break;
                            }

                            break;
                        case ValueType.Type:
                            var typeName = CapitalizeNamespace(field.ListType.InnerType.Type);
                            if (IsFieldRecursive(type, field))
                            {
                                output =
                                    $@"var local{fieldName} = {GetEmptyFieldInstantiationAsCsharp(type, field)};
{fieldName} = local{fieldName};

for(uint i = 0; i < fields.{fieldCount}({field.FieldId}); i++)
{{
    local{fieldName}.Add(new {typeName}(fields.{fieldAccessor}({field.FieldId}, i)));
}}";
                            }
                            else
                            {
                                output =
                                    $@"
{fieldName} = {GetEmptyFieldInstantiationAsCsharp(type, field)};
for(uint i = 0; i < fields.{fieldCount}({field.FieldId}); i++)
{{
    {fieldName} = {fieldName}.Add(new {typeName}(fields.{fieldAccessor}({field.FieldId}, i)));
}}";
                            }

                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    break;
                case FieldType.Map:
                    string getKeyType;
                    string getValueType;

                    switch (field.MapType.KeyType.ValueTypeSelector)
                    {
                        case ValueType.Enum:
                            getKeyType = $"({CapitalizeNamespace(field.MapType.KeyType.Enum)}) kvPair.GetEnum(global::Improbable.Worker.CInterop.SchemaObject.SchemaMapKeyFieldId)";
                            break;
                        case ValueType.Primitive:
                            getKeyType = $"kvPair.Get{field.MapType.KeyType.Primitive}(global::Improbable.Worker.CInterop.SchemaObject.SchemaMapKeyFieldId)";
                            break;
                        case ValueType.Type:
                            getKeyType = $"new {CapitalizeNamespace(field.MapType.KeyType.Type)}(kvPair.GetObject(global::Improbable.Worker.CInterop.SchemaObject.SchemaMapKeyFieldId))";
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    switch (field.MapType.ValueType.ValueTypeSelector)
                    {
                        case ValueType.Enum:
                            getValueType = $"({CapitalizeNamespace(field.MapType.ValueType.Enum)}) kvPair.GetEnum(global::Improbable.Worker.CInterop.SchemaObject.SchemaMapValueFieldId)";
                            break;
                        case ValueType.Primitive:
                            getValueType = $"kvPair.Get{field.MapType.ValueType.Primitive}(global::Improbable.Worker.CInterop.SchemaObject.SchemaMapValueFieldId)";
                            break;
                        case ValueType.Type:
                            getValueType = $"new {CapitalizeNamespace(field.MapType.ValueType.Type)}(kvPair.GetObject(global::Improbable.Worker.CInterop.SchemaObject.SchemaMapValueFieldId))";
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    if (IsFieldRecursive(type, field))
                    {
                        output =
                            $@"var local{fieldName} = {GetEmptyFieldInstantiationAsCsharp(type, field)};
{fieldName} = local{fieldName};

for(uint i = 0; i < fields.{fieldCount}({field.FieldId}); i++)
{{
    var kvPair = fields.IndexObject({field.FieldId}, i);
    local{fieldName}.Add({getKeyType}, {getValueType});
}}";
                    }
                    else
                    {
                        output =
                            $@"{fieldName} = {GetEmptyFieldInstantiationAsCsharp(type, field)};

for(uint i = 0; i < fields.{fieldCount}({field.FieldId}); i++)
{{
    var kvPair = fields.IndexObject({field.FieldId}, i);
    {fieldName} = {fieldName}.Add({getKeyType}, {getValueType});
}}";
                    }

                    break;
                case FieldType.Singular:
                    switch (field.SingularType.Type.ValueTypeSelector)
                    {
                        case ValueType.Enum:
                            output = $"{fieldName} = ({CapitalizeNamespace(field.SingularType.Type.Enum)})fields.{fieldAccessor}({field.FieldId});";
                            break;
                        case ValueType.Primitive:
                            switch (field.SingularType.Type.Primitive)
                            {
                                case PrimitiveType.Bytes:
                                    output = $"{fieldName} = ({SchemaToCSharpTypes[field.SingularType.Type.Primitive]}) fields.{fieldAccessor}({field.FieldId}).Clone();";
                                    break;
                                default:
                                    output = $"{fieldName} = fields.{fieldAccessor}({field.FieldId});";
                                    break;
                            }

                            break;
                        case ValueType.Type:
                            var objectType = CapitalizeNamespace(field.SingularType.Type.Type);
                            output = $"{fieldName} = new {objectType}(fields.{fieldAccessor}({field.FieldId}));";
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return output;
        }

        private static string GenerateFromUpdate(TypeDescription type, uint componentId, Bundle bundle)
        {
            var typeName = GetPascalCaseNameFromTypeName(type.QualifiedName);

            var text = new StringBuilder();
            var sb = new StringBuilder();
            foreach (var field in type.Fields)
            {
                var fieldName = $"{SnakeCaseToPascalCase(field.Name)}";

                var output = GetAssignmentForField(type, field, fieldName, bundle);
                var fieldCount = GetFieldCountMethod(field);

                string guard;

                switch (field.TypeSelector)
                {
                    case FieldType.Option:
                        guard = $@"if (fields.{fieldCount}({field.FieldId}) > 0)
{{
{Indent(1, output)}
}}
else if(FieldIsCleared({field.FieldId}, clearedFields))
{{
    {fieldName} = {GetEmptyFieldInstantiationAsCsharp(type, field)};
}}";
                        break;
                    case FieldType.List:
                    case FieldType.Map:
                        guard = $@"if (fields.{fieldCount}({field.FieldId}) > 0)
{{
{Indent(1, output)}
}}
else if(FieldIsCleared({field.FieldId}, clearedFields))
{{
    {fieldName} = {GetEmptyFieldInstantiationAsCsharp(type, field)};
}}";
                        break;
                    case FieldType.Singular:
                        guard = $@"if (fields.{fieldCount}({field.FieldId}) > 0)
{{
{Indent(1, output)}
}}";
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                sb.AppendLine(guard);
            }

            text.AppendLine($@"internal {typeName}({typeName} source, {SchemaComponentUpdate} update)
{{
    var fields = update.GetFields();
    var clearedFields = update.GetClearedFields();
    this = source;

{Indent(1, sb.ToString().TrimEnd())}
}}");

            text.AppendLine(
                $@"private static bool FieldIsCleared(uint fieldId, uint[] fields)
{{
    for (var i = 0; i < fields.Length; i++)
    {{
        if (fields[i] == fieldId)
        {{
            return true;
        }}
    }}

    return false;
}}

public static {typeName} Create(global::Improbable.Worker.CInterop.SchemaComponentData? fields)
{{
    return fields.HasValue ? new {typeName}(fields.Value.GetFields()) : new {typeName}();
}}

public static {typeName} CreateFromSnapshot(global::Improbable.Worker.CInterop.Entity snapshotEntity)
{{
    var component = snapshotEntity.Get(ComponentId);
    if (component.HasValue)
    {{
        return Create(component.Value.SchemaData);
    }}
    return new {typeName}();
}}

internal static {typeName} ApplyUpdate({typeName} source, {SchemaComponentUpdate}? update)
{{
    return update.HasValue ? new {typeName}(source, update.Value) : source;
}}

public global::Improbable.Worker.CInterop.ComponentData ToData()
{{
    var schemaData = new global::Improbable.Worker.CInterop.SchemaComponentData({componentId});
    ApplyToSchemaObject(schemaData.GetFields());

    return new global::Improbable.Worker.CInterop.ComponentData(schemaData);
}}");

            return text.ToString();
        }

        private static string GenerateUpdaters(IReadOnlyList<FieldDefinition> fields)
        {
            var text = new StringBuilder();
            foreach (var field in fields)
            {
                var fieldAddMethod = GetFieldAddMethod(field);

                string output;
                switch (field.TypeSelector)
                {
                    case FieldType.Option:
                        switch (field.OptionType.InnerType.ValueTypeSelector)
                        {
                            case ValueType.Enum:
                                output =
                                    $@"if (newValue.HasValue)
{{
    fields.{fieldAddMethod}({field.FieldId}, (uint)newValue.Value);
}}
else
{{
    update.AddClearedField({field.FieldId});
}}";
                                break;
                            case ValueType.Primitive:
                                switch (field.OptionType.InnerType.Primitive)
                                {
                                    case PrimitiveType.Bytes:
                                    case PrimitiveType.String:
                                        output = $@"if (newValue != null)
{{
    fields.{fieldAddMethod}({field.FieldId}, newValue);
}}
else
{{
    update.AddClearedField({field.FieldId});
}}";
                                        break;
                                    case PrimitiveType.EntityId:
                                        output =
                                            $@"if (newValue.HasValue)
{{
    fields.{fieldAddMethod}({field.FieldId}, newValue.Value.Value);
}}
else
{{
    update.AddClearedField({field.FieldId});
}}";
                                        break;
                                    default:
                                        output =
                                            $@"if (newValue.HasValue)
{{
    fields.{fieldAddMethod}({field.FieldId}, newValue.Value);
}}
else
{{
    update.AddClearedField({field.FieldId});
}}";
                                        break;
                                }

                                break;
                            case ValueType.Type:
                                output =
                                    $@"if (newValue.HasValue)
{{
    newValue.Value.ApplyToSchemaObject(fields.{fieldAddMethod}({field.FieldId}));
}}
else
{{
    update.AddClearedField({field.FieldId});
}}";
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }

                        break;
                    case FieldType.List:
                        string addType;
                        switch (field.ListType.InnerType.ValueTypeSelector)
                        {
                            case ValueType.Enum:
                                addType = $"fields.AddEnum({field.FieldId}, (uint)value);";
                                break;
                            case ValueType.Primitive:
                                switch (field.ListType.InnerType.Primitive)
                                {
                                    case PrimitiveType.EntityId:
                                        addType = $"fields.Add{field.ListType.InnerType.Primitive}({field.FieldId}, value.Value);";
                                        break;
                                    default:
                                        addType = $"fields.Add{field.ListType.InnerType.Primitive}({field.FieldId}, value);";
                                        break;
                                }

                                break;
                            case ValueType.Type:
                                addType = $"value.ApplyToSchemaObject(fields.AddObject({field.FieldId}));";
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }

                        output =
                            $@"var any = false;
if (newValue != null)
{{
    foreach(var value in newValue)
    {{
        {addType};
        any = true;
    }}
}}

if (!any)
{{
    update.AddClearedField({field.FieldId});
}}";

                        break;
                    case FieldType.Map:
                        string setKeyType;
                        string setValueType;

                        switch (field.MapType.KeyType.ValueTypeSelector)
                        {
                            case ValueType.Enum:
                                setKeyType = "kvPair.AddEnum(global::Improbable.Worker.CInterop.SchemaObject.SchemaMapKeyFieldId, (uint)kv.Key);";
                                break;
                            case ValueType.Primitive:
                                switch (field.MapType.KeyType.Primitive)
                                {
                                    case PrimitiveType.EntityId:
                                        setKeyType = $"kvPair.Add{field.MapType.KeyType.Primitive}(global::Improbable.Worker.CInterop.SchemaObject.SchemaMapKeyFieldId, kv.Key.Value);";
                                        break;
                                    default:
                                        setKeyType = $"kvPair.Add{field.MapType.KeyType.Primitive}(global::Improbable.Worker.CInterop.SchemaObject.SchemaMapKeyFieldId, kv.Key);";
                                        break;
                                }

                                break;
                            case ValueType.Type:
                                setKeyType = "kv.Key.ApplyToSchemaObject(kvPair.AddObject(global::Improbable.Worker.CInterop.SchemaObject.SchemaMapKeyFieldId));";
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }

                        switch (field.MapType.ValueType.ValueTypeSelector)
                        {
                            case ValueType.Enum:
                                setValueType = "kvPair.AddEnum(global::Improbable.Worker.CInterop.SchemaObject.SchemaMapValueFieldId, (uint)kv.Value);";
                                break;
                            case ValueType.Primitive:
                                switch (field.MapType.ValueType.Primitive)
                                {
                                    case PrimitiveType.EntityId:
                                        setValueType = $"kvPair.Add{field.MapType.ValueType.Primitive}(global::Improbable.Worker.CInterop.SchemaObject.SchemaMapValueFieldId, kv.Value.Value);";
                                        break;
                                    default:
                                        setValueType = $"kvPair.Add{field.MapType.ValueType.Primitive}(global::Improbable.Worker.CInterop.SchemaObject.SchemaMapValueFieldId, kv.Value);";
                                        break;
                                }

                                break;
                            case ValueType.Type:
                                setValueType = "kv.Value.ApplyToSchemaObject(kvPair.AddObject(global::Improbable.Worker.CInterop.SchemaObject.SchemaMapValueFieldId));";
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }

                        output =
                            $@"var any = false;
if (newValue != null)
{{
    foreach(var kv in newValue)
    {{
        any = true;
        var kvPair = fields.AddObject({field.FieldId});
        {setKeyType}
        {setValueType}
    }}
}}

if (!any)
{{
    update.AddClearedField({field.FieldId});
}}";
                        break;
                    case FieldType.Singular:
                        switch (field.SingularType.Type.ValueTypeSelector)
                        {
                            case ValueType.Enum:
                                output = $"fields.{fieldAddMethod}({field.FieldId}, (uint)newValue);";
                                break;
                            case ValueType.Primitive:
                                switch (field.SingularType.Type.Primitive)
                                {
                                    case PrimitiveType.EntityId:
                                        output = $"fields.{fieldAddMethod}({field.FieldId}, newValue.Value);";

                                        break;
                                    default:
                                        output = $"fields.{fieldAddMethod}({field.FieldId}, newValue);";
                                        break;
                                }

                                break;
                            case ValueType.Type:
                                output = $"newValue.ApplyToSchemaObject(fields.{fieldAddMethod}({field.FieldId}));";
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }

                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                text.AppendLine($@"
internal static void Update{SnakeCaseToPascalCase(field.Name)}({SchemaComponentUpdate} update, {GetParameterTypeAsCsharp(field)} newValue)
{{
    var fields = update.GetFields();
{Indent(1, output.TrimEnd())}
}}");
            }

            return text.ToString();
        }

        private static string GenerateCreateGetEvents(IReadOnlyList<ComponentDefinition.EventDefinition> events)
        {
            if (events == null || events.Count == 0)
            {
                return string.Empty;
            }

            var parameters = new StringBuilder();
            var eventGetters = new StringBuilder();
            foreach (var evt in events)
            {
                var eventPayloadType = CapitalizeNamespace(CapitalizeNamespace(evt.Type));
                var identifierName = FieldNameToSafeName(SnakeCaseToCamelCase(evt.Name));
                parameters.Append($",\n\t\tout global::System.Collections.Immutable.ImmutableArray<{eventPayloadType}> {identifierName}");

                eventGetters.Append($@"{identifierName} = global::System.Collections.Immutable.ImmutableArray<{eventPayloadType}>.Empty;
for (uint i = 0; i < events.GetObjectCount({evt.EventIndex}); i++)
{{
    {identifierName} = {identifierName}.Add(new {eventPayloadType}(events.IndexObject({evt.EventIndex}, i)));
}}
");
            }

            return $@"
public static bool TryGetEvents({SchemaComponentUpdate} update{parameters})
{{
    var events = update.GetEvents();

{Indent(1, eventGetters.ToString().TrimEnd())}
    return {string.Join($"{Indent(2, "&& ")}\n", events.Select(evt => $"!{FieldNameToSafeName(SnakeCaseToCamelCase(evt.Name))}.IsDefaultOrEmpty"))};
}}";
        }
        
        private string GenerateUpdateStruct(TypeDescription type, IReadOnlyList<FieldDefinition> fields)
        {
            var fieldText = new StringBuilder();

            var typeName = GetPascalCaseNameFromTypeName(type.QualifiedName);
            var typeNamespace = GetPascalCaseNamespaceFromTypeName(type.QualifiedName);

            foreach (var field in fields)
            {
                fieldText.AppendLine($"private {GetFieldTypeAsCsharp(type, field)} {FieldNameToSafeName(SnakeCaseToCamelCase(field.Name))};");
                fieldText.AppendLine($"private bool was{SnakeCaseToPascalCase(field.Name)}Updated;");
            }

            foreach (var ev in type.Events)
            {
                fieldText.AppendLine($"private global::System.Collections.Generic.List<global::{CapitalizeNamespace(ev.Type)}> {SnakeCaseToCamelCase(ev.Name)}Events;");
            }

            var setMethodText = new StringBuilder();

            foreach (var field in fields)
            {
                setMethodText.AppendLine($@"public Update Set{SnakeCaseToPascalCase(field.Name)}({GetParameterTypeAsCsharp(field)} {FieldNameToSafeName(SnakeCaseToCamelCase(field.Name))})
{{
    this.{FieldNameToSafeName(SnakeCaseToCamelCase(field.Name))} = {ParameterConversion(type, field)};
    was{SnakeCaseToPascalCase(field.Name)}Updated = true;
    return this;
}}
");
            }

            foreach (var ev in type.Events)
            {
                setMethodText.AppendLine($@"public Update Add{SnakeCaseToPascalCase(ev.Name)}Event(global::{CapitalizeNamespace(ev.Type)} ev)
{{
    this.{SnakeCaseToCamelCase(ev.Name)}Events = this.{SnakeCaseToCamelCase(ev.Name)}Events ?? new global::System.Collections.Generic.List<global::{CapitalizeNamespace(ev.Type)}>();
    this.{SnakeCaseToCamelCase(ev.Name)}Events.Add(ev);
    return this;
}}
");
            }

            var toUpdateMethodBody = new StringBuilder();

            foreach (var field in fields)
            {
                toUpdateMethodBody.AppendLine($@"if (was{SnakeCaseToPascalCase(field.Name)}Updated)
{{
    global::{typeNamespace}.{typeName}.Update{SnakeCaseToPascalCase(field.Name)}(update, {FieldNameToSafeName(SnakeCaseToCamelCase(field.Name))});
}}
");
            }

            foreach (var ev in type.Events)
            {
                toUpdateMethodBody.AppendLine($@"if (this.{SnakeCaseToCamelCase(ev.Name)}Events != null)
{{
    var events = update.GetEvents();

    foreach (var ev in this.{SnakeCaseToCamelCase(ev.Name)}Events)
    {{
        ev.ApplyToSchemaObject(events.AddObject({ev.EventIndex}));
    }}
}}");
            }

            return $@"public partial struct Update
{{
{Indent(1, fieldText.ToString().TrimEnd())}

{Indent(1, setMethodText.ToString().TrimEnd())}

    public {SchemaComponentUpdate} ToSchemaUpdate()
    {{
        var update = new {SchemaComponentUpdate}(global::{typeNamespace}.{typeName}.ComponentId);

{Indent(2, toUpdateMethodBody.ToString().TrimEnd())}

        return update;
    }}
}}
";
        }

        private string GenerateSchemaConstructor(TypeDescription type, IReadOnlyList<FieldDefinition> fields)
        {
            var typeName = GetPascalCaseNameFromTypeName(type.QualifiedName);

            var parameters = new StringBuilder();
            var initializers = new StringBuilder();
            var text = new StringBuilder();

            // If all types are options, default all values to null to allow for easy "one-of" initialization.
            var nullDefault = fields.All(f => f.TypeSelector == FieldType.Option) ? " = null" : string.Empty;

            parameters.Append(string.Join(", ", fields.Select(f => $"{GetParameterTypeAsCsharp(f)} {FieldNameToSafeName(SnakeCaseToCamelCase(f.Name))}{nullDefault}")));
            initializers.AppendLine(string.Join(Environment.NewLine, fields.Select(f => $"{SnakeCaseToPascalCase(f.Name)} = {ParameterConversion(type, f)};")));

            if (fields.Count > 0)
            {
                text.Append($@"
public {typeName}({parameters})
{{
{Indent(1, initializers.ToString().TrimEnd())}
}}");
            }

            return text.ToString();
        }
    }
}
