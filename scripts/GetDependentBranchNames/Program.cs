using System;
using System.Collections.Generic;
using System.Linq;
using Medallion.Shell;

namespace BuildNugetPackages
{

    internal class Program
    {
        private static readonly Shell shell = new Shell(options => options.ThrowOnError());

        private struct Dependency
        {
            public string Label { get; set; }
            public string Repo { get; set; }
            public string Trigger { get; set; }
        }

        private static readonly List<Dependency> Dependencies = new List<Dependency>
        {
            new Dependency
            {
                Label = "build-database-sync-worker-example",
                Trigger = "database-sync-worker-example-premerge",
                Repo = "git@github.com:improbable/database_sync_worker_example.git"
            }
        };

        private static int Main(string[] args)
        {
            try
            {
                var currentBranch = Environment.GetEnvironmentVariable("BUILDKITE_BRANCH");

                if (string.IsNullOrEmpty(currentBranch))
                {
                    throw new Exception("The BUILDKITE_BRANCH environment variable is empty or not set.");
                }

                foreach (var dep in Dependencies)
                {
                    var lines = new List<string>();
                    shell.Run("git", "ls-remote", "--heads", dep.Repo, currentBranch)
                        .RedirectTo(lines)
                        .RedirectStandardErrorTo(Console.Error)
                        .Wait();

                    var remoteBranch = lines.Any() && lines.First().Contains(currentBranch) ? currentBranch : "master";

                    var pipeline = $@"steps:
  - label: ""{dep.Label}""
    trigger: ""{dep.Trigger}""
    build:
      branch: ""{remoteBranch}""
      env:
        DBSYNC_WORKER_BRANCH: ""{currentBranch}""
        CSHARP_TEMPLATE_BRANCH: ""$CSHARP_TEMPLATE_BRANCH""";

                    Console.Out.WriteLine(pipeline);

                    // Output to stderr to aid in debugging.
                    Console.Error.WriteLine(pipeline);
                }
            }

            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                return 1;
            }
            return 0;
        }
    }
}
