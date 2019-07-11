using Improbable.Schema.Bundle;

namespace Improbable.CSharpCodeGen
{
    public interface ICodeGenerator
    {
        string Generate(TypeDescription type);
    }
}
