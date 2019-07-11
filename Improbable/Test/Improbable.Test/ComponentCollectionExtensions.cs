using System;
using System.Threading;
using System.Threading.Tasks;
using Improbable.Stdlib;

namespace Improbable.Test
{
    public static class ComponentCollectionExtensions
    {
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

        public static Task EntityExists<TComponent>(this ComponentCollection<TComponent> components, EntityId entityId, TimeSpan? timeout = null) where TComponent : struct
        {
            return Task.Run(async () =>
            {
                using (var tcs = new CancellationTokenSource(timeout ?? DefaultTimeout))
                {
                    while (!tcs.IsCancellationRequested)
                    {
                        if (components.Contains(entityId))
                        {
                            break;
                        }

                        await Task.Delay(TimeSpan.FromMilliseconds(1), tcs.Token);
                    }
                }
            });
        }

        public static Task ComponentSatisfies<TComponent>(this ComponentCollection<TComponent> components, EntityId entityId, Func<TComponent, bool> predicate, TimeSpan? timeout = null) where TComponent : struct
        {
            return Task.Run(async () =>
            {
                using (var tcs = new CancellationTokenSource(timeout ?? DefaultTimeout))
                {
                    while (!tcs.IsCancellationRequested)
                    {
                        if (components.Contains(entityId) && predicate(components.Get(entityId)))
                        {
                            break;
                        }

                        await Task.Delay(TimeSpan.FromMilliseconds(1), tcs.Token);
                    }
                }
            });
        }
    }
}
