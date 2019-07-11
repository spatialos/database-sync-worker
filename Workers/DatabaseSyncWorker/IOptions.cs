using CommandLine;
using Improbable.Stdlib;
using Improbable.Postgres;

namespace DatabaseSyncWorker
{
    internal interface IOptions : IWorkerOptions, IPostgresOptions
    {
        [Option("postgres-from-worker-flags")]
        bool PostgresFromWorkerFlags { get; set; }
    }
}
