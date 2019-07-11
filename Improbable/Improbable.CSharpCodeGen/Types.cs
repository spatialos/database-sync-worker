using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Improbable.Schema.Bundle;
using static Improbable.CSharpCodeGen.Case;
using ValueType = Improbable.Schema.Bundle.ValueType;

namespace Improbable.CSharpCodeGen
{
    public static class Types
    {
        public const string GenericIEnumerable = "global::System.Collections.Generic.IEnumerable";
        public const string SchemaComponentUpdate = "global::Improbable.Worker.CInterop.SchemaComponentUpdate";
        public const string EntityIdType = "global::Improbable.Stdlib.EntityId";

        public static Dictionary<PrimitiveType, string> SchemaToCSharpTypes = new Dictionary<PrimitiveType, string>
        {
            {PrimitiveType.Double, "double"},
            {PrimitiveType.Float, "float"},
            {PrimitiveType.Int32, "int"},
            {PrimitiveType.Int64, "long"},
            {PrimitiveType.Uint32, "uint"},
            {PrimitiveType.Uint64, "ulong"},
            {PrimitiveType.Sint32, "int"},
            {PrimitiveType.Sint64, "long"},
            {PrimitiveType.Fixed32, "uint"},
            {PrimitiveType.Fixed64, "ulong"},
            {PrimitiveType.Sfixed32, "int"},
            {PrimitiveType.Sfixed64, "long"},
            {PrimitiveType.Bool, "bool"},
            {PrimitiveType.String, "string"},
            {PrimitiveType.Bytes, "byte[]"},
            {PrimitiveType.EntityId, EntityIdType}
        };

        public static Dictionary<PrimitiveType, Func<string, string>> SchemaToHashFunction = new Dictionary<PrimitiveType, Func<string, string>>
        {
            {PrimitiveType.Double, f => $"{f}.GetHashCode()"},
            {PrimitiveType.Float, f => $"{f}.GetHashCode()"},
            {PrimitiveType.Int32, f => $"(int){f}"},
            {PrimitiveType.Int64, f => $"(int){f}"},
            {PrimitiveType.Uint32, f => $"(int){f}"},
            {PrimitiveType.Uint64, f => $"(int){f}"},
            {PrimitiveType.Sint32, f => f},
            {PrimitiveType.Sint64, f => $"(int){f}"},
            {PrimitiveType.Fixed32, f => $"(int){f}"},
            {PrimitiveType.Fixed64, f => $"(int){f}"},
            {PrimitiveType.Sfixed32, f => f},
            {PrimitiveType.Sfixed64, f => $"(int){f}"},
            {PrimitiveType.Bool, f => $"{f}.GetHashCode()"},
            {PrimitiveType.String, f => $"{f} != null ? {f}.GetHashCode() : 0"},
            {PrimitiveType.Bytes, f => $"{f}.GetHashCode()"},
            {PrimitiveType.EntityId, f => $"{f}.GetHashCode()"}
        };

        public static Dictionary<PrimitiveType, Func<string, string>> SchemaToEqualsFunction = new Dictionary<PrimitiveType, Func<string, string>>
        {
            {PrimitiveType.Double, f => $"{f}.Equals(other.{f})"},
            {PrimitiveType.Float, f => $"{f}.Equals(other.{f})"},
            {PrimitiveType.Int32, f => $"{f} == other.{f}"},
            {PrimitiveType.Int64, f => $"{f} == other.{f}"},
            {PrimitiveType.Uint32, f => $"{f} == other.{f}"},
            {PrimitiveType.Uint64, f => $"{f} == other.{f}"},
            {PrimitiveType.Sint32, f => $"{f} == other.{f}"},
            {PrimitiveType.Sint64, f => $"{f} == other.{f}"},
            {PrimitiveType.Fixed32, f => $"{f} == other.{f}"},
            {PrimitiveType.Fixed64, f => $"{f} == other.{f}"},
            {PrimitiveType.Sfixed32, f => $"{f} == other.{f}"},
            {PrimitiveType.Sfixed64, f => $"{f} == other.{f}"},
            {PrimitiveType.Bool, f => $"{f} == other.{f}"},
            {PrimitiveType.String, f => $"string.Equals({f}, other.{f})"},
            {PrimitiveType.Bytes, f => $"Equals({f}, other.{f})"},
            {PrimitiveType.EntityId, f => $"{EntityIdType}.Equals({f}, other.{f})"}
        };

        public static HashSet<string> Keywords = new HashSet<string>
        {
            "abstract",
            "as",
            "base",
            "bool",
            "break",
            "byte",
            "case",
            "catch",
            "char",
            "checked",
            "class",
            "const",
            "continue",
            "decimal",
            "default",
            "delegate",
            "do",
            "double",
            "else",
            "enum",
            "event",
            "explicit",
            "extern",
            "false",
            "finally",
            "fixed",
            "float",
            "for",
            "foreach",
            "goto",
            "if",
            "implicit",
            "in",
            "int",
            "interface",
            "internal",
            "is",
            "lock",
            "long",
            "namespace",
            "new",
            "null",
            "object",
            "operator",
            "out",
            "override",
            "params",
            "private",
            "protected",
            "public",
            "readonly",
            "ref",
            "return",
            "sbyte",
            "sealed",
            "short",
            "sizeof",
            "stackalloc",
            "static",
            "string",
            "struct",
            "switch",
            "this",
            "throw",
            "true",
            "try",
            "typeof",
            "uint",
            "ulong",
            "unchecked",
            "unsafe",
            "ushort",
            "using",
            "var",
            "virtual",
            "void",
            "volatile",
            "while"
        };

        public static string GetOptionType(TypeReference type)
        {
            switch (type.ValueTypeSelector)
            {
                case ValueType.Enum:
                    return "?";
                case ValueType.Primitive:
                    switch (type.Primitive)
                    {
                        case PrimitiveType.String:
                        case PrimitiveType.Bytes:
                            return string.Empty;
                        default:
                            return "?";
                    }

                case ValueType.Type:
                    return "?";
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static string GetOptionTest(TypeReference type)
        {
            switch (type.ValueTypeSelector)
            {
                case ValueType.Enum:
                    return ".HasValue";
                case ValueType.Primitive:
                    switch (type.Primitive)
                    {
                        case PrimitiveType.String:
                        case PrimitiveType.Bytes:
                            return " != null";
                        default:
                            return ".HasValue";
                    }

                case ValueType.Type:
                    return ".HasValue";
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static string GetOptionValue(TypeReference type)
        {
            switch (type.ValueTypeSelector)
            {
                case ValueType.Enum:
                    return ".Value";
                case ValueType.Primitive:
                    switch (type.Primitive)
                    {
                        case PrimitiveType.String:
                        case PrimitiveType.Bytes:
                            return string.Empty;
                        default:
                            return ".Value";
                    }

                case ValueType.Type:
                    return ".Value";
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static string GetFieldTypeAsCsharp(TypeDescription type, FieldDefinition field)
        {
            switch (field.TypeSelector)
            {
                case FieldType.Option:
                    return $"{TypeReferenceToType(field.OptionType.InnerType)}{GetOptionType(field.OptionType.InnerType)}";
                case FieldType.List:
                    if (IsFieldRecursive(type, field))
                    {
                        return $"global::System.Collections.Generic.IReadOnlyList<{TypeReferenceToType(field.ListType.InnerType)}>";
                    }
                    else
                    {
                        return $"global::System.Collections.Immutable.ImmutableArray<{TypeReferenceToType(field.ListType.InnerType)}>";
                    }

                case FieldType.Map:
                    if (IsFieldRecursive(type, field))
                    {
                        return $"global::System.Collections.Generic.IReadOnlyDictionary<{TypeReferenceToType(field.MapType.KeyType)}, {TypeReferenceToType(field.MapType.ValueType)}>";
                    }
                    else
                    {
                        return $"global::System.Collections.Immutable.ImmutableDictionary<{TypeReferenceToType(field.MapType.KeyType)}, {TypeReferenceToType(field.MapType.ValueType)}>";
                    }

                case FieldType.Singular:
                    return TypeReferenceToType(field.SingularType.Type);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static string GetParameterTypeAsCsharp(FieldDefinition field)
        {
            switch (field.TypeSelector)
            {
                case FieldType.Option:
                    return $"{TypeReferenceToType(field.OptionType.InnerType)}{GetOptionType(field.OptionType.InnerType)}";
                case FieldType.List:
                    return $"global::System.Collections.Generic.IEnumerable<{TypeReferenceToType(field.ListType.InnerType)}>";
                case FieldType.Map:
                    return $"global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<{TypeReferenceToType(field.MapType.KeyType)}, {TypeReferenceToType(field.MapType.ValueType)}>>";
                case FieldType.Singular:
                    return TypeReferenceToType(field.SingularType.Type);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static string ParameterConversion(TypeDescription type, FieldDefinition field)
        {
            switch (field.TypeSelector)
            {
                case FieldType.List:
                    if (IsFieldRecursive(type, field))
                    {
                        return $"new global::System.Collections.Generic.List<{TypeReferenceToType(field.ListType.InnerType)}>({FieldNameToSafeName(SnakeCaseToCamelCase(field.Name))})";
                    }
                    else
                    {
                        return $"global::System.Collections.Immutable.ImmutableArray.ToImmutableArray({FieldNameToSafeName(SnakeCaseToCamelCase(field.Name))})";
                    }

                case FieldType.Map:
                    if (IsFieldRecursive(type, field))
                    {
                        return $"global::System.Linq.Enumerable.ToDictionary({FieldNameToSafeName(SnakeCaseToCamelCase(field.Name))}, kv => kv.Key, kv => kv.Value)";
                    }
                    else
                    {
                        return $"global::System.Collections.Immutable.ImmutableDictionary.ToImmutableDictionary({FieldNameToSafeName(SnakeCaseToCamelCase(field.Name))})";
                    }

                default:
                    return $"{FieldNameToSafeName(SnakeCaseToCamelCase(field.Name))}";
            }
        }

        public static string GetTypeAsCsharp(TypeReference type)
        {
            switch (type.ValueTypeSelector)
            {
                case ValueType.Enum:
                    return CapitalizeNamespace(type.Enum);
                case ValueType.Primitive:
                    return SchemaToCSharpTypes[type.Primitive];
                case ValueType.Type:
                    return CapitalizeNamespace(type.Type);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static string GetEmptyFieldInstantiationAsCsharp(TypeDescription type, FieldDefinition field)
        {
            switch (field.TypeSelector)
            {
                case FieldType.Option:
                    return "null";
                case FieldType.List:
                    if (IsFieldRecursive(type, field))
                    {
                        return $"new global::System.Collections.Generic.List<{TypeReferenceToType(field.ListType.InnerType)}>()";
                    }
                    else
                    {
                        return $"global::System.Collections.Immutable.ImmutableArray<{TypeReferenceToType(field.ListType.InnerType)}>.Empty";
                    }

                case FieldType.Map:
                    if (IsFieldRecursive(type, field))
                    {
                        return $"new global::System.Collections.Generic.Dictionary<{TypeReferenceToType(field.MapType.KeyType)}, {TypeReferenceToType(field.MapType.ValueType)}>()";
                    }
                    else
                    {
                        return $"global::System.Collections.Immutable.ImmutableDictionary<{TypeReferenceToType(field.MapType.KeyType)}, {TypeReferenceToType(field.MapType.ValueType)}>.Empty";
                    }

                case FieldType.Singular:
                    return TypeReferenceToType(field.SingularType.Type);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static string TypeReferenceToType(TypeReference typeRef)
        {
            switch (typeRef.ValueTypeSelector)
            {
                case ValueType.Enum:
                    return $"global::{CapitalizeNamespace(typeRef.Enum)}";
                case ValueType.Primitive:
                    return SchemaToCSharpTypes[typeRef.Primitive];
                case ValueType.Type:
                    return $"global::{CapitalizeNamespace(typeRef.Type)}";
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static string FieldToHash(FieldDefinition field)
        {
            var fieldName = SnakeCaseToPascalCase(field.Name);

            switch (field.TypeSelector)
            {
                case FieldType.Option:
                    return $"{fieldName}{GetOptionTest(field.OptionType.InnerType)} ? {fieldName}{GetOptionValue(field.OptionType.InnerType)}.GetHashCode() : 0";
                case FieldType.List:
                case FieldType.Map:
                    return $"{fieldName} == null ? {fieldName}.GetHashCode() : 0";
                case FieldType.Singular:
                    if (field.SingularType.Type.Primitive == PrimitiveType.Invalid)
                    {
                        return $"{fieldName}.GetHashCode()";
                    }

                    return SchemaToHashFunction[field.SingularType.Type.Primitive](fieldName);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static string FieldToEquals(FieldDefinition field)
        {
            var fieldName = SnakeCaseToPascalCase(field.Name);

            switch (field.TypeSelector)
            {
                case FieldType.Option:
                    return $"{fieldName} == other.{fieldName}";
                case FieldType.List:
                case FieldType.Map:
                    return $"global::System.Collections.StructuralComparisons.StructuralEqualityComparer.Equals({fieldName}, other.{fieldName})";
                case FieldType.Singular:
                    if (field.SingularType.Type.Primitive == PrimitiveType.Invalid)
                    {
                        return $"{fieldName} == other.{fieldName}";
                    }

                    return SchemaToEqualsFunction[field.SingularType.Type.Primitive](fieldName);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static string GetFieldGetMethod(FieldDefinition field)
        {
            switch (field.TypeSelector)
            {
                case FieldType.Option:
                    switch (field.OptionType.InnerType.ValueTypeSelector)
                    {
                        case ValueType.Enum:
                            return "GetEnum";
                        case ValueType.Primitive:
                            return $"Get{field.OptionType.InnerType.Primitive}";
                        case ValueType.Type:
                            return "GetObject";
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                case FieldType.List:
                    switch (field.ListType.InnerType.ValueTypeSelector)
                    {
                        case ValueType.Enum:
                            return "GetEnumList";
                        case ValueType.Primitive:
                            return $"Get{field.ListType.InnerType.Primitive}List";
                        case ValueType.Type:
                            return "IndexObject";
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                case FieldType.Map:
                    return "IndexObject";

                case FieldType.Singular:
                    switch (field.SingularType.Type.ValueTypeSelector)
                    {
                        case ValueType.Enum:
                            return "GetEnum";
                        case ValueType.Primitive:
                            return $"Get{field.SingularType.Type.Primitive}";
                        case ValueType.Type:
                            return "GetObject";
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static string GetFieldAddMethod(FieldDefinition field)
        {
            switch (field.TypeSelector)
            {
                case FieldType.Option:
                    switch (field.OptionType.InnerType.ValueTypeSelector)
                    {
                        case ValueType.Enum:
                            return "AddEnum";
                        case ValueType.Primitive:
                            return $"Add{field.OptionType.InnerType.Primitive}";
                        case ValueType.Type:
                            return "AddObject";
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                case FieldType.List:
                    switch (field.ListType.InnerType.ValueTypeSelector)
                    {
                        case ValueType.Enum:
                            return "AddEnumList";
                        case ValueType.Primitive:
                            return $"Add{field.ListType.InnerType.Primitive}List";
                        case ValueType.Type:
                            return "AddObject";
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                case FieldType.Map:
                    return "GetObject";

                case FieldType.Singular:
                    switch (field.SingularType.Type.ValueTypeSelector)
                    {
                        case ValueType.Enum:
                            return "AddEnum";
                        case ValueType.Primitive:
                            return $"Add{field.SingularType.Type.Primitive}";
                        case ValueType.Type:
                            return "AddObject";
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static string GetFieldCountMethod(FieldDefinition field)
        {
            switch (field.TypeSelector)
            {
                case FieldType.Option:
                    switch (field.OptionType.InnerType.ValueTypeSelector)
                    {
                        case ValueType.Enum:
                            return "GetEnumCount";
                        case ValueType.Primitive:
                            return $"Get{field.OptionType.InnerType.Primitive}Count";
                        case ValueType.Type:
                            return "GetObjectCount";
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                case FieldType.List:
                    switch (field.ListType.InnerType.ValueTypeSelector)
                    {
                        case ValueType.Enum:
                            return "GetEnumCount";
                        case ValueType.Primitive:
                            return $"Get{field.ListType.InnerType.Primitive}Count";
                        case ValueType.Type:
                            return "GetObjectCount";
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                case FieldType.Map:
                    return "GetObjectCount";

                case FieldType.Singular:
                    switch (field.SingularType.Type.ValueTypeSelector)
                    {
                        case ValueType.Enum:
                            return "GetEnumCount";
                        case ValueType.Primitive:
                            return $"Get{field.SingularType.Type.Primitive}Count";
                        case ValueType.Type:
                            return "GetObjectCount";
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static string TypeToFilename(string qualifiedName)
        {
            var path = qualifiedName.Split('.');

            var folder = Path.Combine(path.Take(path.Length - 1).Select(SnakeCaseToPascalCase).Select(CapitalizeFirstLetter).ToArray());
            return Path.Combine(folder, $"{path.Last()}.g.cs");
        }

        public static string FieldNameToSafeName(string name)
        {
            if (!Keywords.Contains(name))
            {
                return name;
            }

            return $"@{name}";
        }

        public static bool HasAnnotation(TypeDescription t, string attributeName)
        {
            return t.Annotations.Any(a => a.TypeValue.Type == attributeName);
        }

        public static bool IsFieldRecursive(TypeDescription type, FieldDefinition field)
        {
            switch (field.TypeSelector)
            {
                case FieldType.Option:
                    return field.OptionType.InnerType.ValueTypeSelector == ValueType.Type && field.OptionType.InnerType.Type == type.QualifiedName;
                case FieldType.List:
                    return field.ListType.InnerType.ValueTypeSelector == ValueType.Type && field.ListType.InnerType.Type == type.QualifiedName;
                case FieldType.Map:
                    return field.MapType.KeyType.ValueTypeSelector == ValueType.Type && field.MapType.KeyType.Type == type.QualifiedName ||
                           field.MapType.ValueType.ValueTypeSelector == ValueType.Type && field.MapType.ValueType.Type == type.QualifiedName;
                case FieldType.Singular:
                    return field.SingularType.Type.ValueTypeSelector == ValueType.Type && field.SingularType.Type.Type == type.QualifiedName;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
