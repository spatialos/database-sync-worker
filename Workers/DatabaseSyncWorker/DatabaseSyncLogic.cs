using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Improbable.DatabaseSync;
using Improbable.Stdlib;
using Improbable.Postgres;
using Improbable.Restricted;
using Improbable.Worker.CInterop;
using Newtonsoft.Json;
using Npgsql;
using NpgsqlTypes;
using Serilog;
using Metrics = Improbable.Worker.CInterop.Metrics;
using OpList = Improbable.Stdlib.OpList;

namespace DatabaseSyncWorker
{
    public class DatabaseSyncLogic : IDisposable
    {
        private static readonly UpdateParameters NoLoopbackParameters = new UpdateParameters { Loopback = ComponentUpdateLoopback.None };

        private readonly ComponentCollection<PlayerClient> clients = PlayerClient.CreateComponentCollection();
        private readonly ComponentCollection<Worker> workers = Worker.CreateComponentCollection();
        private readonly WhenAllComponents whenWorker = new WhenAllComponents(Worker.ComponentId);
        private readonly WhenAllComponents whenWorkerClient = new WhenAllComponents(Worker.ComponentId, PlayerClient.ComponentId);

        private static IReadOnlyDictionary<uint, Reflection.HydrationType>? _restoreComponents;
        private readonly ConcurrentDictionary<string, string> clientWorkers = new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<string, string> adminWorkers = new ConcurrentDictionary<string, string>();
        private readonly WorkerConnection connection;
        private readonly EntityId serviceEntityId;
        private readonly Dictionary<uint, WhenAllComponents> hydrateAllComponents = new Dictionary<uint, WhenAllComponents>();
        private readonly ConcurrentDictionary<string, DateTime> pendingUpdates = new ConcurrentDictionary<string, DateTime>();
        private readonly ConcurrentDictionary<string, EntityId> profileToEntityId = new ConcurrentDictionary<string, EntityId>();
        private readonly DatabaseSyncService.CommandSenderBinding service;
        private readonly HashSet<string> writeWorkerTypes;
        private readonly PostgresOptions postgresOptions;
        private readonly string tableName;
        private long concurrentBatchRequests;
        private readonly MetricsPusher metricsPusher;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();

        public DatabaseSyncLogic(PostgresOptions postgresOptions, string tableName, WorkerConnection connection, EntityId serviceEntityId, in DatabaseSyncService serviceEntity)
        {
            this.postgresOptions = postgresOptions;
            this.connection = connection;
            this.serviceEntityId = serviceEntityId;

            service = new DatabaseSyncService.CommandSenderBinding(connection, serviceEntityId);

            this.tableName = tableName;

            foreach (var (key, _) in HydrateComponents)
            {
                hydrateAllComponents.Add(key, new WhenAllComponents(key));
            }

            if (!serviceEntity.WriteWorkerTypes.Any())
            {
                throw new Exception($"{nameof(DatabaseSyncService)} has no write worker types defined.");
            }

            writeWorkerTypes = new HashSet<string>(serviceEntity.WriteWorkerTypes);
            adminWorkers.TryAdd(connection.WorkerId, connection.WorkerId);

            metricsPusher = new MetricsPusher(this.postgresOptions);
            metricsPusher.StartPushingMetrics(TimeSpan.FromSeconds(10));

            StartWatchingDatabase();
        }

        private void StartWatchingDatabase()
        {
            Task.Factory.StartNew(async unusedStateObject =>
            {
                NpgsqlConnection sqlConnection = null;

                while (!cts.IsCancellationRequested && connection.GetConnectionStatusCode() == ConnectionStatusCode.Success)
                {
                    var connectionString = postgresOptions.ConnectionString;
                    try
                    {
                        sqlConnection = new NpgsqlConnection(connectionString);
                        sqlConnection.Open();

                        Log.Information("Listening to {TableName}", tableName);

                        sqlConnection.Notification += (sender, args) =>
                        {
                            try
                            {
                                var changeNotification = JsonConvert.DeserializeObject<DatabaseSyncItem.DatabaseChangeNotification>(args.Payload);
                                Task.Run(() => ProcessDatabaseSyncChanges(changeNotification));

                                Improbable.Postgres.Metrics.Inc(Improbable.Postgres.Metrics.TotalChangesReceived);
                            }
                            catch (Exception e)
                            {
                                Log.Error(e, "While parsing JSON for change notification");
                            }
                        };

                        // Receive notifications from the database when rows change.
                        using (var cmd = new NpgsqlCommand($"LISTEN {tableName}", sqlConnection))
                        {
                            cmd.ExecuteNonQuery();
                        }

                        while (sqlConnection.State == ConnectionState.Open)
                        {
                            await sqlConnection.WaitAsync(cts.Token).ConfigureAwait(false);
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        // Don't log, avoid adding confusion to logs on a graceful shutdown.
                        break;
                    }
                    catch (Exception e)
                    {
                        if (!cts.Token.IsCancellationRequested)
                        {
                            Log.Error(e, "LISTEN {TableName}", tableName);
                        }
                    }
                    finally
                    {
                        try
                        {
                            sqlConnection?.Dispose();
                        }
                        catch
                        {
                            // This is noisy, quiet it down.
                        }

                    }

                    // Reconnection delay.
                    await Task.Delay(TimeSpan.FromSeconds(5), cts.Token).ConfigureAwait(false);
                }

                Log.Information("Stopped listening to {TableName}", tableName);
            }, cts.Token, TaskCreationOptions.LongRunning);
        }

        public static IReadOnlyDictionary<uint, Reflection.HydrationType> HydrateComponents => _restoreComponents ??= Reflection.FindHydrateMethods();

        public void ProcessOpList(OpList opList)
        {
            whenWorker.ProcessOpList(opList);
            whenWorkerClient.ProcessOpList(opList);

            foreach (var workerEntityId in whenWorker.Deactivated)
            {
                if (clients.TryGet(workerEntityId, out var client))
                {
                    if (clientWorkers.TryRemove(client.PlayerIdentity.PlayerIdentifier, out _))
                    {
                        Log.Debug("Signed out client {Id}", client.PlayerIdentity.PlayerIdentifier);
                    }
                    else
                    {
                        Log.Warning("Failed to sign out {Id}", client.PlayerIdentity.PlayerIdentifier);
                    }
                }
                else if (workers.TryGet(workerEntityId, out var worker))
                {
                    if (adminWorkers.TryRemove(worker.WorkerId, out _))
                    {
                        Log.Debug("Signed out admin {Id}", worker.WorkerId);
                    }
                    else
                    {
                        Log.Warning("Failed to sign out admin {Id}", worker.WorkerId);
                    }
                }
                else
                {
                    Log.Error("Unknown worker {EntityId}", workerEntityId);
                }
            }

            clients.ProcessOpList(opList);
            workers.ProcessOpList(opList);

            foreach (var entityId in whenWorkerClient.Activated)
            {
                if (!clients.TryGet(entityId, out var client) || !workers.TryGet(entityId, out var worker))
                {
                    continue;
                }

                Log.Debug("Logged in {Id} => {Path}", worker.WorkerId, client.PlayerIdentity.PlayerIdentifier);
                clientWorkers.AddOrUpdate(client.PlayerIdentity.PlayerIdentifier, worker.WorkerId, (key, oldValue) => worker.WorkerId);
            }

            foreach (var entityId in whenWorker.Activated)
            {
                if (!workers.TryGet(entityId, out var worker) || !writeWorkerTypes.Contains(worker.WorkerType))
                {
                    continue;
                }

                Log.Debug("Logged in admin {Id} => {Type} {Self}", worker.WorkerId, worker.WorkerType, worker.WorkerId == connection.WorkerId ? "(self)": "");
                adminWorkers.AddOrUpdate(worker.WorkerId, worker.WorkerType, (key, oldValue) => worker.WorkerType);
            }

            foreach (var (componentId, components) in hydrateAllComponents)
            {
                components.ProcessOpList(opList);

                var activated = components.Activated;

                if (activated.IsEmpty)
                {
                    continue;
                }

                // Extract the profileId from the incoming schema data, to avoid needing to deserialize the whole object.
                var ops = opList
                    .OfOpType<AddComponentOp>()
                    .OfComponent(componentId)
                    .Where(op => op.Data.SchemaData.HasValue && activated.Contains(op.EntityId))
                    .ToDictionary(op => new EntityId(op.EntityId), op =>
                    {
#pragma warning disable 8629 // Nullable value type may be null. <- the presence of a value is checked in the Where query above.
                        var profile = HydrateComponents[componentId].ProfileIdFromSchemaData(op.Data.SchemaData.Value.GetFields());
#pragma warning restore 8629
                        if (string.IsNullOrEmpty(profile))
                        {
                            Log.Error("{EntityId} has an empty profileId field", op.EntityId);
                        }

                        return profile;
                    });

                foreach (var (entityId, profile) in ops)
                {
                    profileToEntityId.AddOrUpdate(profile, entityId, (oldProfile, oldEntityId) => entityId);
                }

                Parallel.ForEach(activated, async entityId => await HydrateComponentAsync(ops[entityId], componentId, entityId).ConfigureAwait(false));
            }

            foreach (var commandRequestOp in opList
                .OfOpType<CommandRequestOp>()
                .OfComponent(DatabaseSyncService.ComponentId))
            {
                var commandType = DatabaseSyncService.GetCommandType(commandRequestOp);

                Improbable.Postgres.Metrics.Inc($"CommandIndex.{commandType}");

                switch (commandType)
                {
                    case DatabaseSyncService.Commands.GetItem:
                        HandleGetItemRequest(commandRequestOp);
                        break;

                    case DatabaseSyncService.Commands.GetItems:
                        HandleGetItemsRequest(commandRequestOp);
                        break;

                    case DatabaseSyncService.Commands.Increment:
                        HandleIncrementRequest(commandRequestOp);
                        break;

                    case DatabaseSyncService.Commands.Decrement:
                        HandleDecrementRequest(commandRequestOp);
                        break;

                    case DatabaseSyncService.Commands.SetParent:
                        HandleSetParentRequest(commandRequestOp);
                        break;

                    case DatabaseSyncService.Commands.Create:
                        HandleCreateRequest(commandRequestOp);
                        break;

                    case DatabaseSyncService.Commands.Delete:
                        HandleDeleteRequest(commandRequestOp);
                        break;

                    case DatabaseSyncService.Commands.Batch:
                        HandleBatch(commandRequestOp);
                        break;

                    default:
                        Log.Error("Unhandled commandType {CommandType}", commandType);
                        break;
                }
            }
        }

        private void HandleDeleteRequest(CommandRequestOp commandRequestOp)
        {
            var deleteRequest = DeleteRequest.Create(commandRequestOp.Request.SchemaData);

            if (!ValidateBaseRequest(deleteRequest.WorkerId, deleteRequest.Path))
            {
                Log.Error("Invalid {@Request}", deleteRequest);
                connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.InvalidRequest);
                return;
            }

            if (!AuthorizeDelete(commandRequestOp, deleteRequest))
            {
                Log.Error("Unauthorized {@Request}", deleteRequest);
                connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.Unauthorized);

                return;
            }

            Task.Run(() =>
            {
                using var wrapper = new ConnectionWrapper(postgresOptions.ConnectionString);
                using var cmd = wrapper.Command();
                try
                {
                    AddDeleteStatement(cmd.Command, deleteRequest);

                    cmd.Command.CommandText = cmd.Command.CommandText.Replace("$TABLENAME", tableName);
                    cmd.Command.Prepare();

                    var affected = (ulong) cmd.Command.ExecuteNonQuery();
                    Log.Debug("Removed {Count} rows", affected);

                    service.SendDeleteResponse(commandRequestOp.RequestId, new DeleteResponse(affected));
                }
                catch (Exception e)
                {
                    Log.Error(e, "{@Request}: {Sql}", deleteRequest, cmd.Command.CommandText);
                    connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.InvalidRequest);
                }
            });
        }

        private bool AuthorizeDelete(CommandRequestOp commandRequestOp, DeleteRequest deleteRequest)
        {
            return CanWorkerTypeWrite(commandRequestOp) && IsRequestValid(deleteRequest.WorkerId, deleteRequest.Path);
        }

        private static void AddCreateStatement(NpgsqlCommand command, CreateRequest createRequest, string suffix = "")
        {
            var query = $@"insert into $TABLENAME (path, name, count) values(@path{suffix}, @name{suffix}, @count{suffix});";
            command.Parameters.AddWithValue($"name{suffix}", NpgsqlDbType.Text, createRequest.Item.Name);
            command.Parameters.AddWithValue($"path{suffix}", NpgsqlDbType.Unknown, createRequest.Item.Path);
            command.Parameters.AddWithValue($"count{suffix}", NpgsqlDbType.Bigint, createRequest.Item.Count);

            command.CommandText += query;
        }

        private static void AddDeleteStatement(NpgsqlCommand command, DeleteRequest deleteRequest, string suffix = "")
        {
            var query = $@"delete from $TABLENAME where path <@ @path{suffix};";
            command.Parameters.AddWithValue($"path{suffix}", NpgsqlDbType.Unknown, deleteRequest.Path);

            command.CommandText += query;
        }

        private static void AddSetParentStatement(NpgsqlCommand command, SetParentRequest setParentRequest, string suffix = "")
        {
            // '||' is the string/ltree concatenation operator, that is, (newParent + subpath(path, nlevel(sourcePath) - 1)
            var query = $"update $TABLENAME set path = @newParent{suffix} || subpath(path, nlevel(@sourcePath) - 1) where path <@ @sourcePath{suffix} returning path::text;";
            command.Parameters.AddWithValue($"newParent{suffix}", NpgsqlDbType.Unknown, setParentRequest.NewParent);
            command.Parameters.AddWithValue($"sourcePath{suffix}", NpgsqlDbType.Unknown, setParentRequest.Path);

            command.CommandText += query;
        }

        private static void AddIncrementStatement(NpgsqlCommand command, IncrementRequest incrementRequest, string suffix = "")
        {
            var query = $"update $TABLENAME set count = count + @amount{suffix} where path ~ @itemPath{suffix} returning count;";
            command.Parameters.AddWithValue($"itemPath{suffix}", NpgsqlDbType.Unknown, incrementRequest.Path);
            command.Parameters.AddWithValue($"amount{suffix}", NpgsqlDbType.Bigint, incrementRequest.Amount);

            command.CommandText += query;
        }

        private static void AddDecrementStatement(NpgsqlCommand command, DecrementRequest decrementRequest, string suffix = "")
        {
            var query = $"update $TABLENAME set count = count - @amount{suffix} where path ~ @itemPath{suffix} returning count;";
            command.Parameters.AddWithValue($"itemPath{suffix}", NpgsqlDbType.Unknown, decrementRequest.Path);
            command.Parameters.AddWithValue($"amount{suffix}", NpgsqlDbType.Bigint, decrementRequest.Amount);

            command.CommandText += query;
        }

        private static void AddGetItemStatement(NpgsqlCommand command, GetItemRequest getItemRequest, string suffix = "")
        {
            var query = $"select {DatabaseSyncItem.SelectClause} from $TABLENAME where path ~ @path{suffix};";
            command.Parameters.AddWithValue($"path{suffix}", NpgsqlDbType.Unknown, getItemRequest.Path);

            command.CommandText += query;
        }

        private static void AddGetDatabaseSyncStatement(NpgsqlCommand command, GetItemsRequest getItems, string suffix = "")
        {
            var query = $"select {DatabaseSyncItem.SelectClause} from $TABLENAME where path ~ @itemChildren{suffix};";

            switch (getItems.Depth)
            {
                case GetItemDepth.Recursive:
                    command.Parameters.AddWithValue($"itemChildren{suffix}", NpgsqlDbType.Unknown, $"{getItems.Path}.*");
                    break;
                case GetItemDepth.ChildrenOnly:
                    command.Parameters.AddWithValue($"itemChildren{suffix}", NpgsqlDbType.Unknown, $"{getItems.Path}.*{{1}}");

                    // We query for the root item when to distinguish between empty items and non-existent items.
                    query = query.Replace(";", $" or path ~ @root{suffix};");
                    command.Parameters.AddWithValue($"root{suffix}", NpgsqlDbType.Unknown, $"{getItems.Path}");

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            command.CommandText += query;
        }

        private void HandleCreateRequest(CommandRequestOp commandRequestOp)
        {
            var createRequest = CreateRequest.Create(commandRequestOp.Request.SchemaData);

            if (!ValidateBaseRequest(createRequest.WorkerId, createRequest.Item.Path))
            {
                Log.Error("Invalid {@Request}", createRequest);
                connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.InvalidRequest);
                return;
            }

            if (!AuthorizeCreate(commandRequestOp, createRequest))
            {
                Log.Error("Unauthorized {@Request}", createRequest);
                connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.Unauthorized);

                return;
            }

            Task.Run(() =>
            {
                using (var wrapper = new ConnectionWrapper(postgresOptions.ConnectionString))
                {
                    using var cmd = wrapper.Command();
                    try
                    {
                        AddCreateStatement(cmd.Command, createRequest);
                        cmd.Command.CommandText = cmd.Command.CommandText.Replace("$TABLENAME", tableName);
                        cmd.Command.Prepare();
                        cmd.Command.ExecuteNonQuery();
                    }
                    catch (Exception e)
                    {
                        Log.Error(e, "{@Request}: {Sql}", createRequest, cmd.Command.CommandText);
                        connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.InvalidRequest);
                    }
                }

                service.SendCreateResponse(commandRequestOp.RequestId, new CreateResponse());
            });
        }

        private bool AuthorizeCreate(CommandRequestOp commandRequestOp, CreateRequest createRequest)
        {
            return CanWorkerTypeWrite(commandRequestOp) &&
                   IsRequestValid(createRequest.WorkerId, createRequest.Item.Path);
        }

        private void HandleBatch(CommandRequestOp commandRequestOp)
        {
            var batch = BatchOperationRequest.Create(commandRequestOp.Request.SchemaData);

            var validationErrors = new CommandErrors[batch.Actions.Length];

            for (var index = 0; index < batch.Actions.Length; index++)
            {
                var action = batch.Actions[index];
                var requestCount = 0;

                var authorized = false;
                var valid = false;

                if (action.Increment.HasValue)
                {
                    requestCount++;

                    authorized = AuthorizeIncrementRequest(commandRequestOp, action.Increment.Value);
                    valid = ValidateIncrementRequest(action.Increment.Value);
                }
                else if (action.Decrement.HasValue)
                {
                    requestCount++;

                    authorized = AuthorizeDecrementRequest(commandRequestOp, action.Decrement.Value);
                    valid = ValidateDecrementRequest(action.Decrement.Value);
                }
                else if (action.SetParent.HasValue)
                {
                    requestCount++;

                    authorized = AuthorizeSetParentRequest(commandRequestOp, action.SetParent.Value);
                    valid = ValidateSetParentRequest(action.SetParent.Value);
                }
                else if (action.Create.HasValue)
                {
                    requestCount++;

                    authorized = AuthorizeCreate(commandRequestOp, action.Create.Value);
                    valid = ValidateBaseRequest(action.Create.Value.WorkerId, action.Create.Value.Item.Path);
                }
                else if (action.Delete.HasValue)
                {
                    requestCount++;

                    authorized = AuthorizeDelete(commandRequestOp, action.Delete.Value);
                    valid = ValidateBaseRequest(action.Delete.Value.WorkerId, action.Delete.Value.Path);
                }
                else if (action.GetItem.HasValue)
                {
                    requestCount++;

                    authorized = AuthorizeGetItem(action.GetItem.Value);
                    valid = ValidateBaseRequest(action.GetItem.Value.WorkerId, action.GetItem.Value.Path);
                }
                else if (action.GetItems.HasValue)
                {
                    requestCount++;

                    authorized = AuthorizeGetDatabaseSync(action.GetItems.Value);
                    valid = ValidateBaseRequest(action.GetItems.Value.WorkerId, action.GetItems.Value.Path);
                }

                if (requestCount != 1 || !valid)
                {
                    validationErrors[index] = CommandErrors.InvalidRequest;
                }
                else if (!authorized)
                {
                    validationErrors[index] = CommandErrors.Unauthorized;
                }
            }

            if (validationErrors.Any(a => a != CommandErrors.None))
            {
                for (var index = 0; index < validationErrors.Length; index++)
                {
                    var response = validationErrors[index];
                    Log.Error("Request {RequestId}: Actions[{Index}] '{Code}'", commandRequestOp.RequestId, index, response);
                }

                connection.SendCommandFailure(commandRequestOp.RequestId, string.Join(",", validationErrors.Select(a => a.ToString("D"))));
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    var responses = new List<CompositeResponse>(batch.Actions.Length);

                    using var wrapper = new ConnectionWrapper(postgresOptions.ConnectionString);
                    using var transaction = wrapper.Connection.BeginTransaction(IsolationLevel.RepeatableRead);
                    Interlocked.Increment(ref concurrentBatchRequests);

                    for (var index = 0; index < batch.Actions.Length; index++)
                    {
                        var op = batch.Actions[index];
                        using var cmd = wrapper.Command();
                        if (op.Increment.HasValue)
                        {
                            AddIncrementStatement(cmd.Command, op.Increment.Value);
                        }
                        else if (op.Decrement.HasValue)
                        {
                            AddDecrementStatement(cmd.Command, op.Decrement.Value);
                        }
                        else if (op.SetParent.HasValue)
                        {
                            AddSetParentStatement(cmd.Command, op.SetParent.Value);
                        }
                        else if (op.Create.HasValue)
                        {
                            AddCreateStatement(cmd.Command, op.Create.Value);
                        }
                        else if (op.Delete.HasValue)
                        {
                            AddDeleteStatement(cmd.Command, op.Delete.Value);
                        }
                        else if (op.GetItem.HasValue)
                        {
                            AddGetItemStatement(cmd.Command, op.GetItem.Value);
                        }
                        else if (op.GetItems.HasValue)
                        {
                            AddGetDatabaseSyncStatement(cmd.Command, op.GetItems.Value);
                        }

                        cmd.Command.CommandText = cmd.Command.CommandText.Replace("$TABLENAME", tableName);

                        try
                        {
                            using var reader = cmd.Command.ExecuteReader();
                            try
                            {
                                var statement = cmd.Command.Statements[0];

                                if (op.Increment.HasValue)
                                {
                                    if (reader.Read())
                                    {
                                        responses.Add(new CompositeResponse(new IncrementResponse(reader.GetInt64(0))));
                                    }
                                    else
                                    {
                                        Log.Error("Action[{Index}]: Rows affected '{Rows}' != '1'. '{Sql}' {Parameters}", index, statement.Rows, statement.SQL, statement.InputParameters.Select(p => p.Value?.ToString()));
                                        validationErrors[index] = CommandErrors.InvalidRequest;
                                    }
                                }
                                else if (op.Decrement.HasValue)
                                {
                                    if (reader.Read())
                                    {
                                        responses.Add(new CompositeResponse(decrement: new DecrementResponse(reader.GetInt64(0))));
                                    }
                                    else
                                    {
                                        Log.Error("Action[{Index}]: Rows affected '{Rows}' != '1'. '{Sql}' {Parameters}", index, statement.Rows, statement.SQL, statement.InputParameters.Select(p => p.Value?.ToString()));
                                        validationErrors[index] = CommandErrors.InvalidRequest;
                                    }
                                }
                                else if (op.SetParent.HasValue)
                                {
                                    if (reader.Read())
                                    {
                                        responses.Add(new CompositeResponse(setParent: new SetParentResponse(reader.GetString(0), statement.Rows)));
                                    }
                                    else
                                    {
                                        Log.Error("Action[{Index}]: Rows affected '{Rows}' == '0'. '{Sql}' {Parameters}", index, statement.Rows, statement.SQL, statement.InputParameters.Select(p => p.Value?.ToString()));
                                        validationErrors[index] = CommandErrors.InvalidRequest;
                                    }
                                }
                                else if (op.Create.HasValue)
                                {
                                    if (statement.Rows > 0)
                                    {
                                        responses.Add(new CompositeResponse(create: new CreateResponse()));
                                    }
                                    else
                                    {
                                        Log.Error("Action[{Index}]: Rows affected '{Rows}' == '0'. '{Sql}' {Parameters}", index, statement.Rows, statement.SQL, statement.InputParameters.Select(p => p.Value?.ToString()));
                                        validationErrors[index] = CommandErrors.InvalidRequest;
                                    }
                                }
                                else if (op.Delete.HasValue)
                                {
                                    responses.Add(new CompositeResponse(delete: new DeleteResponse(statement.Rows)));
                                }
                                else if (op.GetItem.HasValue)
                                {
                                    if (reader.Read())
                                    {
                                        responses.Add(new CompositeResponse(getItem: new GetItemResponse(DatabaseSyncItem.FromQuery(reader))));
                                    }
                                    else
                                    {
                                        Log.Error("Action[{Index}]: Rows affected '{Rows}' == '1'. '{Sql}' {Parameters}", index, statement.Rows, statement.SQL, statement.InputParameters.Select(p => p.Value?.ToString()));
                                        validationErrors[index] = CommandErrors.InvalidRequest;
                                    }
                                }
                                else if (op.GetItems.HasValue)
                                {
                                    var parentExists = false;
                                    var children = ReadGetDatabaseSyncResponse(op.GetItems.Value, reader, ref parentExists);
                                    if (!parentExists)
                                    {
                                        Log.Error("No rows found. '{Sql}' {Parameters}", statement.SQL, statement.InputParameters.Select(p => p.Value?.ToString()));
                                        validationErrors[index] = CommandErrors.InvalidRequest;
                                    }
                                    else
                                    {
                                        responses.Add(new CompositeResponse(getItems: new GetItemsResponse(children)));
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e, nameof(BatchOperationRequest));
                                validationErrors[index] = CommandErrors.InternalError;
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(e, "{@Request}: {Sql} {Parameters}", batch, cmd.Command.CommandText, cmd.Command.Parameters.Select(p => p.Value?.ToString()));
                            validationErrors = Enumerable.Repeat(CommandErrors.InvalidRequest, validationErrors.Length).ToArray();
                        }
                    }

                    if (validationErrors.Any(a => a != CommandErrors.None))
                    {
                        connection.SendCommandFailure(commandRequestOp.RequestId, string.Join(",", validationErrors.Select(a => a.ToString("D"))));
                        transaction.Rollback();
                    }
                    else
                    {
                        transaction.Commit();
                        service.SendBatchResponse(commandRequestOp.RequestId, new BatchOperationResponse(responses));
                    }

                    if (concurrentBatchRequests > 1)
                    {
                        Log.Debug("Concurrent batch operations {Count}", concurrentBatchRequests);
                    }

                    Interlocked.Decrement(ref concurrentBatchRequests);
                }
                catch (Exception e)
                {
                    Log.Error(e, "{@Request}", nameof(DecrementRequest));
                    connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.InvalidRequest);
                }
            });
        }

        public void ProcessDatabaseSyncChanges(DatabaseSyncItem.DatabaseChangeNotification change)
        {
            var changedPaths = new HashSet<string>();

            var newProfile = GetProfileRoot(change.New.Path);
            var oldProfile = GetProfileRoot(change.Old?.Path);

            if (!string.IsNullOrEmpty(newProfile))
            {
                changedPaths.Add(change.New.Path);
            }

            if (!string.IsNullOrEmpty(oldProfile))
            {
                changedPaths.Add(change.Old?.Path);
            }

            // Work out database roundtrip time
            if (change.Old.HasValue && pendingUpdates.TryGetValue(change.Old.Value.Path, out var opStartTime))
            {
                Improbable.Postgres.Metrics.Observe(Improbable.Postgres.Metrics.RoundTripDuration, (long) (DateTime.Now - opStartTime).TotalMilliseconds);
                pendingUpdates.TryRemove(change.Old.Value.Path, out _);
            }

            if (!string.IsNullOrEmpty(newProfile) && profileToEntityId.TryGetValue(newProfile, out var newEntityId))
            {
                foreach (var kv in HydrateComponents)
                {
                    var _ = HydrateComponentAsync(newProfile, kv.Key, newEntityId);
                }
            }

            if (!string.IsNullOrEmpty(oldProfile) && profileToEntityId.TryGetValue(oldProfile, out var oldEntityId))
            {
                foreach (var kv in HydrateComponents)
                {
                    var _ = HydrateComponentAsync(oldProfile, kv.Key, oldEntityId);
                }
            }

            var update = new DatabaseSyncService.Update();
            update.AddPathsUpdatedEvent(new PathsUpdated(changedPaths));
            DatabaseSyncService.SendUpdate(connection, serviceEntityId, update, NoLoopbackParameters);
        }

        private static string GetProfileRoot(string? profileId)
        {
            if (string.IsNullOrEmpty(profileId))
            {
                return string.Empty;
            }

            var count = 0;
            var index = 0;
            for (; count < 2 && index < profileId.Length; index++)
            {
                if (profileId[index] == '.')
                {
                    count++;
                }
            }

            if (count == 1)
            {
                return profileId;
            }

            return count != 2 ? string.Empty : profileId.Substring(0, index - 1);
        }

        private bool IsRequestValid(string clientWorkerId, string requestPath)
        {
            // Admin workers always have access.
            if (adminWorkers.ContainsKey(clientWorkerId))
            {
                return true;
            }

            var profileRoot = GetProfileRoot(requestPath);

            if (!clientWorkers.TryGetValue(profileRoot, out var associatedWorkerId))
            {
                Log.Error("No client worker associated with '{Profile}'", profileRoot);
                return false;
            }

            if (associatedWorkerId == clientWorkerId)
            {
                return true;
            }

            Log.Error("Worker {WorkerId} is not associated with {Profile}", clientWorkerId, profileRoot);
            return false;

        }

        private bool CanWorkerTypeWrite(CommandRequestOp request)
        {
            var canWorkerTypeWrite = adminWorkers.ContainsKey(request.CallerWorkerId);
            if (!canWorkerTypeWrite)
            {
                Log.Error("{Types} not in {AdminWorkers}", request.CallerWorkerId, adminWorkers);
            }

            return canWorkerTypeWrite;
        }

        private async Task HydrateComponentAsync(string profileId, uint componentId, EntityId entityId, CancellationToken cancellation = default)
        {
            try
            {
                Log.Debug("Hydrating {Profile} {EntityId}...", profileId, entityId);
                var children = await service.SendGetItemsAsync(new GetItemsRequest(profileId, GetItemDepth.Recursive, connection.WorkerId), cancellation, null, new CommandParameters { AllowShortCircuit = true })
                    .ConfigureAwait(false);

                var update = HydrateComponents[componentId].Hydrate(children.Items, profileId);
                connection.SendComponentUpdate(entityId, componentId, update, NoLoopbackParameters);
            }
            catch (Exception e)
            {
                Log.Error(e, "While hydrating {EntityId} {ProfileId}", entityId, profileId);
            }
        }

        #region Command handlers

        private void HandleSetParentRequest(CommandRequestOp commandRequestOp)
        {
            var setParentRequest = SetParentRequest.Create(commandRequestOp.Request.SchemaData);

            if (!ValidateSetParentRequest(setParentRequest))
            {
                Log.Error("Invalid {@Request}", setParentRequest);
                connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.InvalidRequest);
                return;
            }

            if (!AuthorizeSetParentRequest(commandRequestOp, setParentRequest))
            {
                Log.Error("Unauthorized {@Request}", setParentRequest);
                connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.Unauthorized);

                return;
            }

            Task.Run(() =>
            {
                using var wrapper = new ConnectionWrapper(postgresOptions.ConnectionString);
                using var cmd = wrapper.Command();
                try
                {
                    AddSetParentStatement(cmd.Command, setParentRequest);
                    cmd.Command.CommandText = cmd.Command.CommandText.Replace("$TABLENAME", tableName);
                    cmd.Command.Prepare();

                    pendingUpdates.TryAdd(setParentRequest.Path, DateTime.Now);

                    var path = cmd.Command.ExecuteScalar();
                    if (path == null)
                    {
                        connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.InvalidRequest);
                        return;
                    }

                    service.SendSetParentResponse(commandRequestOp.RequestId, new SetParentResponse((string) path, cmd.Command.Statements[0].Rows));
                }
                catch (Exception e)
                {
                    Log.Error(e, "{@Request}: {Sql} {Parameters}", setParentRequest, cmd.Command.CommandText, cmd.Command.Parameters.Select(p => p.Value?.ToString()));
                    connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.InvalidRequest);
                }
            });
        }

        private static bool ValidateSetParentRequest(in SetParentRequest setParentRequest)
        {
            return ValidateBaseRequest(setParentRequest.WorkerId, setParentRequest.Path) && !string.IsNullOrEmpty(setParentRequest.NewParent);
        }

        private bool AuthorizeSetParentRequest(CommandRequestOp commandRequestOp, SetParentRequest setParentRequest)
        {
            return CanWorkerTypeWrite(commandRequestOp) &&
                   (IsRequestValid(commandRequestOp.CallerWorkerId, setParentRequest.Path) || IsRequestValid(setParentRequest.WorkerId, setParentRequest.Path)) &&
                   (IsRequestValid(commandRequestOp.CallerWorkerId, setParentRequest.NewParent) || IsRequestValid(setParentRequest.WorkerId, setParentRequest.NewParent));
        }

        private void HandleDecrementRequest(CommandRequestOp commandRequestOp)
        {
            var decrementRequest = DecrementRequest.Create(commandRequestOp.Request.SchemaData);

            if (!ValidateDecrementRequest(decrementRequest))
            {
                Log.Error("Invalid {@Request}", decrementRequest);
                connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.InvalidRequest);
                return;
            }

            if (!AuthorizeDecrementRequest(commandRequestOp, decrementRequest))
            {
                Log.Error("Unauthorized {@Request}", decrementRequest);
                connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.Unauthorized);
                return;
            }

            Task.Run(() =>
            {
                using var wrapper = new ConnectionWrapper(postgresOptions.ConnectionString);
                using var cmd = wrapper.Command();
                try
                {
                    AddDecrementStatement(cmd.Command, decrementRequest);
                    cmd.Command.CommandText = cmd.Command.CommandText.Replace("$TABLENAME", tableName);
                    cmd.Command.Prepare();

                    pendingUpdates.TryAdd(decrementRequest.Path, DateTime.Now);

                    var newValueObject = cmd.Command.ExecuteScalar();
                    if (newValueObject == null)
                    {
                        connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.InvalidRequest);
                        return;
                    }

                    var newValue = (long) newValueObject;
                    service.SendDecrementResponse(commandRequestOp.RequestId, new DecrementResponse(newValue));
                }
                catch (Exception e)
                {
                    Log.Error(e, "{@Request}: {Sql} {Parameters}", decrementRequest, cmd.Command.CommandText, cmd.Command.Parameters.Select(p => p.Value?.ToString()));
                    connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.InvalidRequest);
                }
            });
        }

        private bool AuthorizeDecrementRequest(CommandRequestOp commandRequestOp, DecrementRequest decrementRequest)
        {
            return CanWorkerTypeWrite(commandRequestOp)
                   && IsRequestValid(decrementRequest.WorkerId, decrementRequest.Path);
        }

        private void HandleIncrementRequest(CommandRequestOp commandRequestOp)
        {
            var incrementRequest = IncrementRequest.Create(commandRequestOp.Request.SchemaData);

            if (!ValidateIncrementRequest(incrementRequest))
            {
                Log.Error("Invalid {@Request}", incrementRequest);
                connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.InvalidRequest);

                return;
            }

            if (!AuthorizeIncrementRequest(commandRequestOp, incrementRequest))
            {
                Log.Error("Unauthorized {@Request}", incrementRequest);
                connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.Unauthorized);

                return;
            }

            Task.Run(() =>
            {
                using var wrapper = new ConnectionWrapper(postgresOptions.ConnectionString);
                using var cmd = wrapper.Command();
                try
                {
                    AddIncrementStatement(cmd.Command, incrementRequest);
                    cmd.Command.CommandText = cmd.Command.CommandText.Replace("$TABLENAME", tableName);
                    cmd.Command.Prepare();

                    pendingUpdates.TryAdd(incrementRequest.Path, DateTime.Now);

                    var newValueObject = cmd.Command.ExecuteScalar();
                    if (newValueObject == null)
                    {
                        connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.InvalidRequest);
                        return;
                    }

                    var newValue = (long) newValueObject;
                    service.SendIncrementResponse(commandRequestOp.RequestId, new IncrementResponse(newValue));
                }
                catch (Exception e)
                {
                    Log.Error(e, "{@Request}: {Sql} {Parameters}", incrementRequest, cmd.Command.CommandText, cmd.Command.Parameters.Select(p => p.Value?.ToString()));
                    connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.InternalError);
                }
            });
        }

        private static bool ValidateBaseRequest(string workerId, string path)
        {
            return !string.IsNullOrEmpty(workerId) && !string.IsNullOrEmpty(path);
        }

        private static bool ValidateIncrementRequest(in IncrementRequest incrementRequest)
        {
            if (ValidateBaseRequest(incrementRequest.WorkerId, incrementRequest.Path) && incrementRequest.Amount > 0)
            {
                return true;
            }

            Log.Error("Invalid: {@IncrementRequest}", incrementRequest);
            return false;

        }

        private static bool ValidateDecrementRequest(in DecrementRequest decrementRequest)
        {
            if (ValidateBaseRequest(decrementRequest.WorkerId, decrementRequest.Path) && decrementRequest.Amount > 0)
            {
                return true;
            }

            Log.Error("Invalid: {@IncrementRequest}", decrementRequest);
            return false;

        }

        private bool AuthorizeIncrementRequest(CommandRequestOp commandRequestOp, IncrementRequest incrementRequest)
        {
            return CanWorkerTypeWrite(commandRequestOp) && IsRequestValid(incrementRequest.WorkerId, incrementRequest.Path);
        }

        private void HandleGetItemsRequest(CommandRequestOp commandRequestOp)
        {
            var getDatabaseSyncRequest = GetItemsRequest.Create(commandRequestOp.Request.SchemaData);
            if (!ValidateBaseRequest(getDatabaseSyncRequest.WorkerId, getDatabaseSyncRequest.Path))
            {
                Log.Error("Invalid {@Request}", getDatabaseSyncRequest);
                connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.InvalidRequest);
                return;
            }

            if (!AuthorizeGetDatabaseSync(getDatabaseSyncRequest))
            {
                Log.Error("Unauthorized {@Request}", getDatabaseSyncRequest);
                connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.Unauthorized);

                return;
            }

            Task.Run(() =>
            {
                using var wrapper = new ConnectionWrapper(postgresOptions.ConnectionString);
                using var cmd = wrapper.Command();
                try
                {
                    AddGetDatabaseSyncStatement(cmd.Command, getDatabaseSyncRequest);

                    cmd.Command.CommandText = cmd.Command.CommandText.Replace("$TABLENAME", tableName);
                    cmd.Command.Prepare();

                    using var reader = cmd.Command.ExecuteReader();
                    var parentExists = false;
                    var children = ReadGetDatabaseSyncResponse(getDatabaseSyncRequest, reader, ref parentExists);

                    if (!parentExists)
                    {
                        var statement = cmd.Command.Statements[0];
                        Log.Error("No rows found. '{Sql}' {Parameters}", statement.SQL, statement.InputParameters.Select(p => p.Value?.ToString()));
                        connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.InvalidRequest);
                    }
                    else
                    {
                        service.SendGetItemsResponse(commandRequestOp.RequestId, new GetItemsResponse(children));
                    }
                }
                catch (Exception e)
                {
                    Log.Error(e, "{@Request}: {Sql} {Parameters}", getDatabaseSyncRequest, cmd.Command.CommandText, cmd.Command.Parameters.Select(p => p.Value?.ToString()));
                    connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.InternalError);
                }
            });
        }

        private static ImmutableArray<DatabaseSyncItem> ReadGetDatabaseSyncResponse(in GetItemsRequest getDatabaseSyncRequest, NpgsqlDataReader reader, ref bool parentExists)
        {
            var children = ImmutableArray<DatabaseSyncItem>.Empty;

            while (reader.Read())
            {
                var item = DatabaseSyncItem.FromQuery(reader);
                if (getDatabaseSyncRequest.Path == item.Path)
                {
                    // We query for the root item when to distinguish between empty items and non-existent items.
                    // Exclude the root item.
                    parentExists = true;

                    if (getDatabaseSyncRequest.Depth == GetItemDepth.ChildrenOnly)
                    {
                        continue;
                    }
                }

                children = children.Add(item);
            }

            return children;
        }

        private bool AuthorizeGetDatabaseSync(GetItemsRequest getDatabaseSyncForProfileRequest)
        {
            return IsRequestValid(getDatabaseSyncForProfileRequest.WorkerId, getDatabaseSyncForProfileRequest.Path);
        }

        private void HandleGetItemRequest(CommandRequestOp commandRequestOp)
        {
            var getItemRequest = GetItemRequest.Create(commandRequestOp.Request.SchemaData);

            if (!ValidateBaseRequest(getItemRequest.WorkerId, getItemRequest.Path))
            {
                Log.Error("Invalid {@Request}", getItemRequest);
                connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.InvalidRequest);
                return;
            }

            if (!AuthorizeGetItem(getItemRequest))
            {
                Log.Error("Unauthorized {@Request}", getItemRequest);
                connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.Unauthorized);

                return;
            }

            Task.Run(() =>
            {
                using var wrapper = new ConnectionWrapper(postgresOptions.ConnectionString);
                using var cmd = wrapper.Command($"select {DatabaseSyncItem.SelectClause} from {tableName} where path ~ @path");
                try
                {
                    cmd.Command.Parameters.AddWithValue("path", NpgsqlDbType.Unknown, getItemRequest.Path);
                    cmd.Command.CommandText = cmd.Command.CommandText.Replace("$TABLENAME", tableName);
                    cmd.Command.Prepare();

                    using var reader = cmd.Command.ExecuteReader();
                    if (reader.Read())
                    {
                        var item = DatabaseSyncItem.FromQuery(reader);
                        service.SendGetItemResponse(commandRequestOp.RequestId, new GetItemResponse(item));
                        // Nb: there can only be one matching response here.
                    }
                    else
                    {
                        connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.InvalidRequest);
                    }
                }
                catch (Exception e)
                {
                    Log.Error(e, "{@Request}: {Sql} {Parameters}", getItemRequest, cmd.Command.CommandText, cmd.Command.Parameters.Select(p => p.Value?.ToString()));
                    connection.SendCommandFailure(commandRequestOp.RequestId, CommandErrors.InternalError);
                }
            });
        }

        private bool AuthorizeGetItem(GetItemRequest getItemRequest)
        {
            return IsRequestValid(getItemRequest.WorkerId, getItemRequest.Path);
        }

        public void Dispose()
        {
            cts.Cancel();
            metricsPusher.Dispose();
        }

        #endregion

        #region Metrics

        private static void AddGaugeMetric(Metrics metrics, string key, double value)
        {
            key = $"database_sync_{key}";
            if (!metrics.GaugeMetrics.ContainsKey(key))
            {
                metrics.GaugeMetrics.Add(key, value);
            }
            else
            {
                metrics.GaugeMetrics[key] = value;
            }
        }

        public void UpdateMetrics(Metrics metrics)
        {
            foreach (var (key, value) in Improbable.Postgres.Metrics.GetCounts())
            {
                AddGaugeMetric(metrics, key, value);
            }
        }

        #endregion
    }
}
