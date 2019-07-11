using System;
using System.Collections.Generic;
using System.Linq;

namespace Improbable.CSharpCodeGen
{
    public static class Case
    {
        private static readonly string[] Underscore = {"_"};
        private static readonly string[] Period = {"."};

        public static string CapitalizeFirstLetter(string text)
        {
            return char.ToUpperInvariant(text[0]) + text.Substring(1, text.Length - 1);
        }

        public static string ToPascalCase(IEnumerable<string> parts)
        {
            var result = string.Empty;
            foreach (var s in parts)
            {
                result += CapitalizeFirstLetter(s);
            }

            return result;
        }

        public static string SnakeCaseToPascalCase(string text)
        {
            return ToPascalCase(text.Split(Underscore, StringSplitOptions.RemoveEmptyEntries));
        }

        public static string SnakeCaseToCamelCase(string text)
        {
            var parts = text.Split(Underscore, StringSplitOptions.RemoveEmptyEntries);
            return parts[0] + ToPascalCase(parts.Skip(1));
        }

        public static string CapitalizeNamespace(string text)
        {
            var strings = text.Split(Period, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(".", strings.Select(SnakeCaseToPascalCase));
        }

        public static string GetPascalCaseNamespaceFromTypeName(string text)
        {
            var strings = text.Split(Period, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(".", strings
                .Take(strings.Length - 1)
                .Select(SnakeCaseToPascalCase));
        }

        public static string Indent(int level, string inputString)
        {
            var indent = string.Empty.PadLeft(level, '\t');
            return indent + inputString.Replace("\n", $"\n{indent}");
        }

        public static string AllCapsSnakeCaseToPascalCase(string screamingSnake)
        {
            return screamingSnake.Split(new[] {"_"}, StringSplitOptions.RemoveEmptyEntries)
                .Select(part =>
                {
                    if (part.Length == 1)
                    {
                        return part;
                    }

                    return part[0] + part.Substring(1, part.Length - 1).ToLowerInvariant();
                })
                .Aggregate(string.Empty, (s1, s2) => s1 + s2);
        }

        public static string GetPascalCaseNameFromTypeName(string text)
        {
            return SnakeCaseToPascalCase(text).Split('.').Last();
        }
    }
}
