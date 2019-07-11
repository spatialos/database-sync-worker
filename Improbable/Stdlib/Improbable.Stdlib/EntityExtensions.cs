using Improbable.Worker.CInterop;

namespace Improbable.Stdlib
{
    public static class EntityExtensions
    {
        public static Entity DeepCopy(this Entity entity)
        {
            var copy = new Entity();

            foreach (var id in entity.GetComponentIds())
            {
                copy.Add(entity.Get(id).Value.Acquire());
            }

            return copy;
        }

        public static void Free(this Entity entity)
        {
            foreach (var id in entity.GetComponentIds())
            {
                entity.Get(id).Value.Release();
            }
        }
    }
}
