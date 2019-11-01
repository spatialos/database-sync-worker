using System.Collections.Generic;
using CommandLine;
using Improbable.Stdlib;

namespace DatabaseSyncWorker
{
    [Verb("receptionist", HelpText = "Connect to a deployment using the receptionist")]
    internal class ReceptionistOptions : IOptions, IReceptionistOptions
    {
        public string WorkerName { get; set; }
        public string LogFileName { get; set; }
        public IEnumerable<string> UnknownPositionalArguments { get; set; }
        public string SpatialOsHost { get; set; }
        public ushort SpatialOsPort { get; set; }
        public string PostgresHost { get; set; }
        public string PostgresUserName { get; set; }
        public string PostgresPassword { get; set; }
        public string PostgresDatabase { get; set; }
        public string PostgresAdditionalOptions { get; set; }
        public bool PostgresFromWorkerFlags { get; set; }
        public string WorkerType { get; set; }
    }
}
