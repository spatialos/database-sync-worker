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
                        RunDatabaseCommandsOptions,
                        CreateDatabaseSyncTableOptions>(args)
                    .MapResult<
                        RunDatabaseCommandsOptions,
                        CreateDatabaseSyncTableOptions,
                        int>(
                        RunDatabaseCommands,
                        CreateDatabaseSyncTable,
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

                Log.Information("Done.");

                return 0;
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to run database commands");
                return 1;
            }
        }

        private static int CreateDatabaseSyncTable(CreateDatabaseSyncTableOptions options)
        {
            try
            {
                var commands = DatabaseSyncItem.InitializeDatabase(options.TableName);

                using (var connection = new ConnectionWrapper(CreatePostgresOptions(options).ConnectionString))
                using (var command = connection.Command(commands))
                {
                    Log.Information("Initializing {TableName}...", options.TableName);
                    command.Command.ExecuteNonQuery();
                    Log.Information("Done.");
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to create {TableName}", options.TableName);
                return 1;
            }

            return 0;
        }

        private static PostgresOptions CreatePostgresOptions(IPostgresOptions options)
        {
            return new PostgresOptions((key, value) =>
            {
                var envFlag = Environment.GetEnvironmentVariable(key.ToUpperInvariant());
                if (!string.IsNullOrEmpty(envFlag))
                {
                    return envFlag;
                }

                return PostgresOptions.GetFromIOptions(options, key, value);
            });
        }
    }
}
