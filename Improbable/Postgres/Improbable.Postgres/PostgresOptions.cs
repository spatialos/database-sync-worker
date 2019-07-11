using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Improbable.Stdlib;

namespace Improbable.Postgres
{
    public class PostgresOptions : IPostgresOptions
    {
        public const string HostFlagName = "postgres_host";
        public const string UserNameFlagName = "postgres_user";
        public const string PasswordFlagName = "postgres_password";
        public const string DatabaseFlagName = "postgres_database";
        public const string AdditionalFlagName = "postgres_additional";

        private string cachedConnectionString;

        private readonly Dictionary<string, string> flagValues = new Dictionary<string, string>
        {
            {HostFlagName, "127.0.0.1"},
            {UserNameFlagName, "postgres"},
            {PasswordFlagName, "DO_NOT_USE_IN_PRODUCTION"},
            {DatabaseFlagName, "postgres"},
            {AdditionalFlagName, string.Empty}
        };

        private readonly List<string> keys;

        public delegate string GetStringDelegate(string flagName, string currentFlagValue);

        public PostgresOptions(GetStringDelegate getter)
        {
            keys = flagValues.Keys.ToList();

            foreach (var kv in flagValues.ToList())
            {
                var value = getter(kv.Key, kv.Value);

                if (!string.IsNullOrEmpty(value))
                {
                    flagValues[kv.Key] = value;
                }
            }
        }

        public PostgresOptions(IPostgresOptions options)
        : this((key, value) => GetFromIOptions(options, key, value))
        {
        }

        public static string GetFromIOptions(IPostgresOptions options, string key, string value)
        {
            string optionValue;
            switch (key)
            {
                case HostFlagName:
                    optionValue = options.PostgresHost;
                    break;
                case UserNameFlagName:
                    optionValue = options.PostgresUserName;
                    break;
                case PasswordFlagName:
                    optionValue = options.PostgresPassword;
                    break;
                case DatabaseFlagName:
                    optionValue = options.PostgresDatabase;
                    break;
                case AdditionalFlagName:
                    optionValue = options.PostgresAdditionalOptions;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown Postgres flag {key}");
            }

            if (string.IsNullOrEmpty(optionValue))
            {
                return value;
            }

            return optionValue;
        }

        public string PostgresHost
        {
            get => flagValues[HostFlagName];
            set
            {
                flagValues[HostFlagName] = value;
                cachedConnectionString = null;
            }
        }

        public string PostgresUserName
        {
            get => flagValues[UserNameFlagName];
            set
            {
                flagValues[UserNameFlagName] = value;
                cachedConnectionString = null;
            }
        }

        public string PostgresPassword
        {
            get => flagValues[PasswordFlagName];
            set
            {
                flagValues[PasswordFlagName] = value;
                cachedConnectionString = null;
            }
        }

        public string PostgresDatabase
        {
            get => flagValues[DatabaseFlagName];
            set
            {
                flagValues[DatabaseFlagName] = value;
                cachedConnectionString = null;
            }
        }

        public string PostgresAdditionalOptions
        {
            get => flagValues[AdditionalFlagName];
            set
            {
                flagValues[AdditionalFlagName] = value;
                cachedConnectionString = null;
            }
        }

        public string ConnectionString => cachedConnectionString ?? (cachedConnectionString = AsPostgresConnectionString());

        public void ProcessOpList(OpList opList)
        {
            foreach (var key in keys)
            {
                string value = null;
                if (opList.TryGetWorkerFlagChange(key, ref value))
                {
                    cachedConnectionString = null;
                    flagValues[key] = value;
                }
            }
        }
        
        private string AsPostgresConnectionString()
        {
            var additional = string.Empty;
            if (!string.IsNullOrEmpty(PostgresDatabase))
            {
                additional = $";Database={PostgresDatabase}";
            }

            if (!string.IsNullOrEmpty(PostgresAdditionalOptions))
            {
                additional += $";{PostgresAdditionalOptions}";
            }

            return $"Host={PostgresHost};Username={PostgresUserName};Password={PostgresPassword}{additional}";
        }
    }
}
