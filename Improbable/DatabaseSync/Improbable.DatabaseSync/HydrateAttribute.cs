using System;

namespace Improbable.DatabaseSync
{
    [AttributeUsage(AttributeTargets.Method)]
    public class HydrateAttribute : Attribute
    {
        public HydrateAttribute(uint componentId)
        {
            ComponentId = componentId;
        }

        public uint ComponentId { get; }
    }
}
