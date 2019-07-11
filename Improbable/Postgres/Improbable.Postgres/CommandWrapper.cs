using System;
using System.Diagnostics;
using Npgsql;

namespace Improbable.Postgres
{
    public class CommandWrapper : IDisposable
    {
        private readonly Stopwatch stopwatch = new Stopwatch();

        public CommandWrapper(NpgsqlCommand command)
        {
            Command = command;
            stopwatch.Start();
        }

        public NpgsqlCommand Command { get; }

        public void Dispose()
        {
            stopwatch.Stop();
            Metrics.Inc(Command.CommandText);
            Command.Dispose();
        }
    }
}
