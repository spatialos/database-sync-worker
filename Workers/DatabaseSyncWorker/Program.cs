using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Improbable.DatabaseSync;
using Improbable.Stdlib;
using Improbable.Postgres;
using Improbable.Worker.CInterop;
using Improbable.Worker.CInterop.Query;
using Npgsql.Logging;
using Serilog;
using OpList = Improbable.Stdlib.OpList;

namespace DatabaseSyncWorker
{
    internal class Program
    {
        private const string WorkerType = "DatabaseSyncWorker";

        private static async Task<int> Main(string[] args)
        {
            ThreadPool.GetMaxThreads(out var maxWorkerThreads, out var maxCompletionPortThreads);
            ThreadPool.SetMinThreads(maxWorkerThreads, maxCompletionPortThreads);

            AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
            {
                Log.Fatal(eventArgs.ExceptionObject as Exception, "Unhandled exception");

                if (eventArgs.IsTerminating)
                {
                    Log.CloseAndFlush();
                }
            };

            NpgsqlLogManager.Provider = new SerilogNpgqslLoggingProvider(NpgsqlLogLevel.Info);

            IOptions? options = null;

            Parser.Default.ParseArguments<ReceptionistOptions>(args)
                .WithParsed(opts => options = opts);

            if (options == null)
            {
                return 1;
            }

            if (options.UnknownPositionalArguments.Any())
            {
                Console.Error.WriteLine($@"Unknown positional arguments: [{string.Join(", ", options.UnknownPositionalArguments)}]");
                return 1;
            }

            try
            {
                await RunAsync(options).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to run");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }

            return 0;
        }

        private static async Task RunAsync(IOptions options)
        {
            if (string.IsNullOrEmpty(options.LogFileName))
            {
                options.LogFileName = Path.Combine(Environment.CurrentDirectory, options.WorkerName + ".log");
            }

            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File(options.LogFileName)
                .CreateLogger();

            Log.Debug($"Opened logfile {options.LogFileName}");

            var connectionParameters = new ConnectionParameters
            {
                EnableProtocolLoggingAtStartup = true,
                ProtocolLogging = new ProtocolLoggingParameters
                {
                    LogPrefix = Path.ChangeExtension(options.LogFileName, string.Empty) + "-protocol"
                },
                WorkerType = WorkerType,
                DefaultComponentVtable = new ComponentVtable()
            };


            using (var connection = await WorkerConnection.ConnectAsync(options, connectionParameters).ConfigureAwait(false))
            {
                var postgresOptions = new PostgresOptions(GetPostgresFlags(options, connection));
                DatabaseSyncLogic databaseLogic;

                var tableName = connection.GetWorkerFlag("postgres_tablename") ?? "postgres";

                using (var response = await connection.SendEntityQueryRequest(new EntityQuery { Constraint = new ComponentConstraint(DatabaseSyncService.ComponentId), ResultType = new SnapshotResultType() }).ConfigureAwait(false))
                {
                    if (response.ResultCount == 0)
                    {
                        throw new ServiceNotFoundException(nameof(DatabaseSyncService));
                    }

                    databaseLogic = new DatabaseSyncLogic(postgresOptions, tableName, connection, response.Results.First().Key, DatabaseSyncService.CreateFromSnapshot(response.Results.First().Value));
                };

                connection.StartSendingMetrics(databaseLogic.UpdateMetrics);

                foreach (var opList in connection.GetOpLists())
                {
                    ProcessOpList(opList);

                    if (options.PostgresFromWorkerFlags)
                    {
                        postgresOptions.ProcessOpList(opList);
                    }

                    databaseLogic.ProcessOpList(opList);
                }

            }

            Log.Information("Disconnected from SpatialOS");
        }

        private static PostgresOptions.GetStringDelegate GetPostgresFlags(IOptions options, WorkerConnection connection)
        {
            return (key, value) =>
            {
                if (options.PostgresFromWorkerFlags)
                {
                    var flagValue = connection.GetWorkerFlag(key);

                    if (!string.IsNullOrEmpty(flagValue))
                    {
                        return flagValue;
                    }
                }

                var envFlag = Environment.GetEnvironmentVariable(key.ToUpperInvariant());
                return !string.IsNullOrEmpty(envFlag) ? envFlag : PostgresOptions.GetFromIOptions(options, key, value);
            };
        }

        private static void ProcessOpList(OpList opList)
        {
            foreach (var disconnectOp in opList.OfOpType<DisconnectOp>())
            {
                Log.Information(disconnectOp.Reason);
            }
        }
    }

    internal class ServiceNotFoundException : Exception
    {
        public ServiceNotFoundException(string typeName) : base($"{typeName} not found. Is your snapshot correct?")
        {
        }
    }
}
