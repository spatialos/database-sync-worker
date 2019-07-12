using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Improbable.DatabaseSync;
using Improbable.Stdlib;
using Improbable.Postgres;
using Improbable.Worker.CInterop;
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

            IOptions options = null;

            Parser.Default.ParseArguments<ReceptionistOptions, LocatorOptions>(args)
                .WithParsed<ReceptionistOptions>(opts => options = opts)
                .WithParsed<LocatorOptions>(opts => options = opts);

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
                await RunAsync(options);
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

            using (var connection = await WorkerConnection.ConnectAsync(options, connectionParameters))
            {
                var postgresOptions = new PostgresOptions((key, value) =>
                {
                    if (options.PostgresFromWorkerFlags)
                    {
                        var flagValue = connection.GetWorkerFlag(key);

                        if (!string.IsNullOrEmpty(flagValue))
                        {
                            return string.IsNullOrEmpty(flagValue) ? value : flagValue;
                        }
                    }

                    var envFlag = Environment.GetEnvironmentVariable(key.ToUpperInvariant());
                    if(!string.IsNullOrEmpty(envFlag))
                    {
                        return envFlag;
                    }

                    return PostgresOptions.GetFromIOptions(options, key, value);
                });

                using (var databaseChanges = new DatabaseChanges<DatabaseSyncItem.DatabaseChangeNotification>(postgresOptions))
                {
                    var (entityId, entity) = connection.FindServiceEntities(DatabaseSyncService.ComponentId).First();
                    var databaseLogic = new DatabaseSyncLogic(postgresOptions, connection, entityId, DatabaseSyncService.CreateFromSnapshot(entity));

                    connection.StartSendingMetrics(databaseLogic.UpdateMetrics);

                    foreach (var opList in connection.GetOpLists(TimeSpan.FromMilliseconds(16)))
                    {
                        var changes = databaseChanges.GetChanges();

                        if (!changes.IsEmpty)
                        {
                            databaseLogic.ProcessDatabaseSyncChanges(changes);
                        }

                        ProcessOpList(opList);

                        if (options.PostgresFromWorkerFlags)
                        {
                            postgresOptions.ProcessOpList(opList);
                        }

                        connection.ProcessOpList(opList);
                        databaseLogic.ProcessOpList(opList);
                    }
                }
            }

            Log.Information("Disconnected from SpatialOS");
        }

        private static void ProcessOpList(OpList opList)
        {
            foreach (var logOp in opList.OfOpType<LogMessageOp>())
            {
                switch (logOp.Level)
                {
                    case LogLevel.Debug:
                        Log.Debug(logOp.Message);
                        break;
                    case LogLevel.Info:
                        Log.Information(logOp.Message);
                        break;
                    case LogLevel.Warn:
                        Log.Warning(logOp.Message);
                        break;
                    case LogLevel.Error:
                        Log.Error(logOp.Message);
                        break;
                    case LogLevel.Fatal:
                        Log.Fatal(logOp.Message);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                break;
            }

            foreach (var disconnectOp in opList.OfOpType<DisconnectOp>())
            {
                Log.Information(disconnectOp.Reason);
            }
        }
    }
}
