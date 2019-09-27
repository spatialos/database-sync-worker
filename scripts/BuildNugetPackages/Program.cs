using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommandLine;
using Medallion.Shell;

namespace BuildNugetPackages
{
    internal class Program
    {
        private static readonly Shell shell = new Shell(options => options.ThrowOnError());

        private const string CSharpWorkerRepo = "https://github.com/spatialos/csharp-worker-template.git";

        private static int Main(string[] args)
        {
            Console.Out.WriteLine(string.Join(" ", args));

            if (args.Length == 0)
            {
                // Default to git checkout.
                args = new[] {"git"};

                var templateBranch = Environment.GetEnvironmentVariable("CSHARP_TEMPLATE_BRANCH");

                if (string.IsNullOrEmpty(templateBranch))
                {
                    // Default to "master", unless the downstream dependency has a branch matching the same name, then use that.
                    var currentBranch = Environment.GetEnvironmentVariable("BUILDKITE_BRANCH");

                    if (string.IsNullOrEmpty(currentBranch))
                    {
                        // We're running locally, get the current branch name.
                        var currentLines = new List<string>();
                        shell.Run("git", "rev-parse", "--abbrev-ref", "HEAD")
                            .RedirectTo(currentLines)
                            .RedirectStandardErrorTo(Console.Error)
                            .Wait();
                        currentBranch = currentLines.First().Trim();
                    }

                    var lines = new List<string>();
                    shell.Run("git", "ls-remote", "--heads", CSharpWorkerRepo, currentBranch)
                        .RedirectTo(lines)
                        .RedirectStandardErrorTo(Console.Error)
                        .Wait();

                    templateBranch = lines.Any() && lines.First().Contains(currentBranch) ? currentBranch : "master";
                }

                if (!string.IsNullOrEmpty(templateBranch))
                {
                    args = new[] {"git", "--branch", templateBranch};
                }
            }

            try
            {
                Parser.Default.ParseArguments<LocalOptions, GitOptions, PrintSchemaOptions>(args)
                    .WithParsed<LocalOptions>(BuildLocal)
                    .WithParsed<GitOptions>(BuildGit)
                    .WithParsed<PrintSchemaOptions>(unused => { PrintSchemaLocations(); })
                    .WithNotParsed(errors => throw new Exception("Failed to parse command line"));
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                return 1;
            }

            return 0;
        }

        private static void PrintSchemaLocations()
        {
            var cacheDirectory = GetCacheDirectory();
            Console.Out.WriteLine("Find schema files in:");
            Console.Out.WriteLine(
                $"  {Path.Combine(cacheDirectory, "improbable.postgres.schema", "0.0.1-preview", "content", "schema")}");
            Console.Out.WriteLine(
                $"  {Path.Combine(cacheDirectory, "improbable.databasesync.schema", "0.0.2-preview", "content", "schema")}");
        }

        private static void BuildLocal(LocalOptions local)
        {
            Console.Out.WriteLine($"Building NuGet packages from {local.SourceDir}");

            BuildPackages(local.SourceDir);
        }

        private static void BuildGit(GitOptions git)
        {
            var nugetSourceDir = Path.Combine(Environment.CurrentDirectory, ".nupkg_src");
            CleanDirectory(nugetSourceDir);

            Console.Out.WriteLine($"Building NuGet packages from {git.Repository} {git.Branch}@{git.Commit}");

            shell.Run("git", "clone", git.Repository, nugetSourceDir, "-b", git.Branch, "--single-branch", "--quiet")
                .RedirectTo(Console.Out)
                .RedirectStandardErrorTo(Console.Error)
                .Wait();

            shell.Run("git", new[] { "checkout", git.Commit, "--quiet" }, options => options.WorkingDirectory(nugetSourceDir))
                .RedirectTo(Console.Out)
                .RedirectStandardErrorTo(Console.Error).Wait();

            BuildPackages(nugetSourceDir);
        }

        private static void BuildPackages(string nugetSourceDir)
        {
            var cacheDirectory = GetCacheDirectory();
            CleanNugetPackages(cacheDirectory);

            var sdkInteropDir =
                Path.GetFullPath(Path.Combine(nugetSourceDir, "Improbable", "WorkerSdkInterop", "Improbable.WorkerSdkInterop"));
            var localNugetPackages = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "nupkgs"));

            CleanDirectory(localNugetPackages);

            // For simplicity, some packages depend on Improbable.WorkerSdkInterop. Make sure that's packaged first in the source directory.
            var targetPath = Path.GetFullPath(Path.Combine(nugetSourceDir, "nupkgs"));
            CleanDirectory(targetPath);

            shell.Run("dotnet", "pack", sdkInteropDir, "--verbosity:quiet", "-p:Platform=x64", "--output", targetPath)
                .RedirectTo(Console.Out)
                .RedirectStandardErrorTo(Console.Error)
                .Wait();

            // Now build everything into the worker's directory.
            shell.Run("dotnet", "pack", Path.Combine(nugetSourceDir, "Improbable"), "--verbosity:quiet",
                "-p:Platform=x64", "--output", localNugetPackages)
                .RedirectTo(Console.Out)
                .RedirectStandardErrorTo(Console.Error)
                .Wait();

            Console.Out.WriteLine("Built NuGet packages.");

            shell.Run("dotnet", "restore", "--verbosity:quiet", "-p:Platform=x64")
                .RedirectTo(Console.Out)
                .RedirectStandardErrorTo(Console.Error)
                .Wait();

            Console.Out.WriteLine("Restoring NuGet packages...");

            PrintSchemaLocations();
        }

        private static string GetCacheDirectory()
        {
            var outputLines = new List<string>();
            // Make any required schema accessible to users
            shell.Run("dotnet", "nuget", "locals", "global-packages", "--list")
                .RedirectTo(outputLines)
                .RedirectStandardErrorTo(Console.Error)
                .Wait();

            const string globalPackages = "global-packages:";
            var locationString = outputLines
                .FirstOrDefault(s => s.Contains(globalPackages));

            if (locationString == null)
            {
                throw new Exception($"Could not find nuget package location in: {string.Join("\n", outputLines)}");
            }

            var location = locationString
                .Substring(locationString.IndexOf(globalPackages, StringComparison.Ordinal) + globalPackages.Length)
                .Trim()
                .TrimEnd('\\', '/');

            return location;
        }

        private static void CleanNugetPackages(string cachePath)
        {
            if (Directory.Exists(cachePath))
            {
                var toClear = Directory.EnumerateDirectories(cachePath, "improbable.*",
                    new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, RecurseSubdirectories = false });

                Console.Out.WriteLine($"Clearing '{cachePath}' of Improbable packages...");

                foreach (var dir in toClear)
                {
                    Console.Out.Write($"  {dir}...");
                    Directory.Delete(dir, true);
                    Console.Out.WriteLine($" (done)");
                }
            }
        }

        private static void CleanDirectory(string nugetSourceDir)
        {
            Console.Out.Write($"Cleaning '{nugetSourceDir}'...");

            if (Directory.Exists(nugetSourceDir))
            {
                var toUnProtect = Directory
                    .EnumerateFileSystemEntries(nugetSourceDir, "*", SearchOption.AllDirectories).Where(IsReadOnly);

                foreach (var f in toUnProtect)
                {
                    File.SetAttributes(f, File.GetAttributes(f) & ~ FileAttributes.ReadOnly);
                }

                Directory.Delete(nugetSourceDir, true);
            }

            Directory.CreateDirectory(nugetSourceDir);
            Console.Out.WriteLine($" (done)");
        }

        private static bool IsReadOnly(string f)
        {
            return (File.GetAttributes(f) & FileAttributes.ReadOnly) != 0;
        }

        [Verb("print-schema")]
        private class PrintSchemaOptions
        {
        }

        [Verb("local")]
        private class LocalOptions
        {
            [Option("source-dir", Default = "../csharp-worker-template")]
            public string SourceDir { get; set; }
        }

        [Verb("git")]
        private class GitOptions
        {
            [Option("repository", Default = CSharpWorkerRepo)]
            public string Repository { get; set; }

            [Option("branch", Default = "master")] public string Branch { get; set; }

            [Option("commit", Default = "HEAD")] public string Commit { get; set; }
        }
    }
}
