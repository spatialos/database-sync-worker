using McMaster.Extensions.CommandLineUtils;
using Serilog;

namespace BootstrapEnv
{
    [Command]
    [Subcommand(typeof(BuildNugetPackagesCommand), typeof(GetDependentBranchNamesCommand))]
    internal class Program
    {
        private static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .MinimumLevel.Debug()
                .CreateLogger();

            try
            {
                CommandLineApplication.Execute<Program>(args);
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
