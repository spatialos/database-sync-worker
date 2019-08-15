using System.Collections.Generic;
using CommandLine;
using Improbable.Stdlib;

namespace DatabaseSyncWorker
{
    [Verb("locator", HelpText = "Connect to a deployment using the locator")]
    internal class LocatorOptions : IOptions, ILocatorOptions
    {
        public string PostgresConnection { get; set; }
        public string WorkerName { get; set; }
        public string LogFileName { get; set; }
        public IEnumerable<string> UnknownPositionalArguments { get; set; }
        public bool PostgresFromWorkerFlags { get; set; }
        public string SpatialOsHost { get; set; }
        public ushort SpatialOsPort { get; set; }
        public bool UseInsecureConnection { get; set; }
        public string DevToken { get; set; }
        public string DisplayName { get; set; }
        public string PlayerId { get; set; }
        public string PostgresHost { get; set; }
        public string PostgresUserName { get; set; }
        public string PostgresPassword { get; set; }
        public string PostgresDatabase { get; set; }
        public string PostgresAdditionalOptions { get; set; }
    }
}
