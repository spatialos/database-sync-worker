using System;
using System.Collections.Generic;
using Improbable.Worker.CInterop;

namespace Improbable.Stdlib
{
    public static class OpListExtensions
    {
        private static readonly Dictionary<Type, OpType> TypeMaps = new Dictionary<Type, OpType>
        {
            {typeof(DisconnectOp), OpType.Disconnect},
            {typeof(FlagUpdateOp), OpType.FlagUpdate},
            {typeof(LogMessageOp), OpType.LogMessage},
            {typeof(MetricsOp), OpType.Metrics},
            {typeof(CriticalSectionOp), OpType.CriticalSection},
            {typeof(AddEntityOp), OpType.AddEntity},
            {typeof(RemoveEntityOp), OpType.RemoveEntity},
            {typeof(ReserveEntityIdsResponseOp), OpType.ReserveEntityIdsResponse},
            {typeof(CreateEntityResponseOp), OpType.CreateEntityResponse},
            {typeof(DeleteEntityResponseOp), OpType.DeleteEntityResponse},
            {typeof(EntityQueryResponseOp), OpType.EntityQueryResponse},
            {typeof(AddComponentOp), OpType.AddComponent},
            {typeof(RemoveComponentOp), OpType.RemoveComponent},
            {typeof(AuthorityChangeOp), OpType.AuthorityChange},
            {typeof(ComponentUpdateOp), OpType.ComponentUpdate},
            {typeof(CommandRequestOp), OpType.CommandRequest},
            {typeof(CommandResponseOp), OpType.CommandResponse}
        };

        public static IEnumerable<T> OfOpType<T>(this OpList ops) where T : struct
        {
            if (!TypeMaps.TryGetValue(typeof(T), out var opType))
            {
                throw new ArgumentException($"{typeof(T).Name} is not a SpatialOS op type.");
            }

            foreach (var op in ops)
            {
                if (op.OpType != opType)
                {
                    continue;
                }

                switch (opType)
                {
                    case OpType.Disconnect:
                        yield return ReinterpretCast<DisconnectOp, T>(op.DisconnectOp);
                        break;
                    case OpType.FlagUpdate:
                        yield return ReinterpretCast<FlagUpdateOp, T>(op.FlagUpdateOp);
                        break;
                    case OpType.LogMessage:
                        yield return ReinterpretCast<LogMessageOp, T>(op.LogMessageOp);
                        break;
                    case OpType.Metrics:
                        yield return ReinterpretCast<MetricsOp, T>(op.MetricsOp);
                        break;
                    case OpType.CriticalSection:
                        yield return ReinterpretCast<CriticalSectionOp, T>(op.CriticalSectionOp);
                        break;
                    case OpType.AddEntity:
                        yield return ReinterpretCast<AddEntityOp, T>(op.AddEntityOp);
                        break;
                    case OpType.RemoveEntity:
                        yield return ReinterpretCast<RemoveEntityOp, T>(op.RemoveEntityOp);
                        break;
                    case OpType.ReserveEntityIdsResponse:
                        yield return ReinterpretCast<ReserveEntityIdsResponseOp, T>(op.ReserveEntityIdsResponseOp);
                        break;
                    case OpType.CreateEntityResponse:
                        yield return ReinterpretCast<CreateEntityResponseOp, T>(op.CreateEntityResponseOp);
                        break;
                    case OpType.DeleteEntityResponse:
                        yield return ReinterpretCast<DeleteEntityResponseOp, T>(op.DeleteEntityResponseOp);
                        break;
                    case OpType.EntityQueryResponse:
                        yield return ReinterpretCast<EntityQueryResponseOp, T>(op.EntityQueryResponseOp);
                        break;
                    case OpType.AddComponent:
                        yield return ReinterpretCast<AddComponentOp, T>(op.AddComponentOp);
                        break;
                    case OpType.RemoveComponent:
                        yield return ReinterpretCast<RemoveComponentOp, T>(op.RemoveComponentOp);
                        break;
                    case OpType.AuthorityChange:
                        yield return ReinterpretCast<AuthorityChangeOp, T>(op.AuthorityChangeOp);
                        break;
                    case OpType.ComponentUpdate:
                        yield return ReinterpretCast<ComponentUpdateOp, T>(op.ComponentUpdateOp);
                        break;
                    case OpType.CommandRequest:
                        yield return ReinterpretCast<CommandRequestOp, T>(op.CommandRequestOp);
                        break;
                    case OpType.CommandResponse:
                        yield return ReinterpretCast<CommandResponseOp, T>(op.CommandResponseOp);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public static IEnumerable<AddComponentOp> OfComponent(this IEnumerable<AddComponentOp> ops, uint componentId)
        {
            foreach (var op in ops)
            {
                if (op.Data.ComponentId == componentId)
                {
                    yield return op;
                }
            }
        }

        public static IEnumerable<RemoveComponentOp> OfComponent(this IEnumerable<RemoveComponentOp> ops, uint componentId)
        {
            foreach (var op in ops)
            {
                if (op.ComponentId == componentId)
                {
                    yield return op;
                }
            }
        }

        public static IEnumerable<ComponentUpdateOp> OfComponent(this IEnumerable<ComponentUpdateOp> ops, uint componentId)
        {
            foreach (var op in ops)
            {
                if (op.Update.ComponentId == componentId)
                {
                    yield return op;
                }
            }
        }

        public static IEnumerable<AuthorityChangeOp> OfComponent(this IEnumerable<AuthorityChangeOp> ops, uint componentId)
        {
            foreach (var op in ops)
            {
                if (op.ComponentId == componentId)
                {
                    yield return op;
                }
            }
        }

        public static IEnumerable<CommandRequestOp> OfComponent(this IEnumerable<CommandRequestOp> ops, uint componentId)
        {
            foreach (var op in ops)
            {
                if (op.Request.ComponentId == componentId)
                {
                    yield return op;
                }
            }
        }

        public static IEnumerable<CommandResponseOp> OfComponent(this IEnumerable<CommandResponseOp> ops, uint componentId)
        {
            foreach (var op in ops)
            {
                if (op.Response.ComponentId == componentId)
                {
                    yield return op;
                }
            }
        }

        private static unsafe TDest ReinterpretCast<TSource, TDest>(TSource source)
        {
            var sourceRef = __makeref(source);
            var dest = default(TDest);
            var destRef = __makeref(dest);
            *(IntPtr*) &destRef = *(IntPtr*) &sourceRef;
            return __refvalue(destRef, TDest);
        }
    }
}
