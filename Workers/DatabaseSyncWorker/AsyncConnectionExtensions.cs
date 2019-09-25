using Improbable.DatabaseSync;
using Improbable.Stdlib;

namespace DatabaseSyncWorker
{
    public static class AsyncConnectionExtensions
    {
        public static void SendCommandFailure(this WorkerConnection connection, long requestId, CommandErrors error)
        {
            connection.SendCommandFailure(requestId, error.ToString("D"));
        }
    }
}
