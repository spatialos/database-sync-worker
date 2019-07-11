using System.Collections.Generic;
using Improbable.Worker.CInterop;

namespace Improbable.Stdlib
{
    public class ComponentCollection<TComponent> : IOpProcessor where TComponent : struct
    {
        public delegate TComponent CreateDelegate(SchemaComponentData? data);

        public delegate TComponent UpdateDelegate(TComponent original, SchemaComponentUpdate? update);

        private readonly HashSet<EntityId> authority = new HashSet<EntityId>();
        private readonly uint componentId;
        private readonly List<TComponent> components = new List<TComponent>();
        private readonly CreateDelegate create;
        private readonly Queue<int> freeSlots = new Queue<int>();
        private readonly Dictionary<EntityId, int> lookup = new Dictionary<EntityId, int>();
        private readonly UpdateDelegate update;

        public ComponentCollection(uint componentId, CreateDelegate create, UpdateDelegate update)
        {
            this.componentId = componentId;
            this.create = create;
            this.update = update;
        }

        public IReadOnlyCollection<EntityId> EntityIds => lookup.Keys;

        public void ProcessOpList(OpList opList)
        {
            foreach (var op in opList.Ops)
            {
                switch (op.OpType)
                {
                    case OpType.AddComponent:
                        var addOp = op.AddComponentOp;
                        if (addOp.Data.ComponentId == componentId)
                        {
                            Add(new EntityId(addOp.EntityId), addOp.Data.SchemaData);
                        }

                        break;
                    case OpType.RemoveComponent:
                        var removeOp = op.RemoveComponentOp;
                        if (removeOp.ComponentId == componentId)
                        {
                            Remove(new EntityId(removeOp.EntityId));
                        }

                        break;
                    case OpType.AuthorityChange:
                        var authorityChangeOp = op.AuthorityChangeOp;
                        if (authorityChangeOp.ComponentId == componentId)
                        {
                            SetAuthority(new EntityId(authorityChangeOp.EntityId), authorityChangeOp.Authority);
                        }

                        break;
                    case OpType.ComponentUpdate:
                        var updateOp = op.ComponentUpdateOp;
                        if (updateOp.Update.ComponentId == componentId)
                        {
                            Update(new EntityId(updateOp.EntityId), updateOp.Update.SchemaData);
                        }

                        break;
                }
            }
        }

        public void Add(EntityId entityId, SchemaComponentData? data)
        {
            var component = create(data);
            if (Contains(entityId))
            {
                // Handle dynamic component adds.
                components[lookup[entityId]] = component;
                return;
            }

            int index;

            if (freeSlots.Count == 0)
            {
                index = components.Count;
                components.Add(component);
            }
            else
            {
                index = freeSlots.Dequeue();
                components[index] = component;
            }

            lookup[entityId] = index;
        }

        private void Update(EntityId entityId, SchemaComponentUpdate? componentUpdate)
        {
            if (lookup.TryGetValue(entityId, out var index))
            {
                components[index] = update(components[index], componentUpdate);
            }
        }

        public TComponent Get(EntityId entityId)
        {
            return components[lookup[entityId]];
        }

        public bool TryGet(EntityId entityId, out TComponent component)
        {
            if (lookup.TryGetValue(entityId, out var index))
            {
                component = components[index];
                return true;
            }

            component = default;
            return false;
        }

        public bool Contains(EntityId entityId)
        {
            return lookup.ContainsKey(entityId);
        }

        public bool Remove(EntityId entityId)
        {
            if (lookup.TryGetValue(entityId, out var index))
            {
                freeSlots.Enqueue(index);
                lookup.Remove(entityId);
                return true;
            }

            return false;
        }

        private void SetAuthority(EntityId entityId, Authority newAuthority)
        {
            if (newAuthority == Authority.Authoritative)
            {
                authority.Add(entityId);
            }
            else
            {
                authority.Remove(entityId);
            }
        }

        public bool HasAuthority(EntityId entityId)
        {
            return authority.Contains(entityId);
        }
    }
}
