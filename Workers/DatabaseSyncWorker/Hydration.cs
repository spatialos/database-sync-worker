using System.Collections.Generic;
using Improbable.DatabaseSync;
using Improbable.Worker.CInterop;

namespace DatabaseSyncWorker
{
    public class Hydration
    {
        public delegate SchemaComponentUpdate HydrateDelegate(IEnumerable<DatabaseSyncItem> items, string profilePath);
    }
}
