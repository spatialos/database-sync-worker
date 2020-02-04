using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Medallion.Shell;

namespace BootstrapEnv
{
    [Command("get-dependent-branch-names")]
    internal class GetDependentBranchNamesCommand
    {
        private readonly List<Dependency> dependencies = new List<Dependency>
        {
            new Dependency
            {
                Label = "build-database-sync-worker-example",
                Trigger = "database-sync-worker-example-premerge",
                Repo = "git@github.com:improbable/database_sync_worker_example.git"
            }
        };

        public async Task<int> OnExecuteAsync(CommandLineApplication app, CancellationToken token)
        {
            var shell = new Shell(options => options
                .ThrowOnError()
                .DisposeOnExit()
                .CancellationToken(token));

            var currentBranch = Environment.GetEnvironmentVariable("BUILDKITE_BRANCH");

            if (string.IsNullOrEmpty(currentBranch))
            {
                throw new Exception("The BUILDKITE_BRANCH environment variable is empty or not set.");
            }

            foreach (var dep in dependencies)
            {
                var lines = new List<string>();
                await shell.Run("git", "ls-remote", "--heads", dep.Repo, currentBranch)
                    .RedirectTo(lines)
                    .RedirectStandardErrorTo(Console.Error)
                    .Task.ConfigureAwait(false);

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

            return 0;
        }

        private struct Dependency
        {
            public string Label { get; set; }
            public string Repo { get; set; }
            public string Trigger { get; set; }
        }
    }
}
