using System.Collections.Generic;
using System.Linq;

namespace Improbable.Schema.Bundle
{
    public readonly struct Bundle
    {
        public SchemaBundle SchemaBundle { get; }
        public IReadOnlyDictionary<string, TypeDefinition> Types { get; }
        public IReadOnlyDictionary<string, EnumDefinition> Enums { get; }
        public IReadOnlyDictionary<string, ComponentDefinition> Components { get; }
        public IReadOnlyDictionary<string, SchemaFile> TypeToFile { get; }
        public IReadOnlyList<string> CommandTypes { get; }

        public Bundle(SchemaBundle bundle)
        {
            SchemaBundle = bundle;

            Components = bundle.SchemaFiles.SelectMany(f => f.Components).ToDictionary(c => c.QualifiedName, c => c);
            Types = bundle.SchemaFiles.SelectMany(f => f.Types).ToDictionary(t => t.QualifiedName, t => t);
            Enums = bundle.SchemaFiles.SelectMany(f => f.Enums).ToDictionary(t => t.QualifiedName, t => t);
            CommandTypes = bundle.SchemaFiles.SelectMany(f => f.Components)
                .SelectMany(c => c.Commands)
                .SelectMany(cmd => new[] {cmd.RequestType, cmd.ResponseType}).ToList();

            var fileNameDict = new Dictionary<string, SchemaFile>();
            TypeToFile = fileNameDict;

            foreach (var file in bundle.SchemaFiles)
            {
                foreach (var type in file.Types)
                {
                    fileNameDict[type.QualifiedName] = file;
                }

                foreach (var type in file.Components)
                {
                    fileNameDict[type.QualifiedName] = file;
                }

                foreach (var type in file.Enums)
                {
                    fileNameDict[type.QualifiedName] = file;
                }
            }
        }
    }
}
