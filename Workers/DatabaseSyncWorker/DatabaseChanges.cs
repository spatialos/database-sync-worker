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

        public DatabaseChanges(PostgresOptions postgresOptions, string tableName)
        {
            tcs = new CancellationTokenSource();

            Task.Factory.StartNew(async unusedStateObject =>
            {
                NpgsqlConnection connection = null;

                while (tcs != null && !tcs.Token.IsCancellationRequested)
                {
                    var connectionString = postgresOptions.ConnectionString;
                    try
                    {
                        connection = new NpgsqlConnection(connectionString);
                        connection.Open();

                        Log.Information("Listening to {Database}", tableName);

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
                    catch (Exception e)
                    {
                        Log.Error(e, "LISTEN {TableName} to {Connection}", tableName, connectionString);
                    }
                    finally
                    {
                        connection?.Dispose();
                    }

                    await Task.Delay(TimeSpan.FromSeconds(5));
                }
            }, TaskCreationOptions.LongRunning, tcs.Token);
        }

        public ImmutableArray<TType> GetChanges()
        {
            var toReturn = changes;
            changes = ImmutableArray<TType>.Empty;

            return toReturn;
        }

        public void Dispose()
        {
            tcs.Cancel();
            tcs.Dispose();
            tcs = null;
        }
    }
}
