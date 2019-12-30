using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using CommandLine;
using Improbable.CSharpCodeGen;
using Improbable.DatabaseSync.CSharpCodeGen;
using Improbable.Postgres.CSharpCodeGen;
using Improbable.Schema.Bundle;
using Improbable.Stdlib.CSharpCodeGen;
using Improbable.WorkerSdkInterop.CSharpCodeGen;
using Serilog;
using static Improbable.CSharpCodeGen.Case;
using Types = Improbable.CSharpCodeGen.Types;

namespace CSharpCodeGenerator
{
    public class Options
    {
        [Option("input-bundle", Required = true,
            HelpText = "The path to the JSON Bundle file output by the SpatialOS schema_compiler.")]
        public string InputBundle { get; set; } = null!;

        [Option("output-marker",
            HelpText = "The path to a file that is written when code is successfully generated. Useful for timestamp checking in build systems.")]
        public string OutputMarker { get; set; } = null!;

        [Option("output-dir", Required = true,
            HelpText = "The path to write the generated code to.")]
        public string OutputDir { get; set; } = null!;
    }

    internal class Program
    {
        private static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console().
                CreateLogger();

            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(Run)
                .WithNotParsed(errors =>
                {
                    foreach (var error in errors)
                    {
                        Log.Error(error.ToString());
                    }

                    Environment.ExitCode = 1;
                });
        }

        private static void Run(Options options)
        {

            Log.Information(Parser.Default.FormatCommandLine(options));

            var timer = new Stopwatch();
            timer.Start();

            try
            {
                try
                {
                    File.Delete(options.OutputMarker);
                }
                catch
                {
                    // Nothing interesting to do here.
                }

                var bundle = SchemaBundleLoader.LoadBundle(options.InputBundle);

                // Sort the types by the depth of their declaration, so that nested types are generated first so they can be used by their declaring type.
                var types = bundle.Types.Select(kv => new TypeDescription(kv.Key, bundle))
                    .Union(bundle.Components.Select(kv => new TypeDescription(kv.Key, bundle)))
                    .OrderByDescending(t => t.QualifiedName.Count(c => c == '.'))
                    .ToList();

                foreach (var w in types.SelectMany(t => t.Warnings))
                {
                    Log.Warning(w);
                }

                var baseGenerator = new Generator(bundle);
                baseGenerator.FieldDecorators.Add(DatabaseSyncGenerator.JsonPropertyDecorator);
                var generators = new List<ICodeGenerator>
                {
                    baseGenerator,
                    new StdlibGenerator(bundle),
                    new ComponentGenerator(),
                    new SchemaObjectGenerator(bundle),
                    new DatabaseSyncGenerator()
                };

                var allContent = new Dictionary<string, StringBuilder>();
                var nestedTypes = new HashSet<string>();

                // Pass 1: generate all type content.
                foreach (var t in types)
                {
                    if (!allContent.TryGetValue(t.QualifiedName, out var builder))
                    {
                        builder = allContent[t.QualifiedName] = new StringBuilder();
                    }

                    // Embed nested enums.
                    builder.AppendJoin(Environment.NewLine, t.NestedEnums.Select(e => GenerateEnum(e, bundle).TrimEnd()));

                    // Class implementation.
                    builder.AppendJoin(Environment.NewLine, generators.Select(g =>
                    {
                        var result = g.Generate(t).TrimEnd();
                        if (string.IsNullOrWhiteSpace(result))
                        {
                            return string.Empty;
                        }

                        return $@"
#region {g.GetType().FullName}

{result}

#endregion {g.GetType().FullName}
";

                    }).Where(s => !string.IsNullOrEmpty(s)));

                    // Nested types.
                    builder.AppendJoin(Environment.NewLine, t.NestedTypes.Select(e => GenerateType(e, allContent[e.QualifiedName].ToString(), bundle)));

                    nestedTypes.UnionWith(t.NestedTypes.Select(type => type.QualifiedName));
                    nestedTypes.UnionWith(t.NestedEnums.Select(type => type.QualifiedName));
                }

                // Pass 2: Generate final content.
                foreach (var t in types.Where(type => !nestedTypes.Contains(type.QualifiedName)))
                {
                    var content = allContent[t.QualifiedName];

                    WriteFile(options, Types.TypeToFilename(t.QualifiedName), $@"
namespace {GetPascalCaseNamespaceFromTypeName(t.QualifiedName)}
{{
{Indent(1, GenerateType(t, content.ToString().TrimEnd(), bundle))}
}}");
                }

                // Enums.
                foreach (var (key, value) in bundle.Enums.Where(type => !nestedTypes.Contains(type.Key)))
                {
                    WriteFile(options, Types.TypeToFilename(key), $@"
namespace {GetPascalCaseNamespaceFromTypeName(key)}
{{
{Indent(1, GenerateEnum(value, bundle))}
}}");
                }

                File.WriteAllText(options.OutputMarker, string.Empty);
            }
            catch (Exception exception)
            {
                Log.Error(exception, "While running");
                Environment.ExitCode = 1;
            }
            finally
            {
                timer.Stop();

                Log.Information($"Processed schema bundle in {timer.Elapsed}.");
            }
        }

        private static void WriteFile(Options options, string filename, string text)
        {
            var outputPath = Path.Combine(options.OutputDir, filename);
            var folder = Path.GetDirectoryName(outputPath);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            // Normalize line endings so editors don't complain.
            text = "// Generated by SpatialOS C# CodeGen.\n" + text.Trim() + "\n";
            text = text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);

            File.WriteAllText(outputPath, text, Encoding.UTF8);
        }

        private static string GenerateEnum(EnumDefinition enumDef, Bundle bundle)
        {
            var values = new StringBuilder();
            foreach (var v in enumDef.Values)
            {
                values.AppendLine($"{AllCapsSnakeCaseToPascalCase(v.Name)} = {v.Value},");
            }

            return $@"// Generated from {bundle.TypeToFile[enumDef.QualifiedName].CanonicalPath}({enumDef.SourceReference.Line},{enumDef.SourceReference.Column})
public enum {enumDef.Name}
{{
{Indent(1, values.ToString().TrimEnd())}
}}";
        }

        private static string GenerateType(TypeDescription type, string content, Bundle bundle)
        {
            var typeName = GetPascalCaseNameFromTypeName(type.QualifiedName);


            return $@"// Generated from {bundle.TypeToFile[type.QualifiedName].CanonicalPath}({type.SourceReference.Line},{type.SourceReference.Column})
public readonly struct {typeName} : global::System.IEquatable<{typeName}>
{{
{Indent(1, content)}
}}";
        }
    }
}
