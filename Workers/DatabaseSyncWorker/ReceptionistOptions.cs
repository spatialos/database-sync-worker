using System.Collections.Generic;
using CommandLine;
using Improbable.Stdlib;

namespace DatabaseSyncWorker
{
    [Verb("receptionist", HelpText = "Connect to a deployment using the receptionist")]
    internal class ReceptionistOptions : IOptions, IReceptionistOptions
    {
        public string? WorkerName { get; set; }
        public string? LogFileName { get; set; }

        public IEnumerable<string> UnknownPositionalArguments { get; set; } = null!;
        public string SpatialOsHost { get; set; } = null!;
        public string PostgresHost { get; set; } = null!;
        public string PostgresUserName { get; set; } = null!;
        public string PostgresPassword { get; set; } = null!;
        public string PostgresDatabase { get; set; } = null!;
        public string PostgresAdditionalOptions { get; set; } = null!;

        public bool PostgresFromWorkerFlags { get; set; }
        public ushort SpatialOsPort { get; set; }
    }
}
