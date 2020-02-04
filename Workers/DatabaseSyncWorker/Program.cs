using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Improbable.DatabaseSync;
using Improbable.Stdlib;
using Improbable.Postgres;
using Improbable.Worker.CInterop;
using Improbable.Worker.CInterop.Query;
using McMaster.Extensions.CommandLineUtils;
using Npgsql.Logging;
using Serilog;
using OpList = Improbable.Stdlib.OpList;

namespace DatabaseSyncWorker
{
    [Command]
    internal class Program : IReceptionistOptions, IPostgresOptions
    {
        private const string WorkerType = "DatabaseSyncWorker";

        [Option("--worker-name", Description = "The name of the worker connecting to SpatialOS.")]
        public string? WorkerName { get; set; }

        [Option("--logfile", Description = "The full path to a logfile.")]
        public string? LogFileName { get; set; }

        [Option("--spatialos-host", Description = "The host to use to connect to SpatialOS.")]
        public string SpatialOsHost { get; set; } = "localhost";

        [Option("--spatialos-port", Description = "The port to use to connect to SpatialOS.")]
        public ushort SpatialOsPort { get; set; } = 7777;

        [Option("--postgres-host" )]
        public string PostgresHost { get; set; } = "127.0.0.1";

        [Option("--postgres-username")]
        public string PostgresUserName { get; set; } = "postgres";

        [Option("--postgres-password")]
        public string PostgresPassword { get; set; } = "DO_NOT_USE_IN_PRODUCTION";

        [Option("--postgres-database")]
        public string PostgresDatabase { get; set; } = "postgres";

        [Option("--postgres-additional-options", Description = "Add additional PostgreSQL connection parameters. See https://www.npgsql.org/doc/connection-string-parameters.html?q=connection for more information.")]
        public string PostgresAdditionalOptions { get; set; } = string.Empty;

        [Option("--postgres-from-worker-flags", Description = "If set, the worker will prefer to use Postgres worker flags over environment variables or command line options.")]
        private bool PostgresFromWorkerFlags { get; set; } = true;

        private static async Task<int> Main(string[] args)
        {
            ThreadPool.GetMaxThreads(out var maxWorkerThreads, out var maxCompletionPortThreads);
            ThreadPool.SetMinThreads(maxWorkerThreads, maxCompletionPortThreads);

            NpgsqlLogManager.Provider = new SerilogNpgqslLoggingProvider(NpgsqlLogLevel.Info);

            try
            {
                return await CommandLineApplication.ExecuteAsync<Program>(args);
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        private async Task<int> OnExecute(CommandLineApplication app, CancellationToken token)
        {
            if (string.IsNullOrEmpty(LogFileName))
            {
                LogFileName = Path.Combine(Environment.CurrentDirectory, WorkerName ?? $"{WorkerType}.log");
            }

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File(LogFileName)
                .CreateLogger();

            Log.Debug($"Opened logfile {LogFileName}");
            
            var connectionParameters = new ConnectionParameters
            {
                EnableProtocolLoggingAtStartup = true,
                ProtocolLogging = new ProtocolLoggingParameters
                {
                    LogPrefix = Path.ChangeExtension(LogFileName, string.Empty) + "-protocol"
                },
                WorkerType = WorkerType,
                DefaultComponentVtable = new ComponentVtable()
            };

            Log.Debug("Connecting to SpatialOS...");

            using var connection = await WorkerConnection.ConnectAsync(this, connectionParameters, token).ConfigureAwait(false);
            var postgresOptions = new PostgresOptions(GetPostgresFlags(connection));
            DatabaseSyncLogic databaseLogic;

            var tableName = connection.GetWorkerFlag("postgres_tablename") ?? "postgres";

            using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(token);

            EntityId serviceEntityId;
            var entityQuery = new EntityQuery { Constraint = new ComponentConstraint(DatabaseSyncService.ComponentId), ResultType = new SnapshotResultType() };
            using (var response = await connection.SendEntityQueryRequest(entityQuery, timeoutMillis: null, token).ConfigureAwait(false))
            {
                if (response.ResultCount == 0)
                {
                    throw new ServiceNotFoundException(nameof(DatabaseSyncService));
                }

                serviceEntityId = response.Results.First().Key;
                databaseLogic = new DatabaseSyncLogic(postgresOptions, tableName, connection, serviceEntityId, DatabaseSyncService.CreateFromSnapshot(response.Results.First().Value), connectionCts.Token);
            };

            Log.Debug("Found {ServiceType} {EntityId}", nameof(DatabaseSyncService), serviceEntityId);

            connection.StartSendingMetrics(databaseLogic.UpdateMetrics);

            Log.Debug("Entering main loop");
            foreach (var opList in connection.GetOpLists(token))
            {
                ProcessOpList(opList);

                if (PostgresFromWorkerFlags)
                {
                    postgresOptions.ProcessOpList(opList);
                }

                databaseLogic.ProcessOpList(opList);
            }

            connectionCts.Cancel();

            Log.Information("Disconnected from SpatialOS");
            return 0;
        }

        private PostgresOptions.GetStringDelegate GetPostgresFlags(WorkerConnection connection)
        {
            return (key, value) =>
            {
                if (PostgresFromWorkerFlags)
                {
                    var flagValue = connection.GetWorkerFlag(key);

                    if (!string.IsNullOrEmpty(flagValue))
                    {
                        return flagValue;
                    }
                }

                var envFlag = Environment.GetEnvironmentVariable(key.ToUpperInvariant());
                return !string.IsNullOrEmpty(envFlag) ? envFlag : PostgresOptions.GetFromIOptions(this, key, value);
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
