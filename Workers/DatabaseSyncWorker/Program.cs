using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Improbable.DatabaseSync;
using Improbable.Stdlib;
using Improbable.Postgres;
using Improbable.Stdlib.Platform;
using Improbable.Worker.CInterop;
using Improbable.Worker.CInterop.Alpha;
using Npgsql.Logging;
using Serilog;
using OpList = Improbable.Stdlib.OpList;

namespace DatabaseSyncWorker
{
    internal class Program
    {
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

            ReceptionistOptions options = null;

            Parser.Default.ParseArguments<ReceptionistOptions>(args)
                .WithParsed(opts => options = opts);

            if (options == null)
            {
                Console.Error.WriteLine(string.Join(" ", args));
                return 1;
            }

            if (options.UnknownPositionalArguments.Any())
            {
                Console.Error.WriteLine(string.Join(" ", args));
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

        private static async Task RunAsync(ReceptionistOptions options)
        {
            options.WorkerName = string.IsNullOrEmpty(options.WorkerName) ? $"{options.WorkerType}-{Guid.NewGuid()}" : options.WorkerName;
            if (string.IsNullOrEmpty(options.LogFileName))
            {
                options.LogFileName = Path.Combine(Environment.CurrentDirectory, Path.ChangeExtension(options.WorkerName, ".log"));
            }

            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File(options.LogFileName)
                .CreateLogger();

            var connectionParameters = new ConnectionParameters
            {
                EnableProtocolLoggingAtStartup = true,
                ProtocolLogging = new ProtocolLoggingParameters
                {
                    LogPrefix = Path.ChangeExtension(options.LogFileName, string.Empty).TrimEnd('.') + "-protocol"
                },
                WorkerType = options.WorkerType,
                DefaultComponentVtable = new ComponentVtable(),
                Network = new NetworkParameters
                {
                    ConnectionType = NetworkConnectionType.ModularUdp,
                    ConnectionTimeoutMillis = 5000,
                    ModularUdp = new ModularUdpNetworkParameters
                    {
                        SecurityType = NetworkSecurityType.Insecure
                    }
                }
            };

            Log.Debug($"Opened logfile {options.LogFileName}");

            if (connectionParameters.EnableProtocolLoggingAtStartup)
            {
                Log.Debug($"Opened protocol logfile prefix {connectionParameters.ProtocolLogging.LogPrefix}");
            }

            using var proxy = new UdpProxy();
            using var cts = new CancellationTokenSource();

            try
            {
                proxy.Start(((IReceptionistOptions) options).SpatialOsHost, 8018, 8018, cts.Token, Log.Debug);

                using var connection = await WorkerConnection.ConnectAsync(options, connectionParameters);

                var postgresOptions = new PostgresOptions(GetPostgresFlags(options, connection));

                var tableName = connection.GetWorkerFlag("postgres_tablename") ?? "postgres";
                var databaseLogic = new DatabaseSyncLogic(postgresOptions, tableName, connection);

                using var databaseChanges = new DatabaseChanges<DatabaseSyncItem.DatabaseChangeNotification>(postgresOptions, tableName);
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

                Log.Information("Disconnected from SpatialOS");
            }
            finally
            {
                cts.Cancel();
            }
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
                if (!string.IsNullOrEmpty(envFlag))
                {
                    return envFlag;
                }

                return PostgresOptions.GetFromIOptions(options, key, value);
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
