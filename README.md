# Using SpatialOS to talk to a database (preview)

First, some pre-requisites:
* Moderate to experienced familiarity with SpatialOS concepts, project structure and configuration. *This project is intended for developers who want to extend their eexisting SpatialOS project with new capabilities.*
* Some level of comfort with cmd/Powershell, or bash.

If this suits you, then read on!

# Premise
Sometimes, your game has data that:

1. Needs to live longer than the lifetime of a deployment
2. Needs to be used by SpatialOS workers and clients
3. Can be accessible to other services and platforms, outside of SpatialOS

A simple example of this might be players' permanent data, which could contain persistent character unlocks, stats and customisations.

1. The profile may need to move between deployments, for example, in session-based games
2. Clients and workers both need to view and modify the profile based on what the player does in-game
3. Online stores, stats services and customer services need access to the profile information

We think that as soon as the data needs to be read and modified by SpatialOS workers, then it should be presented in the same way as all other SpatialOS data.
That is, in terms of [components], component updates and [commands].
This lets you keep your game's logic using the same data models that are already established.

**We're providing the ability to easily map a hierarchy of data back and forth between a database, a SpatialOS deployment and its workers and clients.**

The core of this ability is the Database Sync Worker (DBSync).
DBSync is based on the C# .NET Core [project](https://github.com/improbable/dotnet_core_worker/).

In order to use DBSync in your project, you'll need to do the following:

1. Setup PostgreSQL locally, and in the cloud.
2. Add the DBSync and its schema to your project.
3. Configure SpatialOS to start a single DBSync.
4. [Future work](#Auto-mapping-DatabaseSyncItems-to-and-SpatialOS-components) *Write schema components that you wish to be mirrored in and out of the database.*
5. Send DBSync commands from your workers to read and write to the hierarchy data.
6. Receive updates in your workers and clients that reflect the state of the hierarchy data in the database.

## Setup

This project is based on the .NET Core C# worker [project].

### Locally

1. First, please follow the guide for the .NET Core C# worker [project]
2. Install Postgres 11 from [postgresql.org/download/windows](https://postgresql.org/download/windows)
   1. Set the default password to `DO_NOT_USE_IN_PRODUCTION`

### In the cloud

We don't currently provide hosting for PostgreSQL in the cloud.
When you have your cloud PostgreSQL setup, see [Configure DBSync](#Configure-DBSync).

## Add DBSync schema to your project

The interface to the DBSync worker is defined by [schema], which you need to include in your project's schema.

The required files are:
[`Improbable/Postgres/Improbable.Postgres.Schema/schema/improbable/postgres/postgres.schema`]
[`Improbable/DatabaseSync/Improbable.DatabaseSync.Schema/schema/improbable/database_sync/database_sync.schema`]

You can include these files in your project in a few ways.

---

### Option 1: Copying
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

If your project invokes the schema_compiler directly, you can point it to the new `schema` folders:
```
schema_compiler
    ...arguments...
    --schema_path="<root_path>/Improbable/Postgres/Improbable.Postgres.Schema/schema"
    --schema_path="<root_path>/Improbable/DatabaseSync/Improbable.DatabaseSync.Schema/schema"
```

> Note that `--schema_path`s should be absolute, rather than relative paths.

### Option 3: Nuget package references

If your project happens to be entirely .NET Core C# based, then you can simply add Nuget references to your `GeneratedCode` project:
```
dotnet add GeneratedCode/GeneratedCode.csproj package "Improbable.Postgres.Schema" --version 0.0.1-alpha-preview-1
dotnet add GeneratedCode/GeneratedCode.csproj package "Improbable.DatabaseSync.Schema" --version 0.0.1-alpha-preview-1
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
    "receptionist",
    "--spatialos-host", "${IMPROBABLE_RECEPTIONIST_HOST}",
    "--spatialos-port", "${IMPROBABLE_RECEPTIONIST_PORT}",
    "--worker-name", "${IMPROBABLE_WORKER_ID}",
    "--logfile", "${IMPROBABLE_LOG_FILE}",
    "--postgres-from-worker-flags"
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

* The default database is `"items"`.
* The default table in this database is also `"items"`. This contains all of the hierarchical data.
* The default metrics table in this database is `"metrics"`. This contains various metrics the worker collects about timings, command counts, and failures.

> NOTE: This script `DROPS` the table each time you run it.

> NOTE: Currently, the DBSync worker assumes the name of the table is the same as the database. This is why both are `"items"`.

**Local, Windows**

`scripts/reset-database.ps1`

**Local, macOS/Linux**

`scripts/reset-database.sh`

**Remote, Windows**

`scripts/reset-database.ps1 --postgres-host "_your_instance_hostname" --postgres-username "_your_instance_username_" --postgres-password "DO_NOT_USE_IN_PRODUCTION"`

**Remote, macOS/Linux**

`scripts/reset-database.sh --postgres-host "_your_instance_hostname" --postgres-username "_your_instance_username_" --postgres-password "DO_NOT_USE_IN_PRODUCTION"`

## Building for local

Directly, from the command line: `dotnet run -p Workers/DatabaseSyncWorker receptionist`

If running from Visual Studio or Rider, make sure to add the `receptionist` command to your run configuration.

### Building for the cloud

**Windows**
`scripts/publish-linux-workers.ps1`

**macOS/Linux**
`scripts/publish-linux-workers.sh`

This will build a self-contained appplication, which can be found in `Workers/DatabaseSyncWorker/bin/x64/Release/netcoreapp2.2/linux-x64/publish`, ready for upload.

The executable entry point is `Workers/DatabaseSyncWorker/bin/x64/Release/netcoreapp2.2/linux-x64/publish/DatabaseSyncWorker`.

# Interacting with the database

The DBSync worker runs an entity with a `DatabaseSyncService` component on it.
This component provides commands like `Create`, `GetItems` and `Increment`.

See [database_sync.schema] for the documentation for each specific command.

[database_sync.schema] defines a `DatabaseSyncItem` type.
This is what is stored in the database. Each instance of a `DatabaseSyncItem` is a single row, stored in a table with three columns (`name`, `count` and `path`.)

* `count` is the count of this item. This allows for easily stackable and consumable items.
* `name` is the user-defined identifier for this item. It can reference a type of entity to spawn, an item in a catalog in another database, or any other meaningful identifier.
* `path` is the unique path to the item. It's presented as a string type, but inside of the table it's actually an [ltree], short for "label tree", which allows for efficient queries of hierarchies within PostgreSQL. Paths look like this: `path.to.an.item`.

## Authorization

How do we keep `player1` from peeking into `player2`'s profile, or worse, from stealing all their items?

DBSync has a couple of levels of authorization. Keep in mind this only applies to *commands* sent to DBSync's `DatabaseSyncService`. You need to use the usual SpatialOS [ACLs] to control the visibility of components to other clients and workers.

### Write

Only workers within specified [layers] are allowed to make requests that write to the database.
The `DatabaseSyncService` component has a list of these layers, allowing you to break up workers' areas of concerns into different layers, if you like.

When you create the `DatabaseSyncService` component, add the write-authorized layers to the `write_worker_attributes` list that it contains. It's very unlikely that client-related layers would ever be in this list.

### Read

When a client logs in, they are provided a unique WorkerId which SpatialOS includes with every command request. When the client's entities are created, an authorized worker can also create an association between a profile root, and this unique ID.

It can do this by sending the `associate_path_with_client` command to DBSync in order to allow it. For example, `associate_path_with_client(profiles.player1 -> workerId:Client-{0e61a845-e978-4e5f-b314-cc6bf1929171})`.

Later, when `player2` logs in and sends a `GetItem('profiles.player1')` request to DBSync, it will be rejected, since `player1` isn't associated with `player2`'s worker.

> If your clients never directly interact with the DBSync worker, then you don't need to do this.

## Handling failures

Commands can fail for a variety of reasons. When processing a failed `CommandResponse`, if the `StatusCode` is `ApplicationError`, then you can inspect the `Message` field for more details.

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

## Leverage [System Entities] for player identity

There is an `associate_path_with_client` command, used to tie client workers to specific paths. The need for this should be removed when we leverage the features of [System Entities].

## Auto-mapping `DatabaseSyncItems` to and SpatialOS components

Sometimes, it may be more natural to interact with data in the database in terms of components that are automatically replicated to the database.
This allows you to have persistent data, while also dealing with your types in a higher-level than `DatabaseSyncItems`. This also allows you to leverage SpatialOS's interest systems for efficiently broadcasting state updates throughout your game.

The backend work for this feature is complete, and integration and example work is beginning now.


[project]: https://github.com/improbable/dotnet_core_worker/README.md
[launch configuration]: https://docs.improbable.io/reference/13.8/shared/project-layout/launch-configuration#worker-launch-configuration
[schema]: https://docs.improbable.io/reference/13.8/shared/schema/reference
[ltree]: https://www.postgresql.org/docs/current/ltree.html
[annotations]: https://docs.improbable.io/reference/13.8/shared/schema/reference#annotations
[layers]: https://docs.improbable.io/reference/13.8/shared/concepts/layers#layers
[ACLs]: https://docs.improbable.io/reference/13.8/javasdk/using/creating-and-deleting-entities#entity-acls
[components]: https://docs.improbable.io/reference/13.8/shared/design/design-components#components
[commands]: https://docs.improbable.io/reference/13.8/shared/design/commands#component-commands
[worker flags]: https://docs.improbable.io/reference/13.8/shared/worker-configuration/worker-flags#worker-flags
[worker-flag set]: https://docs.improbable.io/reference/13.8/shared/spatial-cli/spatial-project-deployment-worker-flag-set#spatial-project-deployment-worker-flag-set
[entity query]: https://docs.improbable.io/reference/13.8/csharpsdk/using/sending-data#entity-queries
[deployment configuration]: https://docs.improbable.io/reference/13.8/shared/project-layout/launch-config#reference-format
[`Improbable/Postgres/Improbable.Postgres.Schema/schema/improbable/postgres/postgres.schema`]: ./Improbable/Postgres/Improbable.Postgres.Schema/schema/improbable/postgres/postgres.schema
[`Improbable/DatabaseSync/Improbable.DatabaseSync.Schema/schema/improbable/database_sync/database_sync.schema`]: ./Improbable/DatabaseSync/Improbable.DatabaseSync.Schema/schema/improbable/database_sync/database_sync.schema
[database_sync.schema]: ./Improbable/DatabaseSync/Improbable.DatabaseSync.Schema/schema/improbable/database_sync/database_sync.schema
[Command Response]: https://docs.improbable.io/reference/13.8/csharpsdk/api-reference#improbable-worker-commandresponseop-c-struct
[System Entities]: https://docs.improbable.io/reference/13.8/shared/design/system-entities#system-entities
