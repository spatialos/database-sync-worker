using System;
using System.Collections;
using System.Collections.Generic;
using Improbable.Worker.CInterop;

namespace Improbable.Stdlib
{
    public class OpList : IDisposable, IEnumerable<OpList.Op>
    {
        public readonly List<Op> Ops;

        private readonly Worker.CInterop.OpList rawOps;

        public OpList()
        {
            Ops = new List<Op>();
        }

        public OpList(Worker.CInterop.OpList rawOps)
        {
            this.rawOps = rawOps;
            var count = rawOps.GetOpCount();

            Ops = new List<Op>(count);

            for (var i = 0; i < count; i++)
            {
                switch (rawOps.GetOpType(i))
                {
                    case OpType.Disconnect:
                        Ops.Add(new Op {OpType = OpType.Disconnect, DisconnectOp = rawOps.GetDisconnectOp(i)});
                        break;
                    case OpType.FlagUpdate:
                        Ops.Add(new Op {OpType = OpType.FlagUpdate, FlagUpdateOp = rawOps.GetFlagUpdateOp(i)});
                        break;
                    case OpType.LogMessage:
                        Ops.Add(new Op {OpType = OpType.LogMessage, LogMessageOp = rawOps.GetLogMessageOp(i)});
                        break;
                    case OpType.Metrics:
                        Ops.Add(new Op {OpType = OpType.Metrics, MetricsOp = rawOps.GetMetricsOp(i)});
                        break;
                    case OpType.CriticalSection:
                        Ops.Add(new Op {OpType = OpType.CriticalSection, CriticalSectionOp = rawOps.GetCriticalSectionOp(i)});
                        break;
                    case OpType.AddEntity:
                        Ops.Add(new Op {OpType = OpType.AddEntity, AddEntityOp = rawOps.GetAddEntityOp(i)});
                        break;
                    case OpType.RemoveEntity:
                        Ops.Add(new Op {OpType = OpType.RemoveEntity, RemoveEntityOp = rawOps.GetRemoveEntityOp(i)});
                        break;
                    case OpType.ReserveEntityIdResponse:
                        // Deprecated - should we even bother?
                        break;
                    case OpType.ReserveEntityIdsResponse:
                        Ops.Add(new Op {OpType = OpType.ReserveEntityIdsResponse, ReserveEntityIdsResponseOp = rawOps.GetReserveEntityIdsResponseOp(i)});
                        break;
                    case OpType.CreateEntityResponse:
                        Ops.Add(new Op {OpType = OpType.CreateEntityResponse, CreateEntityResponseOp = rawOps.GetCreateEntityResponseOp(i)});
                        break;
                    case OpType.DeleteEntityResponse:
                        Ops.Add(new Op {OpType = OpType.DeleteEntityResponse, DeleteEntityResponseOp = rawOps.GetDeleteEntityResponseOp(i)});
                        break;
                    case OpType.EntityQueryResponse:
                        Ops.Add(new Op {OpType = OpType.EntityQueryResponse, EntityQueryResponseOp = rawOps.GetEntityQueryResponseOp(i)});
                        break;
                    case OpType.AddComponent:
                        Ops.Add(new Op {OpType = OpType.AddComponent, AddComponentOp = rawOps.GetAddComponentOp(i)});
                        break;
                    case OpType.RemoveComponent:
                        Ops.Add(new Op {OpType = OpType.RemoveComponent, RemoveComponentOp = rawOps.GetRemoveComponentOp(i)});
                        break;
                    case OpType.AuthorityChange:
                        Ops.Add(new Op {OpType = OpType.AuthorityChange, AuthorityChangeOp = rawOps.GetAuthorityChangeOp(i)});
                        break;
                    case OpType.ComponentUpdate:
                        Ops.Add(new Op {OpType = OpType.ComponentUpdate, ComponentUpdateOp = rawOps.GetComponentUpdateOp(i)});
                        break;
                    case OpType.CommandRequest:
                        Ops.Add(new Op {OpType = OpType.CommandRequest, CommandRequestOp = rawOps.GetCommandRequestOp(i)});
                        break;
                    case OpType.CommandResponse:
                        Ops.Add(new Op {OpType = OpType.CommandResponse, CommandResponseOp = rawOps.GetCommandResponseOp(i)});
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public void Dispose()
        {
            rawOps.Dispose();
        }

        public IEnumerator<Op> GetEnumerator()
        {
            return Ops.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public struct Op
        {
            public OpType OpType;

            public DisconnectOp DisconnectOp;
            public FlagUpdateOp FlagUpdateOp;
            public LogMessageOp LogMessageOp;
            public MetricsOp MetricsOp;
            public CriticalSectionOp CriticalSectionOp;
            public AddEntityOp AddEntityOp;
            public RemoveEntityOp RemoveEntityOp;
            public ReserveEntityIdsResponseOp ReserveEntityIdsResponseOp;
            public CreateEntityResponseOp CreateEntityResponseOp;
            public DeleteEntityResponseOp DeleteEntityResponseOp;
            public EntityQueryResponseOp EntityQueryResponseOp;
            public AddComponentOp AddComponentOp;
            public RemoveComponentOp RemoveComponentOp;
            public AuthorityChangeOp AuthorityChangeOp;
            public ComponentUpdateOp ComponentUpdateOp;
            public CommandRequestOp CommandRequestOp;
            public CommandResponseOp CommandResponseOp;
        }
    }
}
