using Improbable.Worker.CInterop;

namespace Improbable.Stdlib
{
    public static class WorkerFlags
    {
        public static bool TryGetWorkerFlagChange(this OpList opList, string flagName, ref string newValue)
        {
            var found = false;
            foreach (var op in opList.OfOpType<FlagUpdateOp>())
            {
                if (op.Name == flagName)
                {
                    newValue = op.Value;
                    found = true;
                }
            }

            return found;
        }
    }
}
