# Database Sync Worker (preview)
[![Build status](https://badge.buildkite.com/eb40ef8885282f2482f0f0b4e0f2a93e1a6cf89e6541022108.svg)](https://buildkite.com/improbable/database-sync-worker-premerge) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

The Database Sync Worker is a SpatialOS server-worker designed to easily sync and persist cross-session game data (such as player inventories) between SpatialOS and an external database.

> If you intend to use this worker with the SpatialOS GDK for Unreal, we recommend following [this tutorial] instead of the below Setup guide. It takes you through integrating this worker in the [Example Project] and using it to store the “All Time Kills” and “Deaths” of the players in a Postgres database running on your local machine.

## Premise

Sometimes, your game has data that:

1. Needs to live longer than the lifetime of a deployment
2. Needs to be used by SpatialOS workers and clients
3. Needs to be accessible to other services and platforms, outside of SpatialOS

A simple example of this might be players' permanent data, which could contain persistent character unlocks, stats and customisations.

1. The profile may need to move between deployments, for example, in session-based games
2. Clients and workers both need to view and modify the profile based on what the player does in-game
3. Online stores, stats services and customer services need access to the profile information

The Database Sync Worker is built on the premise that given this data will be read and modified by SpatialOS workers, it should be presented in the same way as all other SpatialOS data - using [SpatialOS schema](https://docs.improbable.io/reference/latest/shared/schema/introduction/ ) components and commands. This lets you keep your game's logic using already established data models.

**The Database Sync Worker (DBSync for short) provides the ability to easily map a hierarchy of data back and forth between a database and a SpatialOS deployment's workers and clients.**

DBSync is based on the [SpatialOS C# Worker Template](https://github.com/improbable/dotnet_core_worker/).

## Prerequisites

* Moderate to experienced familiarity with SpatialOS concepts, project structure and configuration. **This project is intended for developers who want to extend their existing SpatialOS project with new capabilities.**
* Comfortable with cmd/Powershell, or bash.

In order to use DBSync in your project, you'll need to do the following:

1. Setup PostgreSQL locally, and in the cloud.
2. Add the DBSync and its schema to your project.
3. Configure SpatialOS to start a single DBSync.
4. Send DBSync commands from your workers to read and write to the hierarchy data.
5. Receive updates in your workers and clients that reflect the state of the hierarchy data in the database.

## Project layout

* `Bootstrap/` - A utility that sets up database tables that DBSync can use.
* `CSharpCodeGenerator` - Generates C# code from your project's schema.
* `GeneratedCode` - The output of `CSharpCodeGenerator`.
* `Improbable` - The source for supporting libraries meant to be used by your worker. Once APIs have finalized, these will also be available on [nuget.org]
* `schema` - A placeholder, empty schema, so the project builds out of the box.
* `scripts` - Scripts that automate common and repetitive tasks.
* `Workers/DatabaseSyncWorker` - The source for DBSync.

## Setup

### Locally

1. Follow the guide for the [SpatialOS C# Worker Template](https://github.com/improbable/dotnet_core_worker/)
2. Install Postgres 11 from [postgresql.org/download/windows](https://postgresql.org/download/windows)
   1. Set the default password to `DO_NOT_USE_IN_PRODUCTION`

### In the cloud

We don't currently provide hosting for PostgreSQL in the cloud, but you can use [Google Cloud SQL for PostgreSQL](https://cloud.google.com/sql/docs/postgres/quickstart) to get up and running.
When you have your cloud PostgreSQL setup, see [Configure DBSync](#Configure-DBSync).

## Add DBSync schema to your project

The interface to the DBSync worker is defined by [schema], which you need to include in your project's schema.

The required files are:
[`Improbable/Postgres/Improbable.Postgres.Schema/schema/improbable/postgres/postgres.schema`]
[`Improbable/DatabaseSync/Improbable.DatabaseSync.Schema/schema/improbable/database_sync/database_sync.schema`]

You can include these files in your project in a few ways.

---

### Option 1: Copying

This is best used when your project is using [Structured Project Layout].
You can simply copy the two files into your project's `schema` directory, keeping the directory structure intact.

For example

- `project/`
  - `schema/`
  - ...`your_schema_files`...
  - `improbable/`
    - `postgres/`
      - `postgres.schema`
    - `database_sync/`
      - `database_sync.schema`

### Option 2: Adding a `schema_path`

This is best used when your project is using [Flexible Project Layout].
If your project invokes the [schema compiler] directly, you can point it to the new `schema` folders

```
schema_compiler
    ...arguments...
    --schema_path="<root_path>/Improbable/Postgres/Improbable.Postgres.Schema/schema"
    --schema_path="<root_path>/Improbable/DatabaseSync/Improbable.DatabaseSync.Schema/schema"
```

> Note that `--schema_path`s should be absolute, rather than relative paths.

### Option 3: Nuget package references

If your project is entirely .NET Core C# based, then you can simply add Nuget references to your `GeneratedCode` project:
```
dotnet add GeneratedCode/GeneratedCode.csproj package "Improbable.Postgres.Schema" --version 0.0.2-preview
dotnet add GeneratedCode/GeneratedCode.csproj package "Improbable.DatabaseSync.Schema" --version 0.0.2-preview
```

> By default, the `GeneratedCode` project already includes references to both of these projects.

---

If you choose options 1 or 2, then you need to **remove** the Nuget package references from the `GeneratedCode` project:

```
dotnet remove GeneratedCode/GeneratedCode.csproj package "Improbable.Postgres.Schema"
dotnet remove GeneratedCode/GeneratedCode.csproj package "Improbable.DatabaseSync.Schema"
```

If you don't do this, you'll see error messages similar to this:
```
error: 'type ProfileIdAnnotation' (improbable.database_sync.ProfileIdAnnotation) conflicts with 'type ProfileIdAnnotation' defined at improbable/database_sync/database_sync.schema:10
```

## Configure DBSync

### Connecting to Postgres

When running locally, DBSync will connect to your local PostgreSQL using defaults, which are in the flags that are prefixed with `postgres-`

You can see these defaults by running `dotnet run -p Workers/DatabaseSyncWorker help receptionist`

When running in the cloud, DBSync will need some configuration to connect to your hosted PostgreSQL instance.
This is accomplished by modifying [worker flags].

```
{
    "workerType": "DatabaseSyncWorker",
    "flags": [
        {
        "name": "postgres_host",
        "value": "_your_instance_hostname"
        },
        {
        "name": "postgres_user",
        "value": "postgres"
        },
        {
        "name": "postgres_password",
        "value": "DO_NOT_USE_IN_PRODUCTION"
        },
        {
        "name": "postgres_database",
        "value": "items"
        },
        {
        "name": "postgres_additional",
        "value": ""
        }
    ]
}
```
You can also change these flags at runtime using [worker-flag set].
DBSync will detect the changes and connect using the new details.

### Additional worker flags

The worker is also configured at launch time via command line arguments in its [launch configuration].

For a full list of commands and their arguments, you can run the following.
`dotnet run -p Workers/DatabaseSyncWorker help`

The default recommended options are:

```
"command": "DatabaseSyncWorker",
    "arguments": [
        "--spatialos-host", "${IMPROBABLE_RECEPTIONIST_HOST}",
        "--spatialos-port", "${IMPROBABLE_RECEPTIONIST_PORT}",
        "--worker-name", "${IMPROBABLE_WORKER_ID}",
        "--logfile", "${IMPROBABLE_LOG_FILE}",
    ]
```

### Configuring SpatialOS

The DBSync worker is designed to run as a singleton, that is, only one instance per deployment.
Here is a suggested [deployment configuration]:

```
{
    "layer": "database_sync",
    "hex_grid": {
        "num_workers": 1
    },
    "options": {
        "manual_worker_connection_only": false
    }
}
```

### Configuring the database

DBSync stores its data in PostgreSQL.
The data that is stored is derived from the `DatabaseSyncItem` type in [database_sync.schema]. The `CodeGenerator` uses the Nuget package `Improbable.Postgres.CSharpCodeGen` to generate both C# helpers and SQL to safely map data back and forth between SpatialOS and the database.
This code is used while the worker is running in SpatialOS, and at project setup time to setup the database to the right state.

* The default database is `"items"`.
* The default table in this database is also `"items"`. This contains all of the hierarchical data.
* The default metrics table in this database is `"metrics"`. This contains various metrics the worker collects about timings, command counts, and failures.


**Local, Windows**

`scripts/reset-database.ps1`

**Local, macOS/Linux**

`scripts/reset-database.sh`

**Remote, Windows**

`scripts/reset-database.ps1 --postgres-host "_your_instance_hostname" --postgres-username "_your_instance_username_" --postgres-password "DO_NOT_USE_IN_PRODUCTION"`

**Remote, macOS/Linux**

`scripts/reset-database.sh --postgres-host "_your_instance_hostname" --postgres-username "_your_instance_username_" --postgres-password "DO_NOT_USE_IN_PRODUCTION"`

> NOTE: The `reset-database` script  `DROPS` the `"items"` database each time you run it.

## Building and running locally

Directly, from the command line: `dotnet run -p Workers/DatabaseSyncWorker`

### Building for the cloud

**Windows**
`scripts/publish-linux-workers.ps1`

**macOS/Linux**
`scripts/publish-linux-workers.sh`

This will build a self-contained appplication, which can be found in `Workers/DatabaseSyncWorker/bin/x64/Release/netcoreapp2.2/linux-x64/publish`, ready for upload.

The executable entry point is `Workers/DatabaseSyncWorker/bin/x64/Release/netcoreapp2.2/linux-x64/publish/DatabaseSyncWorker`.

# Interacting with the database

The DBSync worker is authoritative over an entity with a `DatabaseSyncService` component on it.
This component provides commands like `Create`, `GetItems` and `Increment`.

See [database_sync.schema] for the documentation for each specific command.

[database_sync.schema] defines a `DatabaseSyncItem` type.
This is what is stored in the database. Each instance of a `DatabaseSyncItem` is a single row, stored in a table with three columns (`name`, `count` and `path`.)

* `count` is the count of this item. This allows for easily stackable and consumable items.
* `name` is the user-defined identifier for this item. It can reference a type of entity to spawn, an item in a catalog in another database, or any other meaningful identifier.
* `path` is the unique path to the item. It's presented as a string type, but inside of the table it's actually an [ltree], short for "label tree", which allows for efficient queries of hierarchies within PostgreSQL. Paths look like this: `path.to.an.item`.


## Authorization

How do we keep `player1` from peeking into `player2`'s profile, or worse, from taking or modifying their items?

DBSync has a couple of levels of authorization. Keep in mind this only applies to *commands* sent to DBSync's `DatabaseSyncService`.
You need to use the usual SpatialOS [ACLs] to control the visibility of components to other clients and workers.

DBSync leverages [System Entities] to securely associate a client worker with a profile path.
Currently, the connecting player must specify its profile path as its "playerId" when connecting via the [Locator].
This means that the [Development Authentication Flow] is required for local development.

### Write

Only workers of specific Worker types are allowed to make requests that write to the database.
The `DatabaseSyncService` component has a list of these Worker types.

When you create the `DatabaseSyncService` component, add the write-authorized Worker types to the `write_worker_types` list that it contains.

> It's very unlikely that any of your clients' Worker types would ever be in this list.

### Read

When a client logs in, they are provided a unique `WorkerId` which SpatialOS includes with every command request.

If `player2` logs in and sends a `GetItem('profiles.player1')` request to DBSync, it will be rejected, since `player1` isn't associated with `player2`'s worker.

## Handling failures

Commands can fail for a variety of reasons. When processing a failed `CommandResponse` where the `StatusCode` is `ApplicationError`, you can then inspect the `Message` field for more details.

[database_sync.schema] defines a `CommandErrors` enumeration, whose numeric value is stored in the `Message` field of a failed command.

For example, if you send an `Increment` command for a component your worker is not authorized to modify, then the `Message` field will be `"1001"`, which maps to `CommandErrors.Unauthorized`.

In the case of `BatchOperationRequest`, the response will be a comma-separated list of enumeration codes.

For example, if you send a `BatchOperationRequest([Increment, Delete])`, and the `Increment`  request is allowed, but the `Delete` request is unauthorized, then the `Message` field will be `"0,1001"`, which maps to `CommandErrors.None,CommandErrors.Unauthorized`.

## Receiving update notifications

`DatabaseSyncService` provides a `paths_updated` event to notify interested workers of changes to specific paths in the datbase. Workers can then query paths they're interested in with `GetItem` or `GetItems` to see the new changes.

> This API exists as a stopgap solution while we finish implementing the final goal, [auto-mapping components](#Auto-mapping-DatabaseSyncItems-to-and-SpatialOS-components)

External changes to the database (for example, from a storefront or other external services) will also result in notifications. DBSyncWorker receives and re-broadcasts notifications from the database to your workers.

# Future work

## Versioning of `DatabaseSyncItems`

Currently, your worker may send a request to modify the `count` field in the database, but due to overload or other network failures, the command response may time out, even though DBSync successfully modified the database. This may cause it to retry the modification, resulting in double-increments or other unwanted changes.

We have plans to mitigate this by adding the concept of "version" which will reject requests that don't match the expected version of the item.

## Auto-mapping `DatabaseSyncItems` to and from SpatialOS components

Sometimes, it may be more natural to interact with data in the database in terms of components that are automatically replicated to the database.
This allows you to have persistent data, while also dealing with your types in a higher-level than `DatabaseSyncItems`. This also allows you to leverage SpatialOS's interest systems for efficiently broadcasting state updates throughout your game.

The backend work for this feature is complete, and integration and example work is beginning now.

# License

This software is licensed under MIT. See the [LICENSE](./LICENSE.md) file for details.

# Contributing

We currently don't accept PRs from external contributors - sorry about that! We do accept bug reports and feature requests in the form of issues, though.

[worker project]: https://github.com/improbable/dotnet_core_worker/README.md
[launch configuration]: https://docs.improbable.io/reference/latest/shared/project-layout/launch-configuration#worker-launch-configuration
[schema]: https://docs.improbable.io/reference/latest/shared/schema/reference
[ltree]: https://www.postgresql.org/docs/current/ltree.html
[annotations]: https://docs.improbable.io/reference/latest/shared/schema/reference#annotations
[layers]: https://docs.improbable.io/reference/latest/shared/concepts/layers#layers
[ACLs]: https://docs.improbable.io/reference/latest/javasdk/using/creating-and-deleting-entities#entity-acls
[components]: https://docs.improbable.io/reference/latest/shared/design/design-components#components
[commands]: https://docs.improbable.io/reference/latest/shared/design/commands#component-commands
[worker flags]: https://docs.improbable.io/reference/latest/shared/worker-configuration/worker-flags#worker-flags
[worker-flag set]: https://docs.improbable.io/reference/latest/shared/spatial-cli/spatial-project-deployment-worker-flag-set#spatial-project-deployment-worker-flag-set
[entity query]: https://docs.improbable.io/reference/latest/csharpsdk/using/sending-data#entity-queries
[deployment configuration]: https://docs.improbable.io/reference/latest/shared/project-layout/launch-config#reference-format
[`Improbable/Postgres/Improbable.Postgres.Schema/schema/improbable/postgres/postgres.schema`]: https://github.com/spatialos/csharp-worker-template/tree/master/Improbable/Postgres/Improbable.Postgres.Schema/schema/improbable/postgres/postgres.schema
[`Improbable/DatabaseSync/Improbable.DatabaseSync.Schema/schema/improbable/database_sync/database_sync.schema`]: https://github.com/spatialos/csharp-worker-template/tree/master/Improbable/DatabaseSync/Improbable.DatabaseSync.Schema/schema/improbable/database_sync/database_sync.schema
[database_sync.schema]: ./Improbable/DatabaseSync/Improbable.DatabaseSync.Schema/schema/improbable/database_sync/database_sync.schema
[Command Response]: https://docs.improbable.io/reference/latest/csharpsdk/api-reference#improbable-worker-commandresponseop-c-struct
[System Entities]: https://docs.improbable.io/reference/latest/shared/design/system-entities#system-entities
[Structured Project Layout]: https://docs.improbable.io/reference/latest/shared/glossary#structured-project-layout-spl
[Flexible Project Layout]: https://docs.improbable.io/reference/latest/shared/glossary#flexible-project-layout-fpl
[schema compiler]: https://docs.improbable.io/reference/latest/shared/schema/introduction#schema-compiler-cli-reference
[Example Project]: https://github.com/spatialos/UnrealGDKExampleProject
[this tutorial]: TODO
[Development Authentication Flow]: https://docs.improbable.io/reference/13.8/shared/auth/development-authentication#development-authentication-flow
[Locator]: https://docs.improbable.io/reference/13.8/shared/glossary#locator
