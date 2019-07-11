using System;
using CommandLine;
using Improbable.Postgres;
using Improbable.DatabaseSync;
using Npgsql.Logging;
using Serilog;

namespace Bootstrap
{
    internal class Program
    {
        private static int Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .MinimumLevel.Debug()
                .CreateLogger();

            NpgsqlLogManager.Provider = new SerilogNpgqslLoggingProvider(NpgsqlLogLevel.Warn);

            try
            {
                return Parser.Default
                    .ParseArguments<
                        RunDatabaseCommandsOptions>(args)
                    .MapResult<
                        RunDatabaseCommandsOptions,
                        int>(
                        RunDatabaseCommands,
                        errors => 1);
            }
            catch (Exception e)
            {
                Log.Error(e, "Unhandled exception");
                return 1;
            }
        }

        private static int RunDatabaseCommands(RunDatabaseCommandsOptions options)
        {
            try
            {
                var postgresOptions = CreatePostgresOptions(options);
                if (options.NoDatabase)
                {
                    postgresOptions.PostgresDatabase = "";
                }

                var sql = string.Join(";", options.Commands);
                Log.Information("Running {Sql}", sql);

                using (var connection = new ConnectionWrapper(postgresOptions.ConnectionString))
                using(var cmd = connection.Command(sql))
                {
                    cmd.Command.ExecuteNonQuery();
                }

                return 0;
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to run database commands");
                return 1;
            }

        }
    }
}
