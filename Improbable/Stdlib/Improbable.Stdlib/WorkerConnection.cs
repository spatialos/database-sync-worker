using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Improbable.Worker.CInterop;
using Improbable.Worker.CInterop.Alpha;
using Improbable.Worker.CInterop.Query;
using Locator = Improbable.Worker.CInterop.Alpha.Locator;
using LocatorParameters = Improbable.Worker.CInterop.Alpha.LocatorParameters;

namespace Improbable.Stdlib
{
    public class WorkerConnection : IDisposable
    {
        private static readonly OpList EmptyOpList = new OpList();
        private readonly ConcurrentDictionary<uint, TaskHandler> requestsToComplete = new ConcurrentDictionary<uint, TaskHandler>();
        private Connection connection;
        private Task metricsTask;
        private CancellationTokenSource metricsTcs = new CancellationTokenSource();
        private string workerId;

        private WorkerConnection(Connection connection)
        {
            this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        public string WorkerId
        {
            get
            {
                if (string.IsNullOrEmpty(workerId))
                {
                    // ReSharper disable once InconsistentlySynchronizedField
                    workerId = connection.GetWorkerId();
                }

                return workerId;
            }
        }

        public void Dispose()
        {
            CancelCommands();
            StopSendingMetrics();

            connection?.Dispose();
            connection = null;
        }

        public static Task<WorkerConnection> ConnectAsync(IWorkerOptions workerOptions, ConnectionParameters connectionParameters, TaskCreationOptions taskOptions = TaskCreationOptions.None)
        {
            switch (workerOptions)
            {
                case IReceptionistOptions receptionistOptions:
                    var workerName = workerOptions.WorkerName ?? $"{connectionParameters.WorkerType}-{Guid.NewGuid().ToString()}";
                    return ConnectAsync(receptionistOptions.SpatialOsHost, receptionistOptions.SpatialOsPort, workerName, connectionParameters, taskOptions);

                case ILocatorOptions locatorOptions:
                    connectionParameters.Network.UseExternalIp = true;
                    return ConnectAsync(locatorOptions.SpatialOsHost, locatorOptions.SpatialOsPort, connectionParameters, locatorOptions.Token, "Player", "", connectionParameters.WorkerType, taskOptions);

                default:
                    throw new NotImplementedException("Unrecognized option type: " + workerOptions.GetType());
            }
        }

        public static Task<WorkerConnection> ConnectAsync(string host, ushort port, string workerName, ConnectionParameters connectionParameters, TaskCreationOptions taskOptions = TaskCreationOptions.None)
        {
            var tcs = new TaskCompletionSource<WorkerConnection>(taskOptions);

            Task.Factory.StartNew(() =>
            {
                try
                {
                    using (var future = Connection.ConnectAsync(host, port, workerName, connectionParameters))
                    {
                        var connection = future.Get();

                        if (connection.GetConnectionStatusCode() != ConnectionStatusCode.Success)
                        {
                            tcs.SetException(new Exception($"{connection.GetConnectionStatusCode()}: {connection.GetConnectionStatusCodeDetailString()}"));
                        }
                        else
                        {
                            tcs.SetResult(new WorkerConnection(connection));
                        }
                    }
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            }, taskOptions);

            return tcs.Task;
        }

        public static Task<WorkerConnection> ConnectAsync(string host, ushort port, ConnectionParameters connectionParameters, string authToken, string playerId, string displayName, string workerType, TaskCreationOptions taskOptions = TaskCreationOptions.None)
        {
            var tcs = new TaskCompletionSource<WorkerConnection>(taskOptions);

            Task.Factory.StartNew(() =>
            {
                try
                {
                    var pit = GetDevelopmentPlayerIdentityToken(host, port, authToken, playerId, displayName);
                    var loginTokens = GetDevelopmentLoginTokens(host, port, workerType, pit);
                    var loginToken = loginTokens.First().LoginToken;

                    var locatorParameters = new LocatorParameters
                    {
                        PlayerIdentity = new PlayerIdentityCredentials
                        {
                            LoginToken = loginToken,
                            PlayerIdentityToken = pit
                        },
                        UseInsecureConnection = false
                    };

                    using (var locator = new Locator(host, locatorParameters))
                    using (var future = locator.ConnectAsync(connectionParameters))
                    {
                        var connection = future.Get();

                        if (connection.GetConnectionStatusCode() != ConnectionStatusCode.Success)
                        {
                            tcs.SetException(new Exception($"{connection.GetConnectionStatusCode()}: {connection.GetConnectionStatusCodeDetailString()}"));
                        }
                        else
                        {
                            tcs.SetResult(new WorkerConnection(connection));
                        }
                    }
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            }, taskOptions);

            return tcs.Task;
        }

        public void StartSendingMetrics(params Action<Metrics>[] updaterList)
        {
            if (metricsTask != null)
            {
                throw new InvalidOperationException("Metrics are already being sent");
            }

            metricsTask = Task.Factory.StartNew(async _ =>
            {
                var metrics = new Metrics();

                while (!metricsTcs.Token.IsCancellationRequested)
                {
                    foreach (var updater in updaterList)
                    {
                        updater.Invoke(metrics);
                    }

                    metrics.Load = await GetCpuUsageForProcess(metricsTcs.Token) / 100.0;

                    lock (connection)
                    {
                        connection.SendMetrics(metrics);
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(5000), metricsTcs.Token);
                }
            }, metricsTcs.Token, TaskCreationOptions.LongRunning);
        }

        public void StopSendingMetrics()
        {
            metricsTcs?.Cancel();
            metricsTcs?.Dispose();

            metricsTask?.Wait();

            metricsTask = null;
            metricsTcs = null;
        }

        private static string GetDevelopmentPlayerIdentityToken(string host, ushort port, string authToken, string playerId, string displayName)
        {
            var pit = DevelopmentAuthentication.CreateDevelopmentPlayerIdentityTokenAsync(
                host, port,
                new PlayerIdentityTokenRequest
                {
                    DevelopmentAuthenticationToken = authToken,
                    PlayerId = playerId,
                    DisplayName = displayName
                }).Get();

            if (pit.Value.Status.Code != ConnectionStatusCode.Success)
            {
                throw new AuthenticationException("Error received while retrieving a Player Identity Token: " + $"{pit.Value.Status.Detail}");
            }

            return pit.Value.PlayerIdentityToken;
        }

        private static List<LoginTokenDetails> GetDevelopmentLoginTokens(string host, ushort port, string workerType, string pit)
        {
            var tokens = DevelopmentAuthentication.CreateDevelopmentLoginTokensAsync(host, port,
                new LoginTokensRequest
                {
                    PlayerIdentityToken = pit,
                    WorkerType = workerType
                }).Get();

            if (tokens.Value.Status.Code != ConnectionStatusCode.Success)
            {
                throw new AuthenticationException("Error received while retrieving Login Tokens: " + $"{tokens.Value.Status.Detail}");
            }

            if (tokens.Value.LoginTokens.Count == 0)
            {
                throw new Exception("No deployment returned for this project.");
            }

            return tokens.Value.LoginTokens;
        }

        public void ProcessOpList(OpList opList)
        {
            foreach (var op in opList.Ops)
            {
                switch (op.OpType)
                {
                    case OpType.ReserveEntityIdsResponse:
                        CompleteCommand(op.ReserveEntityIdsResponseOp);
                        break;
                    case OpType.CreateEntityResponse:
                        CompleteCommand(op.CreateEntityResponseOp);
                        break;
                    case OpType.DeleteEntityResponse:
                        CompleteCommand(op.DeleteEntityResponseOp);
                        break;
                    case OpType.EntityQueryResponse:
                        CompleteCommand(op.EntityQueryResponseOp);
                        break;
                    case OpType.CommandResponse:
                        CompleteCommand(op.CommandResponseOp);
                        break;
                }
            }
        }

        public void Send(EntityId entityId, SchemaCommandRequest request, uint? timeout, CommandParameters? parameters, Action<CommandResponses> complete, Action<StatusCode, string> fail, Action cancel)
        {
            uint requestId;
            lock (connection)
            {
                requestId = connection.SendCommandRequest(entityId.Value, new CommandRequest(request), 1, timeout, parameters);
            }

            if (!requestsToComplete.TryAdd(requestId, new TaskHandler {Cancel = cancel, Complete = complete, Fail = fail}))
            {
                throw new InvalidOperationException("Key already exists");
            }
        }

        private void CancelCommands()
        {
            while (!requestsToComplete.IsEmpty)
            {
                var keys = requestsToComplete.Keys.ToList();
                foreach (var k in keys)
                {
                    if (requestsToComplete.TryRemove(k, out var request))
                    {
                        request.Cancel();
                    }
                }
            }
        }

        public Task<ReserveEntityIdsResult> SendReserveEntityIdsRequest(uint numberOfEntityIds, uint? timeoutMillis = null)
        {
            lock (connection)
            {
                return RecordTask(connection.SendReserveEntityIdsRequest(numberOfEntityIds, timeoutMillis), responses => new ReserveEntityIdsResult
                {
                    FirstEntityId = responses.ReserveEntityIds.FirstEntityId,
                    NumberOfEntityIds = responses.ReserveEntityIds.NumberOfEntityIds
                });
            }
        }

        public Task<EntityId?> SendCreateEntityRequest(Entity entity, EntityId? entityId = null, uint? timeoutMillis = null)
        {
            lock (connection)
            {
                return RecordTask(connection.SendCreateEntityRequest(entity, entityId?.Value, timeoutMillis), responses => responses.CreateEntity.EntityId.HasValue ? new EntityId(responses.CreateEntity.EntityId.Value) : (EntityId?) null);
            }
        }

        public Task<EntityId> SendDeleteEntityRequest(EntityId entityId, uint? timeoutMillis = null)
        {
            lock (connection)
            {
                return RecordTask(connection.SendDeleteEntityRequest(entityId.Value, timeoutMillis), responses => new EntityId(responses.DeleteEntity.EntityId));
            }
        }

        public Task<EntityQueryResult> SendEntityQueryRequest(EntityQuery entityQuery, uint? timeoutMillis = null)
        {
            lock (connection)
            {
                return RecordTask(connection.SendEntityQueryRequest(entityQuery, timeoutMillis), responses => new EntityQueryResult
                {
                    Results = responses.EntityQuery.Result.ToDictionary(kv => new EntityId(kv.Key), kv => kv.Value.DeepCopy()),
                    ResultCount = responses.EntityQuery.ResultCount
                });
            }
        }

        private Task<TResultType> RecordTask<TResultType>(uint id, Func<CommandResponses, TResultType> getResult)
        {
            var completion = new TaskCompletionSource<TResultType>(TaskCreationOptions.RunContinuationsAsynchronously);

            var cancellation = new CancellationTokenSource();
            cancellation.Token.Register(() => completion.TrySetCanceled(cancellation.Token));

            void Complete(CommandResponses r)
            {
                completion.TrySetResult(getResult(r));
            }

            void Fail(StatusCode code, string message)
            {
                completion.TrySetException(new Exception(message));
            }

            void Cancel()
            {
                cancellation.Cancel();
                cancellation.Dispose();
            }

            if (!requestsToComplete.TryAdd(id, new TaskHandler {Cancel = Cancel, Complete = Complete, Fail = Fail}))
            {
                throw new InvalidOperationException("Key already exists");
            }

            return completion.Task;
        }

        private void CompleteCommand(ReserveEntityIdsResponseOp r)
        {
            if (requestsToComplete.TryRemove(r.RequestId, out var completer))
            {
                switch (r.StatusCode)
                {
                    case StatusCode.Success:
                        completer.Complete(new CommandResponses {ReserveEntityIds = r});
                        break;
                    default:
                        completer.Fail(r.StatusCode, r.Message);
                        break;
                }
            }
        }

        private void CompleteCommand(EntityQueryResponseOp r)
        {
            if (requestsToComplete.TryRemove(r.RequestId, out var completer))
            {
                switch (r.StatusCode)
                {
                    case StatusCode.Success:
                        completer.Complete(new CommandResponses {EntityQuery = r});
                        break;
                    default:
                        completer.Fail(r.StatusCode, r.Message);
                        break;
                }
            }
        }

        private void CompleteCommand(CommandResponseOp r)
        {
            if (requestsToComplete.TryRemove(r.RequestId, out var completer))
            {
                switch (r.StatusCode)
                {
                    case StatusCode.Success:
                        if (!r.Response.SchemaData.HasValue)
                        {
                            throw new ArgumentNullException();
                        }

                        completer.Complete(new CommandResponses {UserCommand = r});
                        break;
                    default:
                        completer.Fail(r.StatusCode, r.Message);
                        break;
                }
            }
        }

        private void CompleteCommand(CreateEntityResponseOp r)
        {
            if (requestsToComplete.TryRemove(r.RequestId, out var completer))
            {
                switch (r.StatusCode)
                {
                    case StatusCode.Success:
                        completer.Complete(new CommandResponses {CreateEntity = r});
                        break;
                    default:
                        completer.Fail(r.StatusCode, r.Message);
                        break;
                }
            }
        }

        private void CompleteCommand(DeleteEntityResponseOp r)
        {
            if (requestsToComplete.TryRemove(r.RequestId, out var completer))
            {
                switch (r.StatusCode)
                {
                    case StatusCode.Success:
                        completer.Complete(new CommandResponses {DeleteEntity = r});
                        break;
                    default:
                        completer.Fail(r.StatusCode, r.Message);
                        break;
                }
            }
        }

        public void SendCommandResponse(uint id, SchemaCommandResponse response)
        {
            lock (connection)
            {
                connection.SendCommandResponse(id, new CommandResponse(response));
            }
        }

        public void SendCommandFailure(uint requestId, string message)
        {
            lock (connection)
            {
                connection.SendCommandFailure(requestId, message);
            }
        }

        public void SendComponentUpdate(EntityId entityId, SchemaComponentUpdate update, UpdateParameters? updateParameters = null)
        {
            lock (connection)
            {
                connection.SendComponentUpdate(entityId.Value, new ComponentUpdate(update), updateParameters);
            }
        }

        public void SendMetrics(Metrics metrics)
        {
            lock (connection)
            {
                connection.SendMetrics(metrics);
            }
        }

        public ConnectionStatusCode GetConnectionStatusCode()
        {
            // ReSharper disable once InconsistentlySynchronizedField
            return connection?.GetConnectionStatusCode() ?? ConnectionStatusCode.Cancelled;
        }

        /// <summary>
        ///     Returns an OpList
        /// </summary>
        /// <param name="timeout">
        ///     An empty OpList will be returned after the specified duration. Use <see cref="TimeSpan.Zero" />
        ///     to block.
        /// </param>
        public OpList GetOpList(TimeSpan timeout)
        {
            if (connection == null || GetConnectionStatusCode() != ConnectionStatusCode.Success)
            {
                return EmptyOpList;
            }

            lock (connection)
            {
                return new OpList(connection.GetOpList((uint) timeout.TotalMilliseconds));
            }
        }

        /// <summary>
        ///     Returns OpLists for as long as connected to SpatialOS.
        /// </summary>
        /// <param name="timeout">
        ///     An empty OpList will be returned after the specified duration. Use <see cref="TimeSpan.Zero" />
        ///     to block.
        /// </param>
        public IEnumerable<OpList> GetOpLists(TimeSpan timeout)
        {
            return GetOpLists(timeout, CancellationToken.None);
        }

        /// <summary>
        ///     Returns OpLists for as long as connected to SpatialOS.
        /// </summary>
        /// <param name="timeout">
        ///     An empty OpList will be returned after the specified duration. Use <see cref="TimeSpan.Zero" />
        ///     to block until new ops are available.
        /// </param>
        /// <param name="token">Cancellation token.</param>
        public IEnumerable<OpList> GetOpLists(TimeSpan timeout, CancellationToken token)
        {
            while (!token.IsCancellationRequested && connection != null && GetConnectionStatusCode() == ConnectionStatusCode.Success)
            {
                OpList opList = null;

                try
                {
                    lock (connection)
                    {
                        opList = new OpList(connection.GetOpList((uint) timeout.TotalMilliseconds));
                    }

                    yield return opList;
                }
                finally
                {
                    opList?.Dispose();
                }
            }
        }

        public string GetWorkerFlag(string flagName)
        {
            lock (connection)
            {
                return connection.GetWorkerFlag(flagName);
            }
        }

        public IEnumerable<KeyValuePair<EntityId, Entity>> FindServiceEntities(uint componentId)
        {
            return FindServiceEntities(componentId, CancellationToken.None);
        }

        public IEnumerable<KeyValuePair<EntityId, Entity>> FindServiceEntities(uint componentId, CancellationToken token)
        {
            var serviceQuery = SendEntityQueryRequest(new EntityQuery {Constraint = new ComponentConstraint(componentId), ResultType = new SnapshotResultType()});
            foreach (var opList in GetOpLists(TimeSpan.FromMilliseconds(16), token))
            {
                ProcessOpList(opList);

                if (serviceQuery.IsCompleted)
                {
                    break;
                }
            }

            return serviceQuery.Result.Results;
        }

        private static async Task<double> GetCpuUsageForProcess(CancellationToken cancellationToken)
        {
            var startTime = DateTime.UtcNow;
            var startCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
            await Task.Delay(500, cancellationToken);

            var endTime = DateTime.UtcNow;
            var endCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalMsPassed = (endTime - startTime).TotalMilliseconds;
            var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
            return cpuUsageTotal * 100;
        }

        public struct ReserveEntityIdsResult
        {
            public EntityId? FirstEntityId;
            public int NumberOfEntityIds;
        }

        public struct EntityQueryResult : IDisposable
        {
            public int ResultCount;
            public Dictionary<EntityId, Entity> Results;

            public void Dispose()
            {
                foreach (var pair in Results)
                {
                    pair.Value.Free();
                }
            }
        }

        public struct CommandResponses
        {
            public CreateEntityResponseOp CreateEntity;
            public ReserveEntityIdsResponseOp ReserveEntityIds;
            public DeleteEntityResponseOp DeleteEntity;
            public EntityQueryResponseOp EntityQuery;
            public CommandResponseOp UserCommand;
        }

        private class TaskHandler
        {
            public Action Cancel;
            public Action<CommandResponses> Complete;
            public Action<StatusCode, string> Fail;
        }
    }
}
