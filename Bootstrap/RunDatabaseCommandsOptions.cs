using System.Collections.Generic;
using CommandLine;
using Improbable.Postgres;

namespace Bootstrap
{
    [Verb("run-database-commands")]
    public class RunDatabaseCommandsOptions : IPostgresOptions
    {
        public string PostgresHost { get; set; } = null!;
        public string PostgresUserName { get; set; } = null!;
        public string PostgresPassword { get; set; } = null!;
        public string PostgresDatabase { get; set; } = null!;
        public string PostgresAdditionalOptions { get; set; } = null!;

        [Option('c', "command", HelpText = "SQL to run")]
        public IEnumerable<string> Commands { get; set; } = null!;

        // HACK: the command line parser treats the literal "" as <no value> (e.g. --postgres-database "") and throws an error.
        [Option("no-database", Default = false, HelpText = "Set to disable connecting to a specific database")]
        public bool NoDatabase { get; set; }
    }
}
