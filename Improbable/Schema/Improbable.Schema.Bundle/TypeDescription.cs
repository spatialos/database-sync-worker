using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Improbable.Schema.Bundle
{
    [DebuggerDisplay("{" + nameof(QualifiedName) + "}")]
    public readonly struct TypeDescription
    {
        public readonly string QualifiedName;

        public readonly string OuterType;

        public readonly SourceReference SourceReference;

        public readonly SchemaFile SourceFile;

        public readonly IReadOnlyList<TypeDescription> NestedTypes;

        public readonly IReadOnlyList<EnumDefinition> NestedEnums;

        public readonly IReadOnlyList<FieldDefinition> Fields;

        public readonly IReadOnlyList<Annotation> Annotations;

        public readonly IReadOnlyList<ComponentDefinition.EventDefinition> Events;

        public readonly uint? ComponentId;

        public TypeDescription(string qualifiedName, Bundle bundle)
        {
            QualifiedName = qualifiedName;

            NestedEnums = bundle.Enums.Where(e => e.Value.OuterType == qualifiedName).Select(type => bundle.Enums[type.Key]).ToList();

            bundle.Components.TryGetValue(qualifiedName, out var component);
            ComponentId = component?.ComponentId;

            SourceFile = bundle.TypeToFile[qualifiedName];

            if (ComponentId.HasValue)
            {
                SourceReference = bundle.Components[qualifiedName].SourceReference;
                OuterType = string.Empty;
            }
            else
            {
                SourceReference = bundle.Types[qualifiedName].SourceReference;
                OuterType = bundle.Types[qualifiedName].OuterType;
            }

            NestedTypes = bundle.Types.Where(t => t.Value.OuterType == qualifiedName).Select(type =>
            {
                var t = bundle.Types[type.Key];
                return new TypeDescription(t.QualifiedName, bundle);
            }).ToList();

            Fields = component?.Fields;

            if (!string.IsNullOrEmpty(component?.DataDefinition))
            {
                // Inline fields into the component.
                Fields = bundle.Types[component.DataDefinition].Fields;
            }

            if (Fields == null)
            {
                if (ComponentId.HasValue)
                {
                    Fields = bundle.Components[qualifiedName].Fields;
                }
                else
                {
                    Fields = bundle.Types[qualifiedName].Fields;
                }
            }

            if (Fields == null)
            {
                throw new Exception("Internal error: no fields found");
            }

            Fields = Fields.Where(f =>
            {
                var allowed = !IsPrimitiveEntityField(f);

                if (!allowed)
                {
                    Console.WriteLine($"field '{qualifiedName}.{f.Name}' is the Entity type, which is currently unsupported.");
                }

                return allowed;
            }).ToList();

            Annotations = component != null ? component.Annotations : bundle.Types[qualifiedName].Annotations;
            Events = component?.Events;
        }

        private static bool IsPrimitiveEntityField(FieldDefinition f)
        {
            // The Entity primitive type is currently unsupported, and undocumented.
            // It is ignored for now.
            switch (f.TypeSelector)
            {
                case FieldType.Option:
                    return f.OptionType.InnerType.ValueTypeSelector == ValueType.Primitive && f.OptionType.InnerType.Primitive == PrimitiveType.Entity;
                case FieldType.List:
                    return f.ListType.InnerType.ValueTypeSelector == ValueType.Primitive && f.ListType.InnerType.Primitive == PrimitiveType.Entity;
                case FieldType.Map:
                    return f.MapType.KeyType.ValueTypeSelector == ValueType.Primitive && f.MapType.KeyType.Primitive == PrimitiveType.Entity || f.MapType.ValueType.ValueTypeSelector == ValueType.Primitive && f.MapType.ValueType.Primitive == PrimitiveType.Entity;
                case FieldType.Singular:
                    return f.SingularType.Type.ValueTypeSelector == ValueType.Primitive && f.SingularType.Type.Primitive == PrimitiveType.Entity;
                default:
                    return false;
            }
        }
    }
}
