using System;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Net.Abstractions;
using DSharpPlus.Net.Gateway.Compression;
using DSharpPlus.Net.Serialization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DSharpPlus.Net.Gateway;

/// <inheritdoc cref="IGatewayClient"/>
public sealed class GatewayClient : IGatewayClient
{
    private ILogger logger;
    private readonly IGatewayController controller;
    private readonly ITransportService transportService;
    private readonly ChannelWriter<GatewayPayload> eventWriter;
    private readonly GatewayClientOptions options;
    private readonly ILoggerFactory factory;
    private readonly EventHandlerCollection handlers;
    private readonly string token;
    private readonly bool compress;

    private DateTimeOffset lastSentHeartbeat = DateTimeOffset.UtcNow;
    private int pendingHeartbeats;

    private int remainingOutboundPayloads = 120;
    private DateTimeOffset lastOutboundPayloadReset = DateTimeOffset.UtcNow;
    private SpinLock resetLock = new();

    private int lastReceivedSequence = 0;
    private string? resumeUrl;
    private string? sessionId;
    private string? reconnectUrl;
    private ShardInfo? shardInfo;

    private GatewayPayload? identify;
    private CancellationTokenSource gatewayTokenSource;
    private bool closureRequested = false;

    // the transport isn't thread-safe: the read loop holds this while it reads, and connecting, reconnecting,
    // resuming and disconnecting hold it while they replace the connection
    private readonly SemaphoreSlim transportLock = new(1, 1);

    // guards stopping and replacing gatewayTokenSource, whose token identifies the current connection
    private readonly object connectionLock = new();

    /// <inheritdoc/>
    public bool IsConnected { get; private set; }

    /// <inheritdoc/>
    public TimeSpan Ping { get; private set; }

    /// <inheritdoc/>
    public int ShardId => this.shardInfo?.ShardId ?? 0;

    public GatewayClient
    (
        [FromKeyedServices("DSharpPlus.Gateway.EventChannel")]
        Channel<GatewayPayload> eventChannel,
        
        ITransportService transportService,
        IOptions<TokenContainer> tokenContainer,
        IPayloadDecompressor decompressor,
        IOptions<GatewayClientOptions> options,
        IGatewayController controller,
        ILoggerFactory factory,
        IOptions<EventHandlerCollection> handlers
    )
    {
        this.transportService = transportService;
        this.eventWriter = eventChannel.Writer;
        this.factory = factory;
        this.token = tokenContainer.Value.GetToken();
        this.gatewayTokenSource = null!;
        this.compress = !decompressor.IsTransportCompression;
        this.options = options.Value;
        this.controller = controller;
        this.handlers = handlers.Value;

        this.logger = factory.CreateLogger("DSharpPlus.Net.Gateway.IGatewayClient - invalid shard");
    }

    /// <inheritdoc/>
    public async ValueTask ConnectAsync
    (
        string url,
        DiscordActivity? activity = null,
        DiscordUserStatus? status = null,
        DateTimeOffset? idleSince = null,
        ShardInfo? shardInfo = null
    )
    {
        this.closureRequested = false;

        this.logger = shardInfo is null
            ? this.factory.CreateLogger("DSharpPlus.Net.Gateway.IGatewayClient")
            : this.factory.CreateLogger($"DSharpPlus.Net.Gateway.IGatewayClient - Shard {shardInfo.ShardId}");

        this.shardInfo = shardInfo;
        this.reconnectUrl = url;

        // stops a read in progress, so a previous connection's read loop lets go of the transport
        StopConnection();

        await this.transportLock.WaitAsync();

        try
        {
            await ConnectCoreAsync(url, activity, status, idleSince, shardInfo);
        }
        finally
        {
            this.transportLock.Release();
        }
    }

    /// <summary>
    /// Connects and identifies, retrying until it succeeds or the client is disconnected. The caller holds the
    /// transport lock.
    /// </summary>
    private async Task ConnectCoreAsync
    (
        string url,
        DiscordActivity? activity,
        DiscordUserStatus? status,
        DateTimeOffset? idleSince,
        ShardInfo? shardInfo
    )
    {
        for (uint i = 0; i < this.options.MaxReconnects; i++)
        {
            CancellationToken ct = StartConnection();

            try
            {
                // a failed attempt can leave its socket open
                await this.transportService.DisconnectAsync(WebSocketCloseStatus.NormalClosure);
                await this.transportService.ConnectAsync(url, shardInfo?.ShardId);

                GatewayHello helloPayload = await ReadHelloAsync(ct);

                this.logger.LogDebug
                (
                    "Received hello event, starting heartbeating with an interval of {interval} and identifying.",
                    TimeSpan.FromMilliseconds(helloPayload.HeartbeatInterval)
                );

                StatusUpdate? statusUpdate;

                if (activity is null && status is null && idleSince is null)
                {
                    statusUpdate = null;
                }
                else
                {
                    statusUpdate = new();

                    if (activity is not null)
                    {
                        statusUpdate.Activity = new TransportActivity(activity);
                    }

                    if (status is not null)
                    {
                        statusUpdate.Status = status.Value;
                    }

                    if (idleSince is not null)
                    {
                        statusUpdate.IdleSince = idleSince.Value.ToUnixTimeMilliseconds();
                    }
                }

                GatewayIdentify inner = new()
                {
                    Token = this.token,
                    Compress = this.compress,
                    LargeThreshold = this.options.LargeThreshold,
                    ShardInfo = shardInfo,
                    Presence = statusUpdate,
                    Intents = this.options.Intents
                };

                GatewayPayload identify = new()
                {
                    OpCode = GatewayOpCode.Identify,
                    Data = inner
                };

                this.identify = identify;

                await WriteAsync(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(identify)));

                // the read loop waits for the transport lock, so it starts reading once the caller lets go of it
                _ = HeartbeatAsync(helloPayload.HeartbeatInterval, ct);
                _ = HandleEventsAsync(ct);

                this.logger.LogDebug("Identified with the Discord gateway");
                return;
            }
            catch (Exception e)
            {
                this.logger.LogError(exception: e, "Encountered an error while connecting.");

                // disconnected, or a manual reconnect took over
                if (!await TryDelayAsync(this.options.GetReconnectionDelay(i), ct))
                    return;
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisconnectAsync()
    {
        this.closureRequested = true;
        this.IsConnected = false;

        // stops a read in progress, so the read loop lets go of the transport, and ends a reconnect's retries
        StopConnection();

        await this.transportLock.WaitAsync();

        try
        {
            // a reconnect that was mid-attempt may have started a new connection meanwhile
            StopConnection();

            await this.transportService.DisconnectAsync(WebSocketCloseStatus.NormalClosure);
        }
        finally
        {
            this.transportLock.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask WriteAsync(byte[] payload)
    {
        if (DateTimeOffset.UtcNow >= this.lastOutboundPayloadReset.AddMinutes(1))
        {
            this.lastOutboundPayloadReset = DateTimeOffset.UtcNow;
            this.remainingOutboundPayloads = 120;
        }

        int remaining = Interlocked.Decrement(ref this.remainingOutboundPayloads);

        while (remaining < 0)
        {
            await Task.Delay(this.lastOutboundPayloadReset.AddMinutes(1) - DateTimeOffset.UtcNow);

            bool taken = false;
            this.resetLock.TryEnter(ref taken);

            // assume that another thread is taking care of this. wait until the lock is free to continue (this is horrible)
            if (!taken)
            {
                this.resetLock.Enter(ref taken);
                this.resetLock.Exit();
            }
            else
            {
                this.lastOutboundPayloadReset = DateTimeOffset.UtcNow;
                this.remainingOutboundPayloads = 120;
            }

            remaining = Interlocked.Decrement(ref this.remainingOutboundPayloads);
        }

        await this.transportService.WriteAsync(payload);
    }

    /// <summary>
    /// Handles dispatching heartbeats to Discord.
    /// </summary>
    private async Task HeartbeatAsync(int heartbeatInterval, CancellationToken ct)
    {
        // left over from the connection this one replaced, which may have zombied
        this.pendingHeartbeats = 0;

        double jitter = Random.Shared.NextDouble() * 0.95;

        await Task.Delay((int)(heartbeatInterval * jitter), ct);
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(heartbeatInterval));

        do
        {
            try
            {
                await WriteAsync(Encoding.UTF8.GetBytes($"{{\"op\":1,\"d\":{this.lastReceivedSequence}}}"));
                this.logger.LogTrace("Heartbeat sent with sequence number {Sequence}.", this.lastReceivedSequence);

                this.lastSentHeartbeat = DateTimeOffset.UtcNow;
                this.pendingHeartbeats++;

                if (this.pendingHeartbeats > 5)
                {
                    _ = this.controller.ZombiedAsync(this);

                    this.logger.LogWarning("Discord stopped acknowledging heartbeats, reconnecting.");

                    await TryReconnectAsync(ct);
                    return;
                }
            }
            catch (WebSocketException e)
            {
                this.logger.LogWarning("The connection died or entered an invalid state, reconnecting. Exception: {ExceptionMessage}", e.Message);

                await TryReconnectAsync(ct);
                return;
            }
            catch (Exception e)
            {
                this.logger.LogError(e, "An error occurred while sending a heartbeat.");
            }
            
        } while (await timer.WaitForNextTickAsync(ct));
    }

    /// <summary>
    /// Handles events incoming from Discord.
    /// </summary>
    private async Task HandleEventsAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TransportFrame frame;

                await this.transportLock.WaitAsync(ct);

                try
                {
                    frame = await this.transportService.ReadAsync(ct);
                }
                finally
                {
                    this.transportLock.Release();
                }

                GatewayPayload? payload = await ProcessAndDeserializeTransportFrameAsync(frame, ct);

                if (payload is null)
                {
                    continue;
                }

                this.lastReceivedSequence = payload.Sequence ?? this.lastReceivedSequence;

                switch (payload.OpCode)
                {
                    case GatewayOpCode.Dispatch when payload.EventName is "READY":

                        ReadyPayload readyPayload = ((JObject)payload.Data).ToDiscordObject<ReadyPayload>();

                        this.resumeUrl = readyPayload.ResumeGatewayUrl;
                        this.sessionId = readyPayload.SessionId;

                        payload = new ShardIdContainingGatewayPayload
                        {
                            Data = payload.Data,
                            EventName = payload.EventName,
                            OpCode = payload.OpCode,
                            Sequence = payload.Sequence,
                            ShardId = this.ShardId
                        };

                        this.IsConnected = true;

                        this.logger.LogDebug("Received READY, the gateway is now operational.");

                        break;

                    case GatewayOpCode.Dispatch when payload.EventName is "RESUMED":

                        payload = new ShardIdContainingGatewayPayload
                        {
                            Data = payload.Data,
                            EventName = payload.EventName,
                            OpCode = payload.OpCode,
                            Sequence = payload.Sequence,
                            ShardId = this.ShardId
                        };

                        this.IsConnected = true;

                        this.logger.LogDebug("A session was resumed successfully.");

                        break;

                    case GatewayOpCode.HeartbeatAck:

                        this.Ping = DateTimeOffset.UtcNow - this.lastSentHeartbeat;
                        this.pendingHeartbeats = 0;

                        // Task is not awaited to dont block gw recieve loop
                        _ = this.controller.HeartbeatedAsync(this);

                        continue;

                    case GatewayOpCode.InvalidSession:

                        this.logger.LogDebug("Received INVALID_SESSION, resumable: {Resumable}", (bool)payload.Data);
                        bool success;

                        if ((bool)payload.Data)
                            success = await TryResumeAsync(ct);
                        else
                            success = await TryReconnectAsync(ct);

                        if (!success)
                        {
                            this.logger.LogError("The session was invalidated and resuming/reconnecting failed.");
                            _ = this.controller.SessionInvalidatedAsync(this);
                        }

                        continue;

                    case GatewayOpCode.Reconnect:

                        this.logger.LogDebug("Received RECONNECT");
                        _ = this.controller.ReconnectRequestedAsync(this);

                        if (!(this.options.AutoReconnect && await TryReconnectAsync(ct)))
                        {
                            this.logger.LogError("A reconnection attempt requested by Discord failed.");
                            _ = this.controller.ReconnectFailedAsync(this);
                        }

                        continue;
                }

                if (CheckShouldBeEnqueued(payload))
                {
                    await this.eventWriter.WriteAsync(payload, CancellationToken.None);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // a reconnect or disconnect stopped this connection
        }
        catch (Exception e)
        {
            this.logger.LogError(e, "An exception occurred in event handling, reconnecting.");

            // otherwise nothing reads from the gateway again
            if (this.options.AutoReconnect)
                await TryReconnectAsync(ct);
        }

#pragma warning disable IDE0046
        bool CheckShouldBeEnqueued(GatewayPayload payload)
        {
            if (!this.options.EnableEventQueuePruning)
            {
                return true;
            }

            // similarly, if the user has an unconditional handler enabled, don't bother checking
            if (this.handlers[typeof(DiscordEventArgs)] is not [])
            {
                return true;
            }

            if (payload.OpCode != GatewayOpCode.Dispatch)
            {
                return true;
            }

            // these events are always enqueued
            if (payload.EventName is "GUILD_CREATE" or "GUILD_DELETE" or "CHANNEL_CREATE" or "CHANNEL_DELETE" or "INTERACTION_CREATE"
                or "GUILD_MEMBERS_CHUNK" or "READY" or "RESUMED")
            {
                return true;
            }

            return this.handlers[GetEventArgsType(payload.EventName)] is not [];
        }
#pragma warning restore IDE0046
    }

    /// <summary>
    /// Attempts to resume a connection, returning whether this was successful.
    /// </summary>
    /// <param name="requester">The token of the connection to resume, whose read loop and heartbeat carry on.</param>
    private async Task<bool> TryResumeAsync(CancellationToken requester)
    {
        if (this.resumeUrl is null || this.sessionId is null)
        {
            return this.options.AutoReconnect && await TryReconnectAsync(requester);
        }

        _ = this.controller.ResumeAttemptedAsync(this);

        await this.transportLock.WaitAsync();

        try
        {
            // a reconnect or disconnect already stopped this connection
            if (requester.IsCancellationRequested)
                return true;

            for (uint i = 0; i < this.options.MaxReconnects; i++)
            {
                this.logger.LogTrace("Attempting resume, attempt {Attempt}", i + 1);

                try
                {
                    this.IsConnected = false;

                    await this.transportService.DisconnectAsync(WebSocketCloseStatus.NormalClosure);
                    await this.transportService.ConnectAsync(this.resumeUrl, this.shardInfo?.ShardId);

                    await WriteAsync
                    (
                        Encoding.UTF8.GetBytes
                        (
                            JsonConvert.SerializeObject
                            (
                                new GatewayPayload
                                {
                                    OpCode = GatewayOpCode.Resume,
                                    Data = new GatewayResume
                                    {
                                        SequenceNumber = this.lastReceivedSequence,
                                        Token = this.token,
                                        SessionId = this.sessionId
                                    }
                                }
                            )
                        )
                    );

                    this.logger.LogDebug("Attempted to resume an existing gateway session.");
                    break;
                }
                catch (WebSocketException e) when (e.InnerException is HttpRequestException)
                {
                    // no internet connection, but we can still try to resume later
                    TimeSpan delay = this.options.GetReconnectionDelay(i);

                    this.logger.LogWarning("Internet connection interrupted, waiting for {Delay}", delay);

                    // a reconnect or disconnect took over
                    if (!await TryDelayAsync(delay, requester))
                        return true;

                    continue;
                }
                catch
                {
                    throw;
                }
            }

            return true;
        }
        catch (Exception e)
        {
            this.logger.LogError(e, "Failed to resume an existing gateway session.");
        }
        finally
        {
            this.transportLock.Release();
        }

        // reconnecting takes the transport lock itself, so this waits until the resume has let go of it
        return this.options.AutoReconnect && await TryReconnectAsync(requester);
    }

    /// <summary>
    /// Attempts to reconnect to the gateway, returning whether this was successful.
    /// </summary>
    /// <param name="requester">The token of the connection to replace. If a reconnect or disconnect already stopped
    /// it, there's nothing left to do.</param>
    private async Task<bool> TryReconnectAsync(CancellationToken requester)
    {
        if (!TryStopConnection(requester))
            return true;

        await this.transportLock.WaitAsync();

        try
        {
            // disconnected, or a manual reconnect replaced the connection, while this waited for the transport
            if (this.closureRequested || !IsCurrentConnection(requester))
                return true;

            return await ReconnectCoreAsync();
        }
        finally
        {
            this.transportLock.Release();
        }
    }

    /// <summary>
    /// Replaces the connection and identifies again, retrying until it succeeds. The caller holds the transport lock.
    /// </summary>
    private async Task<bool> ReconnectCoreAsync()
    {
        for (uint i = 0; i < this.options.MaxReconnects; i++)
        {
            this.logger.LogTrace("Attempting reconnect, attempt {Attempt}", i + 1);

            CancellationToken ct = StartConnection();

            try
            {
                this.IsConnected = false;

                // ensure we're disconnected no matter what the previous state was
                await this.transportService.DisconnectAsync(WebSocketCloseStatus.NormalClosure);

                await this.transportService.ConnectAsync(this.reconnectUrl!, this.shardInfo?.ShardId);

                GatewayHello helloPayload = await ReadHelloAsync(ct);

                this.logger.LogDebug
                (
                    "Received hello event, starting heartbeating with an interval of {interval} and identifying.",
                    TimeSpan.FromMilliseconds(helloPayload.HeartbeatInterval)
                );

                this.IsConnected = true;

                await WriteAsync(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(this.identify)));

                // the read loop waits for the transport lock, so it starts reading once the caller lets go of it
                _ = HeartbeatAsync(helloPayload.HeartbeatInterval, ct);
                _ = HandleEventsAsync(ct);

                this.logger.LogDebug("Identified with the Discord gateway");
                return true;
            }
            catch (Exception e)
            {
                TimeSpan delay = this.options.GetReconnectionDelay(i);

                this.logger.LogError(e, "Reconnecting failed, waiting for {Delay}", delay);

                // disconnected, or a manual reconnect took over
                if (!await TryDelayAsync(delay, ct))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads the HELLO payload that opens a connection. A failed read throws to the caller's retry loop rather than
    /// being handled like a dropped connection, which would start a reconnect inside this one.
    /// </summary>
    private async Task<GatewayHello> ReadHelloAsync(CancellationToken ct)
    {
        TransportFrame frame = await this.transportService.ReadAsync(ct);

        if (frame.TryGetException(out Exception? exception))
            throw new InvalidDataException("Failed to read the HELLO payload from Discord.", exception);

        if (frame.TryGetErrorCode(out int errorCode))
            throw new InvalidDataException($"The gateway closed with code {errorCode} instead of sending HELLO.");

        GatewayPayload? helloEvent = await ProcessAndDeserializeTransportFrameAsync(frame, ct);

        if (helloEvent is not { OpCode: GatewayOpCode.Hello })
            throw new InvalidDataException("Expected HELLO payload from Discord");

        return ((JObject)helloEvent.Data).ToDiscordObject<GatewayHello>();
    }

    /// <summary>
    /// Stops the current connection's read loop and heartbeat, and returns the token for the connection replacing it.
    /// </summary>
    private CancellationToken StartConnection()
    {
        lock (this.connectionLock)
        {
            // replaced before cancelling, so anything the cancellation runs inline already sees the new connection
            CancellationTokenSource previous = this.gatewayTokenSource;
            this.gatewayTokenSource = new();

            if (previous is not null)
                previous.Cancel();

            return this.gatewayTokenSource.Token;
        }
    }

    /// <summary>
    /// Stops the current connection's read loop, including a read in progress, and its heartbeat.
    /// </summary>
    private void StopConnection()
    {
        lock (this.connectionLock)
        {
            if (this.gatewayTokenSource is not null)
                this.gatewayTokenSource.Cancel();
        }
    }

    /// <summary>
    /// Stops the connection that <paramref name="requester"/> belongs to. Returns false when a reconnect or
    /// disconnect already stopped or replaced it, leaving nothing for the requester to do.
    /// </summary>
    private bool TryStopConnection(CancellationToken requester)
    {
        lock (this.connectionLock)
        {
            if (requester.IsCancellationRequested || !IsCurrentConnection(requester))
                return false;

            this.gatewayTokenSource.Cancel();
            return true;
        }
    }

    private bool IsCurrentConnection(CancellationToken token)
    {
        lock (this.connectionLock)
        {
            return token == this.gatewayTokenSource.Token;
        }
    }

    /// <summary>
    /// Waits before the next connection attempt. Returns false if the connection was stopped meanwhile.
    /// </summary>
    private static async Task<bool> TryDelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task HandleErrorAndAttemptToReconnectAsync(TransportFrame frame, CancellationToken ct)
    {
        if(this.closureRequested)
        {
            this.logger.LogDebug("Connection was requested to be closed, ignoring any errors.");
            return;
        }

        if (frame.TryGetException<WebSocketException>(out _))
        {
            await TryResumeAsync(ct);
        }
        else if (frame.TryGetException(out _) && this.options.AutoReconnect)
        {
            await TryReconnectAsync(ct);
        }
        else if (frame.TryGetErrorCode(out int errorCode))
        {
            this.logger.LogInformation("Received error code {Code} from gateway websocket.", errorCode);
            
            bool success = errorCode switch
            {
                < 4000 => await HandleSystemErrorAsync(errorCode, ct),
                (>= 4000 and <= 4002) or 4005 or 4008 => await TryResumeAsync(ct),
                4003 or 4007 or 4009 => this.options.AutoReconnect && await TryReconnectAsync(ct),
                4004 or (>= 4010 and <= 4014) => false,
                _ => this.options.AutoReconnect && await TryReconnectAsync(ct)
            };

            if (!success)
            {
                this.logger.LogError("An attempt to reconnect upon error code {Code} failed.", errorCode);
                _ = this.controller.ReconnectFailedAsync(this);
            }
        }
    }

    private async ValueTask<GatewayPayload?> ProcessAndDeserializeTransportFrameAsync(TransportFrame frame, CancellationToken ct)
    {
        GatewayPayload? payload;

        if (!frame.IsSuccess)
        {
            await HandleErrorAndAttemptToReconnectAsync(frame, ct);
            return null;
        }

        if (frame.TryGetMessage(out string? stringMessage))
        {
            payload = JsonConvert.DeserializeObject<GatewayPayload>(stringMessage);

            if (payload is null)
            {
                this.logger.LogError("Received invalid inbound event: {Data}", stringMessage);
                return null;
            }
        }
        else if (frame.TryGetStreamMessage(out MemoryStream? streamMessage))
        {
            byte[] bytes = streamMessage.ToArray();
            var utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
            
            string utf8ValidatedString = null;
            try
            {
                utf8ValidatedString = utf8.GetString(bytes);
            }
            catch (DecoderFallbackException ex)
            {
                this.logger.LogError("Received invalid inbound event: {Data}", Encoding.UTF8.GetString(streamMessage.ToArray()));
                return null;
            }

            //using StreamReader reader = new(streamMessage);
            using StringReader reader = new StringReader(utf8ValidatedString);
            using JsonReader jsonReader = new JsonTextReader(reader);
            {
                JsonSerializer serializer = new();
                payload = serializer.Deserialize<GatewayPayload>(jsonReader);

                if (payload is null)
                {
                    this.logger.LogError
                    (
                        "Received invalid inbound event: {Data}",
                        Encoding.UTF8.GetString(streamMessage.ToArray())
                    );

                    return null;
                }
            }
        }
        else
        {
            this.logger.LogCritical("Unrecognized transport frame encountered: {Frame}", frame);
            return null;
        }

        return payload;
    }

    private async Task<bool> HandleSystemErrorAsync(int errorCode, CancellationToken ct)
    {
        // 3000 Unauthorized and 3003 Forbidden are a problem we can't fix, error and return to the user
        if (errorCode is 3000 or 3003)
        {
            this.logger.LogError("Encountered irrecoverable gateway close code {CloseCode}", errorCode);
        }

        // else, try to reconnect if so requested
        if (this.options.AutoReconnect && !this.closureRequested)
        {
            return await TryReconnectAsync(ct);
        }

        this.logger.LogDebug("Gateway shutdown in progress, not reconnecting on recoverable close code {CloseCode}", errorCode);
        return false;
    }

    /// <inheritdoc/>
    public async ValueTask ReconnectAsync()
    {
        this.closureRequested = false; // Manual reconnect, so we're not closing

        // stops a read in progress, so the read loop lets go of the transport, and ends a reconnect's retries
        StopConnection();

        await this.transportLock.WaitAsync();

        try
        {
            _ = await ReconnectCoreAsync();
        }
        finally
        {
            this.transportLock.Release();
        }
    }

    private Type GetEventArgsType(string eventName)
    {
        // since we have nothing to sync Dispatch.cs and this, the event queue pruning option may... not work optimally
        return eventName switch
        {
            "APPLICATION_COMMAND_PERMISSIONS_UPDATE" => typeof(ApplicationCommandPermissionsUpdatedEventArgs),
            "AUTO_MODERATION_RULE_CREATE" => typeof(AutoModerationRuleCreatedEventArgs),
            "AUTO_MODERATION_RULE_UPDATE" => typeof(AutoModerationRuleUpdatedEventArgs),
            "AUTO_MODERATION_RULE_DELETE" => typeof(AutoModerationRuleDeletedEventArgs),
            "AUTO_MODERATION_ACTION_EXECUTION" => typeof(AutoModerationRuleExecutedEventArgs),
            "CHANNEL_CREATE" => typeof(ChannelCreatedEventArgs),
            "CHANNEL_UPDATE" => typeof(ChannelUpdatedEventArgs),
            "CHANNEL_DELETE" => typeof(ChannelDeletedEventArgs),
            "CHANNEL_PINS_UPDATE" => typeof(ChannelPinsUpdatedEventArgs),
            "THREAD_CREATE" => typeof(ThreadCreatedEventArgs),
            "THREAD_UPDATE" => typeof(ThreadUpdatedEventArgs),
            "THREAD_DELETE" => typeof(ThreadDeletedEventArgs),
            "THREAD_LIST_SYNC" => typeof(ThreadListSyncedEventArgs),
            "THREAD_MEMBER_UPDATE" => typeof(ThreadMemberUpdatedEventArgs),
            "THREAD_MEMBERS_UPDATE" => typeof(ThreadMembersUpdatedEventArgs),
            "ENTITLEMENT_CREATE" => typeof(EntitlementCreatedEventArgs),
            "ENTITLEMENT_UPDATE" => typeof(EntitlementUpdatedEventArgs),
            "ENTITLEMENT_DELETE" => typeof(EntitlementDeletedEventArgs),
            "GUILD_CREATE" => typeof(GuildCreatedEventArgs),
            "GUILD_UPDATE" => typeof(GuildUpdatedEventArgs),
            "GUILD_DELETE" => typeof(GuildDeletedEventArgs),
            "GUILD_AUDIT_LOG_ENTRY_CREATE" => typeof(GuildAuditLogCreatedEventArgs),
            "GUILD_BAN_ADD" => typeof(GuildBanAddedEventArgs),
            "GUILD_BAN_REMOVE" => typeof(GuildBanRemovedEventArgs),
            "GUILD_EMOJIS_UPDATE" => typeof(GuildEmojisUpdatedEventArgs),
            "GUILD_STICKERS_UPDATE" => typeof(GuildStickersUpdatedEventArgs),
            "GUILD_INTEGRATIONS_UPDATE" => typeof(GuildIntegrationsUpdatedEventArgs),
            "GUILD_MEMBER_ADD" => typeof(GuildMemberAddedEventArgs),
            "GUILD_MEMBER_REMOVE" => typeof(GuildMemberRemovedEventArgs),
            "GUILD_MEMBER_UPDATE" => typeof(GuildMemberUpdatedEventArgs),
            "GUILD_MEMBERS_CHUNK" => typeof(GuildMembersChunkedEventArgs),
            "GUILD_ROLE_CREATE" => typeof(GuildRoleCreatedEventArgs),
            "GUILD_ROLE_UPDATE" => typeof(GuildRoleUpdatedEventArgs),
            "GUILD_ROLE_DELETE" => typeof(GuildRoleDeletedEventArgs),
            "GUILD_SCHEDULED_EVENT_CREATE" => typeof(ScheduledGuildEventCreatedEventArgs),
            "GUILD_SCHEDULED_EVENT_UPDATE" => typeof(ScheduledGuildEventUpdatedEventArgs),
            "GUILD_SCHEDULED_EVENT_DELETE" => typeof(ScheduledGuildEventDeletedEventArgs),
            "GUILD_SCHEDULED_EVENT_USER_ADD" => typeof(ScheduledGuildEventUserAddedEventArgs),
            "GUILD_SCHEDULED_EVENT_USER_REMOVE" => typeof(ScheduledGuildEventUserRemovedEventArgs),
            "INTEGRATION_CREATE" => typeof(IntegrationCreatedEventArgs),
            "INTEGRATION_UPDATE" => typeof(IntegrationUpdatedEventArgs),
            "INTEGRATION_DELETE" => typeof(IntegrationDeletedEventArgs),
            "INTERACTION_CREATE" => typeof(InteractionCreatedEventArgs),
            "INVITE_CREATE" => typeof(InviteCreatedEventArgs),
            "INVITE_DELETE" => typeof(InviteDeletedEventArgs),
            "MESSAGE_CREATE" => typeof(MessageCreatedEventArgs),
            "MESSAGE_UPDATE" => typeof(MessageUpdatedEventArgs),
            "MESSAGE_DELETE" => typeof(MessageDeletedEventArgs),
            "MESSAGE_DELETE_BULK" => typeof(MessagesBulkDeletedEventArgs),
            "MESSAGE_REACTION_ADD" => typeof(MessageReactionAddedEventArgs),
            "MESSAGE_REACTION_REMOVE" => typeof(MessageReactionRemovedEventArgs),
            "MESSAGE_REACTION_REMOVE_ALL" => typeof(MessageReactionsClearedEventArgs),
            "MESSAGE_REACTION_REMOVE_EMOJI" => typeof(MessageReactionRemovedEmojiEventArgs),
            "PRESENCE_UPDATE" => typeof(PresenceUpdatedEventArgs),
            "STAGE_INSTANCE_CREATE" => typeof(StageInstanceCreatedEventArgs),
            "STAGE_INSTANCE_UPDATE" => typeof(StageInstanceUpdatedEventArgs),
            "STAGE_INSTANCE_DELETE" => typeof(StageInstanceDeletedEventArgs),
            "TYPING_START" => typeof(TypingStartedEventArgs),
            "USER_UPDATE" => typeof(UserUpdatedEventArgs),
            "VOICE_STATE_UPDATE" => typeof(VoiceStateUpdatedEventArgs),
            "VOICE_SERVER_UPDATE" => typeof(VoiceServerUpdatedEventArgs),
            "WEBHOOKS_UPDATE" => typeof(WebhooksUpdatedEventArgs),
            "MESSAGE_POLL_VOTE_ADD" => typeof(MessagePollVotedEventArgs),
            "MESSAGE_POLL_VOTE_REMOVE" => typeof(MessagePollVotedEventArgs),
            _ => typeof(UnknownEventArgs)
        };
    }
}
