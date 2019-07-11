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

        [Option("postgres-additional-options", Default = "")]
        string PostgresAdditionalOptions { get; set; }
    }
}
