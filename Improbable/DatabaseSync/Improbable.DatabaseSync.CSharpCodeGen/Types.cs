using Improbable.CSharpCodeGen;
using Improbable.Schema.Bundle;

namespace Improbable.DatabaseSync.CSharpCodeGen
{
    public static class Types
    {
        public static bool IsFieldExposedToDatabase(FieldDefinition f)
        {
            return Annotations.HasAnnotations(f, WellKnownAnnotations.ValueAnnotation, WellKnownAnnotations.ValueListAnnotation);
        }
    }
}
