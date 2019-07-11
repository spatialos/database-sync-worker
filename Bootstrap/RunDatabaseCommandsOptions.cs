using System.Collections.Generic;
using CommandLine;
using Improbable.Postgres;

namespace Bootstrap
{
    [Verb("run-database-commands")]
    public class RunDatabaseCommandsOptions : IPostgresOptions
    {
        public string PostgresHost { get; set; }
        public string PostgresUserName { get; set; }
        public string PostgresPassword { get; set; }
        public string PostgresDatabase { get; set; }
        public string PostgresAdditionalOptions { get; set; }

        // HACK: the command line parser treats the literal "" as <no value> and throws an error.
        [Option("no-database", Default = false, HelpText = "Set to disable connecting to a specific database")]
        public bool NoDatabase { get; set; }

        [Option('c', "command", HelpText = "SQL to run")]
        public IEnumerable<string> Commands { get; set; }
    }
}
