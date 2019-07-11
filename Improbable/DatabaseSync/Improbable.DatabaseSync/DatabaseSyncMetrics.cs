using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Improbable.DatabaseSync
{
    public static class DatabaseSyncMetrics
    {

        public const string DatabaseSyncLogic = "DatabaseSyncLogic.";
        public const string GetItemFailure = DatabaseSyncLogic + nameof(GetItemFailure);
        public const string GetChildrenForItemFailure = DatabaseSyncLogic + nameof(GetChildrenForItemFailure);
        public const string GetDatabaseSyncForProfileFailure = DatabaseSyncLogic + nameof(GetDatabaseSyncForProfileFailure);
        public const string IncrementFailure = DatabaseSyncLogic + nameof(IncrementFailure);
        public const string DecrementFailure = DatabaseSyncLogic + nameof(DecrementFailure);
        public const string SetParentFailure = DatabaseSyncLogic + nameof(SetParentFailure);
        public const string AssociatePathWithClientFailure = DatabaseSyncLogic + nameof(AssociatePathWithClientFailure);
        public const string HydrateComponentFailure = DatabaseSyncLogic + nameof(HydrateComponentFailure);
    }
}
