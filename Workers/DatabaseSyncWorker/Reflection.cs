using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Improbable;
using Improbable.DatabaseSync;
using Improbable.Worker.CInterop;
using Serilog;

namespace DatabaseSyncWorker
{
    public static class Reflection
    {
        public readonly struct HydrationType
        {
            public readonly Hydration.HydrateDelegate Hydrate;

            public readonly Func<object, string> ProfileIdGetter;

            public readonly Func<SchemaObject, string> ProfileIdFromSchemaData;

            public HydrationType(Hydration.HydrateDelegate hydrate, Func<object, string> profileId, Func<SchemaObject, string> profileIdFromSchemaData)
            {
                Hydrate = hydrate;
                ProfileIdGetter = profileId;
                ProfileIdFromSchemaData = profileIdFromSchemaData;
            }
        }

        internal static IReadOnlyDictionary<uint, HydrationType> FindHydrateMethods()
        {
            var methods = new List<MethodInfo>();
            // Look for an assembly that contains generated code for a well-known type.
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a => a.DefinedTypes.Contains(typeof(EntityAcl))))
            {
                try
                {
                    foreach (var type in assembly.ExportedTypes)
                    {
                        methods.AddRange(type.GetMethods(BindingFlags.Static | BindingFlags.Public).Where(IsHydrateMethod));
                    }
                }
                catch (Exception e)
                {
                    Log.Error(e, "Discovering Hydrate methods");
                }
            }

            var components = new Dictionary<uint, HydrationType>();

            foreach (var method in methods)
            {
                try
                {
                    Log.Information("Discovered hydration type {ComponentName}", method.DeclaringType?.FullName);
                    var attribute = method.GetCustomAttributes(typeof(HydrateAttribute)).Cast<HydrateAttribute>().First();

                    if (method.DeclaringType == null)
                    {
                        continue;
                    }

                    var property = method.DeclaringType.GetProperties(BindingFlags.Public | BindingFlags.Instance).First(IsProfileId);

                    if (property.GetMethod == null)
                    {
                        Log.Warning("{ComponentName}.{MethodName}.Get is null", method.DeclaringType?.FullName, property.Name);
                        continue;
                    }

                    var fromSchemaData = method.DeclaringType.GetMethods(BindingFlags.Public | BindingFlags.Static).First(IsGetProfileIdFromSchemaData);

                    var hydrateDelegate = (Hydration.HydrateDelegate) method.CreateDelegate(typeof(Hydration.HydrateDelegate));


                    var type = new HydrationType(hydrateDelegate, instance =>
                    {
                        var result = property.GetMethod.Invoke(instance, null);
                        return result == null ? "" : (string) result;
                    }, fields =>
                    {
                        var args = new object[] { fields };
                        var result = fromSchemaData.Invoke(null, args);
                        return result == null ? "" : (string) result;
                    });

                    components.Add(attribute.ComponentId, type);
                }
                catch (Exception e)
                {
                    Log.Error(e, "While discovering hydration for {ComponentName}", method.DeclaringType?.FullName);
                }
            }

            return components;
        }

        private static bool IsHydrateMethod(MethodInfo method)
        {
            return method.GetCustomAttributes(typeof(HydrateAttribute)).Any();
        }

        private static bool IsProfileId(PropertyInfo property)
        {
            return property.GetCustomAttributes(typeof(ProfileIdAttribute)).Any();
        }

        private static bool IsGetProfileIdFromSchemaData(MethodInfo method)
        {
            return method.GetCustomAttributes(typeof(ProfileIdFromSchemaDataAttribute)).Any();
        }
    }
}
