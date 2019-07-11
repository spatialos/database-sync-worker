using System;
using System.Diagnostics;
using Npgsql;

namespace Improbable.Postgres
{
    public class ConnectionWrapper : IDisposable
    {
        private readonly Stopwatch timer = new Stopwatch();

        public ConnectionWrapper(string connectionString)
        {
            Metrics.Inc(Metrics.ConcurrentConnections);

            Connection = new NpgsqlConnection(connectionString);
            Connection.Open();
            timer.Start();
        }

        public NpgsqlConnection Connection { get; }

        public void Dispose()
        {
            Connection?.Dispose();
            timer.Stop();
            Metrics.Dec(Metrics.ConcurrentConnections);

            Metrics.Observe(Metrics.ConnectionDuration, timer.ElapsedMilliseconds);
        }

        public CommandWrapper Command(string cmd = "")
        {
            return new CommandWrapper(new NpgsqlCommand(cmd, Connection));
        }
    }
}
