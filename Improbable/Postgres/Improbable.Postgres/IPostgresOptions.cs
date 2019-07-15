using CommandLine;

namespace Improbable.Postgres
{
    public interface IPostgresOptions
    {
        [Option("postgres-host", Default = "127.0.0.1")]
        string PostgresHost { get; set; }

        [Option("postgres-username", Default = "postgres")]
        string PostgresUserName { get; set; }

        [Option("postgres-password", Default = "DO_NOT_USE_IN_PRODUCTION")]
        string PostgresPassword { get; set; }

        [Option("postgres-database", Default = "postgres")]
        string PostgresDatabase { get; set; }

        [Option("postgres-additional-options", Default = "", HelpText="Add additional PostgreSQL connection parameters. See https://www.npgsql.org/doc/connection-string-parameters.html?q=connection for more information.")]
        string PostgresAdditionalOptions { get; set; }
    }
}
