using System.Collections.Generic;
using System.Linq;
using Improbable.Schema.Bundle;

namespace Improbable.CSharpCodeGen
{
    public static class Annotations
    {
        public static bool HasAnnotations(FieldDefinition f, params string[] attributeNames)
        {
            return f.Annotations.Select(a => a.TypeValue.Type).Intersect(attributeNames).Any();
        }

        public static IEnumerable<string> GetAnnotationStrings(this IEnumerable<Annotation> annotations, string attributeName, int fieldNumber)
        {
            var annotation = annotations.FirstOrDefault(a => a.TypeValue.Type == attributeName);
            if (annotation == null)
            {
                return new string[] { };
            }

            var list = annotation.TypeValue.Fields[fieldNumber].Value.ListValue.Values;
            return list.Select(v => v.StringValue);
        }

        public static string GetAnnotationString(this IEnumerable<Annotation> annotations, string attributeName, int fieldIndex)
        {
            var annotation = annotations.FirstOrDefault(a => a.TypeValue.Type == attributeName);
            return annotation != null ? annotation.TypeValue.Fields[fieldIndex].Value.StringValue : string.Empty;
        }

        public static IEnumerable<FieldDefinition> WithAnnotation(this IEnumerable<FieldDefinition> fields, string fieldIndex)
        {
            return fields.Where(f => HasAnnotations(f, fieldIndex));
        }
    }
}
