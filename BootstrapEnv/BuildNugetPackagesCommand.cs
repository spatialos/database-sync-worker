using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Medallion.Shell;
using Serilog;

namespace BootstrapEnv
{
    [Command("build-nuget-packages")]
    [Subcommand(typeof(GitCommand), typeof(LocalCommand), typeof(PrintSchemaCommand))]

    internal class BuildNugetPackagesCommand
    {
        private const string CSharpWorkerRepo = "https://github.com/spatialos/csharp-worker-template.git";

        private Task<int> OnExecuteAsync(CommandLineApplication app, CancellationToken token)
        {
            // Default to git.
            var gitCommand = new GitCommand();
            return gitCommand.OnExecuteAsync(app, token);
        }

        private static async Task PrintSchemaLocations(CancellationToken token)
        {
            var cacheDirectory = await GetCacheDirectoryAsync(token);

            Log.Information("Find schema files in:");
            Log.Information(
                $"  {Path.Combine(cacheDirectory, "improbable.postgres.schema", "0.0.2-preview", "content", "schema")}");
            Log.Information(
                $"  {Path.Combine(cacheDirectory, "improbable.databasesync.schema", "0.0.3-preview", "content", "schema")}");
        }

        private static async Task<int> BuildPackagesAsync(string nugetSourceDir, CancellationToken token)
        {
            var shell = new Shell(options => options
                .ThrowOnError()
                .EnvironmentVariable("AutoPublishPackages", "1")
                .DisposeOnExit()
                .CancellationToken(token));

            var sdkInteropDir =
                Path.GetFullPath(Path.Combine(nugetSourceDir, "Improbable", "WorkerSdkInterop", "Improbable.WorkerSdkInterop"));

            // For simplicity, some packages depend on Improbable.WorkerSdkInterop as a NuGet package rather than as a project reference.
            // Make sure that's packaged first in the source directory.
            var targetPath = Path.GetFullPath(Path.Combine(nugetSourceDir, "nupkgs"));

            await shell.Run("dotnet", "pack", sdkInteropDir, "--verbosity:quiet", "-p:Platform=x64", "--output", targetPath)
                .RedirectTo(Console.Out)
                .RedirectStandardErrorTo(Console.Error)
                .Task.ConfigureAwait(false);

            // Now build everything into the worker's directory.
            await shell.Run("dotnet", "pack", Path.Combine(nugetSourceDir, "Improbable"), "--verbosity:quiet",
                "-p:Platform=x64", "--output", targetPath)
                .RedirectTo(Console.Out)
                .RedirectStandardErrorTo(Console.Error)
                .Task.ConfigureAwait(false);

            Log.Information("Built NuGet packages.");

            await shell.Run("dotnet", "restore", "--verbosity:quiet", "-p:Platform=x64")
                .RedirectTo(Console.Out)
                .RedirectStandardErrorTo(Console.Error)
                .Task.ConfigureAwait(false);

            Log.Information("Restoring NuGet packages...");

            await PrintSchemaLocations(token);

            return 0;
        }

        private static async Task<string> GetCacheDirectoryAsync(CancellationToken token)
        {
            var shell = new Shell(options => options
                .ThrowOnError()
                .DisposeOnExit()
                .CancellationToken(token));

            var outputLines = new List<string>();
            // Make any required schema accessible to users
            await shell.Run("dotnet", "nuget", "locals", "global-packages", "--list")
                .RedirectTo(outputLines)
                .RedirectStandardErrorTo(Console.Error)
                .Task.ConfigureAwait(false);

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

        private static void CleanDirectory(string dir)
        {
            Log.Information($"Cleaning '{dir}'...");

            if (Directory.Exists(dir))
            {
                var toUnProtect = Directory
                    .EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories).Where(IsReadOnly);

                foreach (var f in toUnProtect)
                {
                    File.SetAttributes(f, File.GetAttributes(f) & ~FileAttributes.ReadOnly);
                }

                Log.Information(" deleting...");
                Directory.Delete(dir, true);
            }

            Directory.CreateDirectory(dir);
            Log.Information(" (done)");
        }

        private static bool IsReadOnly(string f)
        {
            return (File.GetAttributes(f) & FileAttributes.ReadOnly) != 0;
        }

        [Command("print-schema")]
        private class PrintSchemaCommand
        {
            public async Task<int> OnExecuteAsync(CommandLineApplication app, CancellationToken token)
            {
                await PrintSchemaLocations(token).ConfigureAwait(false);
                return 0;
            }
        }

        [Command("local")]
        private class LocalCommand
        {
            [Option("--source-dir")] public string SourceDir { get; set; } = "../csharp-worker-template";

            public async Task<int> OnExecuteAsync(CommandLineApplication app, CancellationToken token)
            {
                Log.Information($"Building NuGet packages from {SourceDir}");

                return await BuildPackagesAsync(SourceDir, token).ConfigureAwait(false);
            }
        }

        [Command("git")]
        private class GitCommand
        {
            [Option("--repository")]
            public string Repository { get; set; } = CSharpWorkerRepo;

            [Option("--branch")] public string Branch { get; set; } = "master";

            [Option("--commit")] public string Commit { get; set; } = "HEAD";

            public async Task<int> OnExecuteAsync(CommandLineApplication app, CancellationToken token)
            {
                var shell = new Shell(options => options.ThrowOnError().DisposeOnExit());

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
                    Branch = templateBranch;
                }

                var nugetSourceDir = Path.Combine(Environment.CurrentDirectory, ".nupkg_src");
                CleanDirectory(nugetSourceDir);

                Log.Information($"Building NuGet packages from {Repository} {Branch}@{Commit}");

                await shell.Run("git", "clone", Repository, nugetSourceDir, "-b", Branch, "--single-branch", "--quiet")
                    .RedirectTo(Console.Out)
                    .RedirectStandardErrorTo(Console.Error)
                    .Task.ConfigureAwait(false);

                await shell.Run("git", new[] { "checkout", Commit, "--quiet" }, options => options.WorkingDirectory(nugetSourceDir))
                    .RedirectTo(Console.Out)
                    .RedirectStandardErrorTo(Console.Error)
                    .Task.ConfigureAwait(false);

                return await BuildPackagesAsync(nugetSourceDir, token).ConfigureAwait(false);
            }
        }
    }
}
