using System.Collections.Generic;
using System.Collections.Immutable;
using Improbable.Worker.CInterop;

namespace Improbable.Stdlib
{
    public class WhenAllComponents : IOpProcessor
    {
        private readonly HashSet<uint> components;
        private readonly Dictionary<EntityId, uint> counts = new Dictionary<EntityId, uint>();

        public WhenAllComponents(params uint[] componentIds)
        {
            components = new HashSet<uint>(componentIds);
        }

        public ImmutableHashSet<EntityId> Activated { get; private set; } = ImmutableHashSet<EntityId>.Empty;

        public ImmutableHashSet<EntityId> Deactivated { get; private set; } = ImmutableHashSet<EntityId>.Empty;

        public void ProcessOpList(OpList list)
        {
            Activated = ImmutableHashSet<EntityId>.Empty;
            Deactivated = ImmutableHashSet<EntityId>.Empty;

            foreach (var op in list.OfOpType<AddComponentOp>())
            {
                Added(op);
            }

            foreach (var op in list.OfOpType<RemoveComponentOp>())
            {
                Removed(op);
            }
        }

        private void Removed(RemoveComponentOp op)
        {
            if (!components.Contains(op.ComponentId) || !counts.ContainsKey(op.EntityId))
            {
                return;
            }

            var newValue = counts[op.EntityId] = counts[op.EntityId] - 1;
            if (newValue == components.Count - 1)
            {
                Deactivated = Deactivated.Add(op.EntityId);
            }
        }

        private void Added(AddComponentOp op)
        {
            if (!components.Contains(op.Data.ComponentId))
            {
                return;
            }

            if (!counts.TryGetValue(op.EntityId, out var count))
            {
                counts.Add(op.EntityId, 0);
            }

            var newValue = counts[op.EntityId] = counts[op.EntityId] + 1;
            if (newValue == components.Count)
            {
                Activated = Activated.Add(op.EntityId);
            }
        }
    }
}
