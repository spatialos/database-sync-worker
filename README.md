# Using SpatialOS to talk to a database (preview)

Sometimes, your game has data that:

1. Needs to live longer than the lifetime of a deployment
1. Needs to be used by SpatialOS workers and clients
1. Can be accessible to other services and platforms, outside of SpatialOS

A simple example of this might be players' permanent data, which could contain persistent character unlocks, stats and customisations.

1. The profile may need to move between deployments, for example, in session-based games
2. Clients and workers both need to view and modify the profile based on what the player does in-game
3. Online stores, stats services and customer services need access to the profile information

We think that as soon as the data needs to be read and modified by SpatialOS workers, then it should be presented in the same way as all other SpatialOS data.
That is, in terms of [components], component updates and [commands].
This lets you keep your game's logic using the same data models that are already established.
We're providing the ability to easily map a hierarchy of data back and forth between a database, a SpatialOS deployment and its workers and clients.

The core of this ability is the Hierarchy Persistence Worker (HPW). HPW is based on the C# .NET Core project **TODO: Link to github once the repo is complete.**

In order to use HPW in your project, you'll need to do the following:

1. Setup PostgreSQL locally, and in the cloud.
2. Add the HPW and its schema to your project.
3. Configure SpatialOS to start a single HPW.
4. Write schema components that you wish to be mirrored in and out of the database.
5. Send HPW commands from your workers to read and write to the hierarchy data.
6. Receive component updates in your workers and clients that reflect the state of the hierarchy data in the database.

## Setup

This project is based on the .NET Core C# worker. Please visit [here] for more information.

### Locally

1. First, please follow the guide for the .NET Core worker [here]
1. Install Postgres 11 from [postgresql.org/download/windows](https://postgresql.org/download/windows)
1. Set the default password to `DO_NOT_USE_IN_PRODUCTION`.

### In the cloud

We don't currently provide hosting for PostgreSQL in the cloud.
When you have your cloud PostgreSQL setup, enter the appropriate details in `config/deployment.json` to allow HPW to connect to it:


## Add HPW to your project

1. *Flesh out* Make sure `Improbable.CodeGen.CSharp/Improbable.CodeGen.CSharp.HierarchyPersistence/schema/hierarchy.schema` is visible to your project's schema compiler.

## Configure HPW

When running locally, HPW will connect to your local PostgreSQL using some defaults.

When running in the cloud, HPW will need some configuration to connect to your hosted PostgreSQL instance.
This is accomplished by modifying [worker flags].

In `config/deployment.json`, modify the value of `postgres_connection_string` with the details of your PostgreSQL instance.

The default is `"Host=<hostname>;Username=postgres;Password=DO_NOT_USE_IN_PRODUCTION;Database=hierarchy;NoResetOnClose=true"`

You can also change this flag at runtime using [worker-flag set]; HPW will detect the changes and connect using the new details.

## Concepts

What is a `HierarchyItem`

Components containing `HierarchyItems`

int value vs. list of `HierarchyItems`

Write authority

Topology of the setup

## Write schema components

You can write components that will be stored in database.
These components have some rules that normal components don't:

1. They must have a `string` field that

## Initialize the database

1. Create profiles

## Run a local deployment

### Send commands

### Handle component updates

# How it works

> This sections assumes that you're moderately familiar with SpatialOS schema and schema features like [annotations].

HPW includes a SpatialOS schema file that specifies the `HierarchyItem` component.

```
type HierarchyItem {
    string name = 1;
    int64 count = 2;
    string path = 3;
}
```
> For the sake of brevity, we've omitted some annotations that tell our code generator how to generate SQL used during the database initialisation process.

This is what is stored in the database. Each instance of a `HierarchyItem` is a single row, stored in a table with three columns (`name`, `count` and `path`.)

When you write components that are stored in the database, each field in the component has a matching `HierarchyItem` row.

* `count` is the count of this item. This allows for easily stackable and consumable items.
* `name` is the user-defined identifier for this item. It can reference a type of entity to spawn, an item in a catalog in another database, or any other meaningful identifier.
* `path` is the unique path to the item. It's presented as a string type, but inside of the table it's actually an [ltree], short for "label tree", which allows for efficient queries of hierarchies within PostgreSQL. Paths look like this: `path.to.an.item`.

Let's start with this component.

```
component PlayerStats {
    id = 30002;

    [improbable.hierarchy.ProfileIdAttribute]
    string profile_id = 1;

    [improbable.hierarchy.ValueAttribute]
    int64 kills = 2;

    [improbable.hierarchy.ValueAttribute]
    int64 deaths = 3;

    [improbable.hierarchy.ValueListAttribute]
    list<improbable.hierarchy.HierarchyItem> items = 4;
}
```

The code generator will visit this schema component and see that it has fields with annotations on it. This will cause it to generate code that can transform a list of individual `HierarchyItem` rows into a `SchemaComponentUpdate` that allows other SpatialOS workers to view all of these items as a single component.

Briefly: The `ValueAttribute` and `ValueListAttribute` let the code generator know that these fields should be used to map `count` and `HierarchyItems` from the database. The `ProfileIdAttribute` is used to provide the root of the hierarchy to query from. More on that below.

Let's walk through the flow of data.

### Bootstrapping

We create a profile for a player (`player1`), and part of that process includes writing the default fields of this component as rows in the database:

```
{
    kills: 0,
    deaths: 0,
    items: <empty>
}
```

When written to the database this turns into

|name|count|path|
|----|--------:|----|
|yourgame.player.kills|0|profiles.player1.30002_2|
|yourgame.player.deaths|0|profiles.player1.30002_3|
|yourgame.player.items|0|profiles.player1.30002_4|

What's the weird `30002_2` in the path? That's the component id of `PlayerStats` (30002) plus the field id of each field (`2`). This brings the same stability guarantees as provided for copmonents saved into deployment snapshots. That is, fields can be renamed and moved around, but as long as the component id and the field id are constant, everything stays compatible.

### Running in a deployment: Lifecycle

1. We start up a deployment, which starts up the HPW.
The HPW provides a "service" entity that listens for commands. Your game's logic workers can send an [entity query] to find the entity with the `HierarchyService` component, and then send that entity commands.

2. At some point, `player1` connects and creates an entity with a `PlayerStats` component on it.
When the HPW gains authority over `PlayerStats`, it knows that it needs to "hydrate" it from the database.
*Hydrate* means that the data in the component come entirely from the database. SpatialOS just provides a convenient view of that data as a component.

3. It sends off a query to the database for all of the children of `profiles.player1` and receives a list back. It then matches the `path` field of each item to the corresponding field id and writes the values to the component update, which it then sends to SpatialOS, where it fans out to all interested workers like any other component update.

4. Later still, the unfortunate player dies. A logic worker sees this, and decides to permanently save it to the database. To do this, it sends a command to the HPW's service entity:

   1. `Increment('profiles.player1.30002_3', 1, 'Client-{2c07a843-4d6f-4e4e-943b-9d637fe67743}')`.

   2. The HPW receives this command, and sends an `UPDATE` command to the database, then sends a succesful command response once the database acknowledges its been written. :arrow_left: `IncrementResponse(newValue)`.
   3. Later, the HPW will re-hydrate the component and send out a component update to SpatialOS.
```
{
    deaths: 1
}
```
5. In order to avoid any more deaths, `player1` opens the game's web store on their phone and buys a 10 pack of health potions. The store backend writes this to the database:

|name|count|path|
|----|--------:|----|
|yourgame.player.kills|0|profiles.player1.30002_2|
|yourgame.player.deaths|0|profiles.player1.30002_3|
|yourgame.player.items|0|profiles.player1.30002_4|
|yourgame.items.health_potion|10|profiles.player1.30002_4.4acaebd9|

> `4acaebd9` will stand for a shortened GUID representing this unique item purchased in the store

6. HPW receives a notification from the database that `profiles.player1.30002_4` has changed, and it "re-hydrates" the component, turning it back into a SpatialOS component update:

```
{
    kills: 0,
    deaths: 1
    items: [
        {
            name: "yourgame.items.health_potion",
            count: 10,
            path: "profiles.player1.30002_4.4acaebd9"
        }
    ]
}
```
1. `player1`'s client receives this update and redraws the inventory screen showing the new purchase.

### Runtime: Authorization

How do we keep `player1` from peeking into `player2`'s profile, or worse, from stealing all their items?

HPW has a couple of levels of authorization. Keep in mind this only applies to *commands* sent to HPW's `HierarchyService`. You need to use SpatialOS [ACLs] to control the visibility of components to other clients and workers.

#### Write

Only workers within specified [layers] are allowed to make requests that write to the database.
The `HierarchyService` component has a list of these layers, allowing you to break up workers' areas of concerns into different layers, if you like.

#### Read

When a client logs in, they are provided a unique WorkerId which SpatialOS includes with every command request. When the client's entities are created, an authorized worker can create an association between a profile root, and this unique ID. For example, `profiles.player1 -> workerId:Client-{0e61a845-e978-4e5f-b314-cc6bf1929171}`. If `player2` logs in and sends a `GetItem('profiles.player')` request to HPW, it will be rejected, since `player1` isn't associated with `player2`'s worker.

# Limitations and caveats

# Future work


[here]: https://github.com/improbable/dotnet_core_worker/README.md
[ltree]: https://www.postgresql.org/docs/current/ltree.html
[annotations]: https://docs.improbable.io/reference/13.8/shared/schema/reference#annotations
[layers]: https://docs.improbable.io/reference/13.8/shared/concepts/layers#layers
[ACLs]: https://docs.improbable.io/reference/13.8/javasdk/using/creating-and-deleting-entities#entity-acls
[components]: https://docs.improbable.io/reference/13.8/shared/design/design-components#components
[commands]: https://docs.improbable.io/reference/13.8/shared/design/commands#component-commands
[worker flags]: https://docs.improbable.io/reference/13.8/shared/worker-configuration/worker-flags#worker-flags
[worker-flag set]: https://docs.improbable.io/reference/13.8/shared/spatial-cli/spatial-project-deployment-worker-flag-set#spatial-project-deployment-worker-flag-set
[entity query]: https://docs.improbable.io/reference/13.8/csharpsdk/using/sending-data#entity-queries
