using CommandLine;
using Improbable.Stdlib;
using Improbable.Postgres;

namespace DatabaseSyncWorker
{
    internal interface IOptions : IWorkerOptions, IPostgresOptions
    {
        [Option("postgres-from-worker-flags", Default = true, HelpText = "If set, the worker will prefer to use Postgres worker flags over environment variables or command line options.")]
        bool PostgresFromWorkerFlags { get; set; }
    }
}
