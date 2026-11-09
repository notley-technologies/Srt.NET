# Asynchronous I/O, readiness, admission, and diagnostics

Start with the [client and server examples](../examples/README.md). The APIs below give more control over waiting, admission, and diagnostics. All snippets use `Srt.Net`; endpoint examples also use `System.Net`.

## Use asynchronous operations

Every blocking operation has an asynchronous counterpart that follows `System.Net.Sockets` conventions: `ConnectAsync`, `AcceptAsync`, `SendAsync`, `ReceiveAsync`, `SendMessageAsync`, and `ReceiveMessageAsync`. They return `ValueTask` and take a `CancellationToken`; use a timed `CancellationTokenSource` for timeouts.

```csharp
using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
await connection.ConnectAsync(endpoint, timeout.Token);
await connection.SendAsync(payload, timeout.Token);
int received = await connection.ReceiveAsync(buffer, timeout.Token);
```

How it works:

- An operation that can complete immediately completes synchronously and does not allocate. Otherwise it waits on a shared readiness engine: one background thread waits for all sockets and hands completed work to the thread pool, so thousands of idle connections cost no threads.
- Operations queue per direction and complete in the order they were started. Synchronous and asynchronous calls share the same queues, so you can mix them on one socket.
- Pass memory that stays valid and unchanged until the operation completes. Await each `ValueTask` once, and do not reuse it.
- Cancellation completes the operation with `OperationCanceledException` and leaves the socket usable. Disposing the socket completes its pending operations with `ObjectDisposedException`, including blocked synchronous calls, immediately.
- A failed or canceled `ConnectAsync` disposes the socket, like `Connect`.
- After a peer closes, live and message-mode operations fail with `SrtErrorCode.ConnectionLost`, and stream-mode receives return 0. A connection that breaks without an orderly close (the peer process died, or the network failed until the peer idle timeout) fails with `ConnectionLost` in every mode. Once libsrt has released the socket, operations fail with `InvalidSocket` after either kind of close.
- Disposing a socket with linger enabled (the file-profile default) keeps sending its queued data in the background for up to the linger time. Unlike a TCP close, process exit and the unload of an application domain or collectible `AssemblyLoadContext` discard data that is still queued, and the peer still sees an orderly close. In the live profile, linger is off and queued data is discarded, so the side that receives the last message should close first.

libsrt wakes epoll waiters through one process-wide event. Every extra thread that waits on libsrt directly, such as an application `SrtPoller.Wait`, competes for those wake-ups, so either side can see up to about 10 ms of extra latency. Prefer the asynchronous API over many polling threads.

## Use a stream

`SrtStream` adapts a connected socket to `System.IO.Stream`, like `NetworkStream`, for reliable byte transfer with any stream-based API.

```csharp
SrtSocket socket = new();
socket.SetOption(SrtOptions.TransmissionType, SrtTransmissionType.File);
await socket.ConnectAsync(endpoint, cancellationToken);

await using SrtStream stream = new(socket, ownsSocket: true);
await using FileStream file = File.OpenRead(path);
byte[] length = new byte[sizeof(long)];
BinaryPrimitives.WriteInt64LittleEndian(length, file.Length);
await stream.WriteAsync(length, cancellationToken);
await file.CopyToAsync(stream, cancellationToken);

// The receiver reads the length, then that many bytes, and answers with one byte.
await stream.ReadExactlyAsync(new byte[1], cancellationToken);
```

Use the file profile with `MessageApi` left disabled (byte-stream mode). Writes complete once libsrt has queued every byte, not when the peer has received them. Disposing the stream lets queued data drain in the background for up to the linger time, but process exit and the unload of an application domain or collectible `AssemblyLoadContext` discard data that is still queued, and the peer still sees an orderly close. Confirm delivery before the program can exit, as the acknowledgment above does. `BinaryPrimitives` is in `System.Buffers.Binary`.

Reads return zero after an orderly close by the peer. A broken connection throws `SrtException` (an `IOException`). libsrt reports both kinds of close as `InvalidSocket` once it has released the socket, which the stream treats as the end unless it already saw the break; and SRT signals an orderly close with a single control packet. So a stream that ends with zero is not proof that everything arrived: when truncation must be detected reliably, frame the data, as the length prefix above does, or add a digest. `ReadTimeout` and `WriteTimeout` apply to synchronous reads and writes.

Other modes do not behave like a byte stream. In live mode, each write must fit one message (`SrtOptions.PayloadSize`, 1316 bytes by default) and each read needs a buffer of at least the payload size (use 1456 bytes with the default MTU); otherwise the call fails with `SrtErrorCode.InvalidMessageApi`, so `CopyToAsync` and other large writes throw. In the file profile with `MessageApi` enabled, each write is one message and a read truncates a message that does not fit its buffer. Use the socket's message methods in both modes instead.

## Reuse buffers

`Send` and `SendMessage` accept `ReadOnlySpan<byte>`; `Receive` and `ReceiveMessage` accept `Span<byte>`. Their Try variants also accept spans, and the asynchronous methods accept `Memory<byte>`. The array and offset/count overloads use the same transfer implementation.

```csharp
Span<byte> buffer = stackalloc byte[1500];
int received = connection.Receive(buffer, cancellationToken: cancellationToken);

if (received > 0)
{
    connection.Send(buffer[..received], cancellationToken: cancellationToken);
}
```

The buffer is used only until the synchronous call returns. Array-backed spans, pooled arrays, stack buffers, and unmanaged spans all work. Keep large buffers on the heap or in a pool, and reuse them across calls. Synchronous calls keep a managed buffer pinned until they return, including while they wait for readiness; asynchronous calls pin their `Memory<byte>` only during each native attempt. For long or unbounded synchronous waits, use pooled or long-lived arrays, or the asynchronous methods, so that short-lived arrays are not held pinned. libsrt copies payloads into and out of its transport buffers; Span avoids additional managed buffer copies.

On .NET Standard 2.0, the span and memory types come from the `System.Memory` package. `SrtMessageResult`, `SrtPollEvent`, and `SrtSendBuffer` are readonly value types.

## Wait before accepting

`Accept`, `AcceptAsync`, and `TryAccept` check that the listener reports a queued connection before calling `srt_accept`, so waiting for clients does not make libsrt log its empty-queue error. One case remains: libsrt does not clear the listener's accept readiness when its collector discards a queued connection that broke before it was accepted. The next accept attempt then logs one Error-level `srt_accept: no pending connection available at the moment` record (to standard error unless `SrtLogging` has a handler) and keeps waiting, or `TryAccept` returns false. In a rarer race the collector closes the queued connection while an accept attempt is taking it; Srt.NET skips that connection, and libsrt logs one Error-level `srt_accept: pending connection has unexpectedly closed` record. If these records matter, filter them in your `SrtLogging` handler.

Use a poller when one loop should watch a listener together with other sockets.

Register the listener with the poller before `Listen`. A registration added after connections have arrived can miss their readiness notification, so Srt.NET rejects that order.

```csharp
using SrtSocket listener = new();
listener.Bind(new(IPAddress.Loopback, 9000));

using SrtPoller poller = new();
poller.Add(listener, SrtPollEvents.Accept);
listener.Listen();

while (true)
{
    poller.Wait(cancellationToken: cancellationToken);

    using SrtSocket connection = listener.Accept(cancellationToken: cancellationToken);
    HandleConnection(connection, cancellationToken);
}
```

Here `cancellationToken` and `HandleConnection` belong to the application. The runnable server shows the simpler asynchronous alternative: an `AcceptAsync` loop that serves each client on its own task.

## Monitor multiple sockets

One poller can monitor multiple listeners, connecting callers, and established connections. `Wait` returns `SrtPollEvent` values containing the original socket and flags. The native implementation is level-triggered: a socket remains ready until the condition is handled. `Read` also means `Accept` for listeners; `Write` also means `Connect` for connecting callers. Errors are always monitored.

For an established connection:

```csharp
poller.Add(connection, SrtPollEvents.Read);
byte[] buffer = new byte[1500];

foreach (SrtPollEvent ready in poller.Wait(TimeSpan.FromSeconds(1), cancellationToken))
{
    if ((ready.Events & SrtPollEvents.Closed) != 0)
    {
        continue;
    }

    if ((ready.Events & (SrtPollEvents.Read | SrtPollEvents.Error)) != 0)
    {
        if (ready.Socket.TryReceive(buffer, out int received))
        {
            Consume(buffer, received);
        }
    }
}
```

`Consume` belongs to the application. Native connection errors still throw `SrtException`; the application should remove and dispose failed connections. `TryReceive` and `TrySend` return false only when the operation would have to wait. Stream sends can succeed with fewer bytes than requested. `TryAccept` returns a separately owned socket, or false when no connection is queued; like `Accept`, it checks readiness before calling libsrt (see [Wait before accepting](#wait-before-accepting)). Readiness is a snapshot, so another reader can consume data before an operation runs. Keep one reader, writer, and acceptor per socket.

Use `Update` to change interests and `Remove` to unregister without closing the socket. Monitor writes only while data is waiting to be sent: connected sockets are usually writable, so continuous write monitoring can cause a busy loop. A registration does not keep its socket open: disposing a registered socket closes it immediately, and the poller reports one `Closed` event for it and removes it.

Only one `Wait` can run on each poller. Registration changes and disposal may run concurrently with it. Waits check cancellation in slices of at most 50 ms, plus thread scheduling time. Timeout or an empty registration set returns an empty array; cancellation throws `OperationCanceledException`. Dispose the poller when finished to release its native poll.

For repeated waits, allocate result storage once and use the Span overload:

```csharp
SrtPollEvent[] events = new SrtPollEvent[32];
int count = poller.Wait(events.AsSpan(), TimeSpan.FromSeconds(1), cancellationToken);

for (int index = 0; index < count; index++)
{
    SrtPollEvent ready = events[index];
    HandleReadySocket(ready, cancellationToken);
}
```

`HandleReadySocket` belongs to the application. Only the returned prefix is valid for that wait; the remaining entries are unchanged. A full destination can leave additional events for later waits. Consume readable data or update write interests before waiting again. Closed events are removed only when delivered, even with space for just one result. Result storage contains socket references, so use an array or pool rather than `stackalloc` for these events. Registration changes do not wait for the native polling call to finish. Disposing a poller interrupts the wait and waits for that native call to finish before returning and releasing its registrations. The wait remains bounded by the cancellation polling interval.

## Drive connection completion

Register a caller before starting its handshake:

```csharp
using SrtSocket caller = new();
using SrtPoller poller = new();
poller.Add(caller, SrtPollEvents.Connect);
caller.BeginConnect(new(IPAddress.Loopback, 9000));

if (poller.Wait(TimeSpan.FromSeconds(5), cancellationToken).Length == 0)
{
    throw new TimeoutException("The peer did not complete the connection in time.");
}

caller.CompleteConnect();
poller.Update(caller, SrtPollEvents.Read);
```

Call `CompleteConnect` after `Connect` or `Error` readiness. It verifies the state and reports rejection details through `SrtException.RejectionReason`. Calling it prematurely closes the unfinished attempt and throws. Dispose the caller to abandon a handshake; cancelling a poll wait alone does not close registered sockets. `Connect` remains the simpler choice when one blocking call is enough.

## Decide which connections to admit

Install a handler before `Listen`. It receives the peer address, handshake version, and UTF-8 stream identifier. It can configure the candidate socket and return a Boolean decision before the handshake finishes.

```csharp
listener.SetConnectionHandler(request =>
{
    if (!streamPassphrases.TryGetValue(request.StreamId, out string? passphrase))
    {
        request.SetRejectionReason(SrtRejectReason.Forbidden);

        return false;
    }

    request.SetOption(SrtOptions.Passphrase, passphrase);

    return true;
});
```

`streamPassphrases` is an application-supplied dictionary loaded before listening and kept unchanged while callbacks run. A stream identifier selects configuration; it is not proof of identity. Supply the matching passphrase on the caller. Do not place secrets in stream identifiers or logs.

The handler runs synchronously on a native receive thread. Keep it short: do not perform I/O, block, dispose sockets, or call back into the listener. Candidate `GetOption`, `SetOption`, and `SetRejectionReason` calls are valid only on that thread during the callback. The request cannot become a socket owned by the application; `Accept` returns that socket after admission succeeds.

Exceptions reject the candidate and are retained in `LastAdmissionException`; they never unwind into native code. Use a predefined server reason such as `SrtRejectReason.Forbidden` or `SrtRejectReason.NotFound`, or an application code from `SrtRejectReason.UserDefined` upward (for example `SrtRejectReason.UserDefined + 1`). Clear the handler by passing null before listening. Handler replacement during listening is not supported.

## Inspect traffic and message metadata

```csharp
SrtStatistics statistics = connection.GetStatistics(clear: true, instantaneous: true);
Console.WriteLine($"RTT: {statistics.RoundTripTimeMilliseconds:F1} ms");
Console.WriteLine($"Send rate: {statistics.SendRateMegabitsPerSecond:F2} Mb/s");

SrtSendBuffer pending = connection.GetSendBuffer();
Console.WriteLine($"Queued: {pending.Bytes} bytes");
```

The [statistics reference](statistics.md) lists all fields, units, and interval semantics. An empty send buffer does not prove the remote application consumed the data; use an application acknowledgment when that matters.

`SendMessage` accepts an `SrtMessageOptions` value for TTL, ordering, and source time. It is a readonly struct, so creating one per message does not allocate; its default value uses libsrt's defaults. `SendMessage` and `ReceiveMessage` return `SrtMessageResult` with the byte count, message number, packet sequence number, and source timestamp. Use them with live mode or with `MessageApi` enabled in the file profile. Native mode rules still apply; a control may have no effect in a particular transmission profile.

```csharp
SrtMessageOptions options = new()
{
    SourceTimeMicroseconds = SrtClock.NowMicroseconds
};

SrtMessageResult sent = connection.SendMessage(payload, options,
    timeout: TimeSpan.FromSeconds(5), cancellationToken: cancellationToken);
```

Zero source time lets libsrt choose the timestamp. Explicit times must use libsrt's monotonic microsecond clock, not UTC ticks. `ConnectionTimeMicroseconds` uses the same clock; it is the time libsrt opened the socket by `Bind`, or by `Connect` on an unbound socket, not the time that handshake completed, and it is zero before then. For an accepted socket it is the time the listener created the socket for the incoming connection, before `Accept` returns it. Received source timestamps can support forwarding to another SRT connection in the same process. Preserve their ordering and the native timing constraints. Group metadata is not exposed because socket bonding is outside the current API.

## Route native logs

Logging configuration applies to the whole process. No setting changes unless the application calls `SrtLogging`.

```csharp
SrtLogging.SetLevel(SrtLogLevel.Warning);
SrtLogging.SetHandler(entry => Console.Error.WriteLine(entry.Message));
```

`SetFormat`, `EnableArea`, `DisableArea`, and `SetAreas` control native formatting and functional areas. Call `SetHandler(null)` before disposing application-owned logging services to restore native output. In-flight handlers may still finish; queued messages use the handler current at delivery.

Handlers run on a managed worker outside native logging locks. The queue holds up to 1024 entries; overload drops records instead of blocking native transport threads. Inspect `DroppedMessages` and `LastHandlerException` when diagnosing logging failures. Keep handlers brief. `SrtErrors` can describe preserved native error and rejection numbers without taking over native thread-local error state.

## Collect metrics

On .NET 8 and later, Srt.NET publishes connection metrics through a `System.Diagnostics.Metrics` meter named `Srt.Net`. Nothing is recorded until a listener enables it, such as OpenTelemetry or `dotnet-counters monitor --counters Srt.Net`.

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter("Srt.Net"));
```

| Instrument | Type | Unit | Measures |
| --- | --- | --- | --- |
| `srt.connect.duration` | Histogram | `s` | Outgoing connection attempts, including the handshake (`BeginConnect` until `CompleteConnect` for polled connections) |
| `srt.open_connections` | UpDownCounter | `{connection}` | Connected sockets that have not been disposed |
| `srt.connection.duration` | Histogram | `s` | Time from connection until the socket is disposed or finalized |

Every measurement has `srt.connection.role`: `caller` or `rendezvous` for outgoing connections and `listener` for accepted ones. Failed attempts add `error.type` (the `SrtErrorCode` name, `timeout`, `canceled`, or the exception type) and, for rejected attempts, `srt.reject_reason`. The peer can choose the rejection code, so this tag has a fixed set of values: the `SrtRejectReason` name for a reason the enum defines, `UserDefined` for any application code of 2000 or more, and `_OTHER` for any other code. Read `SrtException.RejectionReason` for the exact code.

A connection whose peer has gone stays open until the socket is disposed. A connection is counted only if `srt.open_connections` was enabled when it connected, and only counted connections are subtracted when they close, as with System.Net's own connection counters. The .NET Standard 2.0 build, used on .NET Framework, has no metrics.

## Use a different libsrt build

Srt.NET looks for the native library in the package's runtime assets and then in the directory of the application or of `Srt.NET.dll`. On Windows, the rest of the search is limited to the safe DLL search directories, such as System32 and directories added with `AddDllDirectory`, so `srt.dll` is never loaded from the current directory or `PATH`. On Linux and macOS, the operating system's library search is the last fallback, as for any P/Invoke.

To use your own libsrt build on .NET, register a resolver for the Srt.NET assembly before the first Srt.NET call. `NativeLibrary` is in `System.Runtime.InteropServices`:

```csharp
NativeLibrary.SetDllImportResolver(typeof(SrtSocket).Assembly, (name, assembly, searchPath) =>
    name == "srt" ? NativeLibrary.Load(libsrtPath) : IntPtr.Zero);
```

`libsrtPath` is the full path of your library and belongs to the application. Each assembly can have only one resolver, so Srt.NET registers none of its own.

On .NET Framework, the package copies its `srt.dll` beside the application on every build and publish, and that copy is loaded first. To use your own build there, replace the file in the build and publish output, for example with a target that runs after the copy; a file replaced by hand is overwritten by the next clean build.

The build must be libsrt 1.5 or a later 1.x release, and `GetStatistics` supports only libsrt 1.5.0 through the release bundled with your Srt.NET version. Newer releases are rejected because their structure layout has not been checked; see [statistics](statistics.md).
