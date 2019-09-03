using System;
using System.Collections.Immutable;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Improbable.Postgres;
using Newtonsoft.Json;
using Npgsql;
using Serilog;

namespace DatabaseSyncWorker
{
    public class DatabaseChanges<TType> : IDisposable
    {
        private ImmutableArray<TType> changes = ImmutableArray<TType>.Empty;
        private CancellationTokenSource tcs;
        private readonly Thread thread;

        public DatabaseChanges(PostgresOptions postgresOptions, string tableName)
        {
            tcs = new CancellationTokenSource();

            thread = new Thread(async unusedStateObject =>
            {
                NpgsqlConnection connection = null;

                while (tcs != null && !tcs.Token.IsCancellationRequested)
                {
                    var connectionString = postgresOptions.ConnectionString;
                    try
                    {
                        connection = new NpgsqlConnection(connectionString);
                        connection.Open();

                        Log.Information("Listening to {TableName}", tableName);

                        connection.Notification += (sender, args) =>
                        {
                            var changeNotification = JsonConvert.DeserializeObject<TType>(args.AdditionalInformation);
                            changes = changes.Add(changeNotification);

                            Metrics.Inc(Metrics.TotalChangesReceived);
                        };

                        // Receive notifications from the database when rows change.
                        using (var cmd = new NpgsqlCommand($"LISTEN {tableName}", connection))
                        {
                            cmd.ExecuteNonQuery();
                        }

                        while (connection.State == ConnectionState.Open)
                        {
                            await connection.WaitAsync(tcs.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Don't log, avoid adding confusion to logs on a graceful shutdown.
                        break;
                    }
                    catch (Exception e)
                    {
                        Log.Error(e, "LISTEN {TableName}", tableName);
                    }
                    finally
                    {
                        connection?.Dispose();
                    }

                    await Task.Delay(TimeSpan.FromSeconds(5), tcs.Token);
                }

                Log.Information("Stopped listening to {TableName}", tableName);
            });

            thread.Start();
        }

        public ImmutableArray<TType> GetChanges()
        {
            var toReturn = changes;
            changes = ImmutableArray<TType>.Empty;

            return toReturn;
        }

        public void Dispose()
        {
            tcs?.Cancel();
            thread?.Join();

            tcs?.Dispose();
            tcs = null;
        }
    }
}
