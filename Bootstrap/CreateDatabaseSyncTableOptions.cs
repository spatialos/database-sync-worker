using CommandLine;
using Improbable.Postgres;

namespace Bootstrap
{
    [Verb("create-database-table")]
    class CreateDatabaseSyncTableOptions : IPostgresOptions
    {
        [Option("table-name", HelpText = "The name of the table to create.", Required = true)]
        public string TableName { get; set; }

        public string PostgresHost { get; set; }
        public string PostgresUserName { get; set; }
        public string PostgresPassword { get; set; }
        public string PostgresDatabase { get; set; }
        public string PostgresAdditionalOptions { get; set; }
    }
}
