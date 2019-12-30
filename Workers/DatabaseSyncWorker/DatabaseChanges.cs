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
        private readonly CancellationTokenSource cts;
        private readonly Task task;

        public DatabaseChanges(PostgresOptions postgresOptions, string tableName)
        {
            cts = new CancellationTokenSource();

            task = Task.Factory.StartNew(async unusedStateObject =>
            {
                NpgsqlConnection? connection = null;

                while (!cts.IsCancellationRequested)
                {
                    var connectionString = postgresOptions.ConnectionString;
                    try
                    {
                        cts.Token.ThrowIfCancellationRequested();

                        connection = new NpgsqlConnection(connectionString);
                        connection.Open();

                        Log.Information("Listening to {TableName}", tableName);

                        connection.Notification += (sender, args) =>
                        {
                            try
                            {
                                var changeNotification = JsonConvert.DeserializeObject<TType>(args.Payload);
                                changes = changes.Add(changeNotification);

                                Metrics.Inc(Metrics.TotalChangesReceived);
                            }
                            catch (Exception e)
                            {
                                Log.Error(e, "While parsing JSON for change notification");
                            }
                        };

                        // Receive notifications from the database when rows change.
                        using (var cmd = new NpgsqlCommand($"LISTEN {tableName}", connection))
                        {
                            cmd.ExecuteNonQuery();
                        }

                        while (connection.State == ConnectionState.Open)
                        {
                            await connection.WaitAsync(cts.Token);
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        // Don't log, avoid adding confusion to logs on a graceful shutdown.
                        break;
                    }
                    catch (Exception e)
                    {
                        if (!cts.Token.IsCancellationRequested)
                        {
                            Log.Error(e, "LISTEN {TableName}", tableName);
                        }
                    }
                    finally
                    {
                        connection?.Dispose();
                    }

                    await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
                }

                Log.Information("Stopped listening to {TableName}", tableName);
            }, TaskCreationOptions.LongRunning);
        }

        public ImmutableArray<TType> GetChanges()
        {
            var toReturn = changes;
            changes = ImmutableArray<TType>.Empty;

            return toReturn;
        }

        public void Dispose()
        {
            cts?.Cancel();
            task?.Wait();

            cts?.Dispose();
        }
    }
}
