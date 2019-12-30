using CommandLine;
using Improbable.Postgres;

namespace Bootstrap
{
    [Verb("create-database-table")]
    internal class CreateDatabaseSyncTableOptions : IPostgresOptions
    {
        [Option("table-name", HelpText = "The name of the table to create.", Required = true)]
        public string TableName { get; set; } = null!;

        public string PostgresHost { get; set; } = null!;
        public string PostgresUserName { get; set; } = null!;
        public string PostgresPassword { get; set; } = null!;
        public string PostgresDatabase { get; set; } = null!;
        public string PostgresAdditionalOptions { get; set; } = null!;
    }
}
