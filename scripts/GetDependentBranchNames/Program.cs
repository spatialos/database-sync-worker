using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BuildNugetPackages
{
    internal class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                // Default to "master", unless the downstream dependency has a branch matching the same name, then use that.
                var currentBranch = Environment.GetEnvironmentVariable("BUILDKITE_BRANCH");
                var checkRemoteBranch = RunRedirected("git", "ls-remote", "--heads", "git@github.com:improbable/database_sync_worker_example.git", currentBranch).Trim();

                var remoteBranch = checkRemoteBranch.Contains(currentBranch) ? currentBranch : "master";

                Console.Out.WriteLine($@"steps:
  - label: ""build-database-sync-worker-example""
    trigger: database-sync-worker-example-premerge
    build:
      branch: {remoteBranch}
      env:
        DBSYNC_WORKER_BRANCH: ""{currentBranch}""
        CSHARP_TEMPLATE_BRANCH: ""$CSHARP_TEMPLATE_BRANCH""");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                return 1;
            }
            return 0;
        }

        private static string RunRedirected(string command, params string[] args)
        {
            var info = new ProcessStartInfo(command, string.Join(' ', args))
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using (var process = Process.Start(info))
            {
                if (process == null) throw new Exception($"Failed to start {command}");

                var output = new StringBuilder();
                process.OutputDataReceived += (sender, eventArgs) =>
                {
                    if (!string.IsNullOrEmpty(eventArgs.Data))
                    {
                        output.AppendLine(eventArgs.Data);
                    }
                };

                process.ErrorDataReceived += (sender, eventArgs) =>
                {
                    if (!string.IsNullOrEmpty(eventArgs.Data))
                    {
                        Console.Error.WriteLine(eventArgs.Data);
                    }
                };

                process.EnableRaisingEvents = true;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new Exception($"Non-zero exit code: {command} {string.Join(" ", args)}");
                }

                return output.ToString();
            }
        }
    }
}
