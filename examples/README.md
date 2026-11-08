# Client and server examples

These console applications are a starting point for Srt.NET. Each client sends one UTF-8 message to a server on the same computer, waits for the echo, and exits. Each server echoes messages from any number of clients at once until you press Ctrl+C. The same pair is written twice, once with the asynchronous API and once with the synchronous one:

| API | Client | Server |
| --- | --- | --- |
| Asynchronous | [Srt.NET.Client](Srt.NET.Client/Program.cs): `ConnectAsync`, `SendAsync`, and `ReceiveAsync` under one timeout | [Srt.NET.Server](Srt.NET.Server/Program.cs): an `AcceptAsync` loop that serves each client on its own task |
| Synchronous | [Srt.NET.SyncClient](Srt.NET.SyncClient/Program.cs): `Connect`, `Send`, and `Receive`, each with a timeout | [Srt.NET.SyncServer](Srt.NET.SyncServer/Program.cs): an `Accept` loop that serves each client on its own thread |

All four use the same port and protocol, so any client works with either server.

## Build the native library

Use the .NET SDK selected by the root `global.json` and install the .NET 8 runtime. Build libsrt once for your platform using the [contributing guide](../CONTRIBUTING.md). On Linux, the build runs in a pinned container, so only Docker is required. For example, from the repository root on Linux x64:

```sh
bash native/docker/build-native.sh linux-x64
```

Supported runtime identifiers are `win-x64`, `linux-x64`, `linux-arm64`, `linux-musl-x64`, `linux-musl-arm64`, `osx-x64`, and `osx-arm64`. The examples pick the identifier that matches the machine they are built on.

The examples reference the library project directly and copy the native files from `artifacts/runtimes/<rid>/native/` into their output. A missing native build produces an error with the expected path. No NuGet release is needed to run these examples from the checkout.

## Run in two terminals

Run these commands from the repository root. Start the server first:

```sh
dotnet run --project examples/Srt.NET.Server
```

Wait for `Listening on 127.0.0.1:9000`, then start the client in another terminal:

```sh
dotnet run --project examples/Srt.NET.Client
```

For the synchronous pair, run `examples/Srt.NET.SyncServer` and `examples/Srt.NET.SyncClient` the same way. Only one server can listen on port 9000 at a time.

The server prints one line when the client connects, one for each message, and one when the client disconnects. `<port>` is the client's UDP port, which changes on every run:

```text
Client 127.0.0.1:<port> connected.
Received from 127.0.0.1:<port>: Hello from Srt.NET!
Client 127.0.0.1:<port> disconnected.
```

The client prints:

```text
Sent: Hello from Srt.NET!
Echo: Hello from Srt.NET!
```

Run the client as many times as you like, including several at once. The accept loop hands each connection to its own task and immediately waits for the next one, so a slow or idle client never blocks the others. There is no idle receive timeout. When a client closes or loses its connection, the server prints that it disconnected; any other connection error prints `Connection to 127.0.0.1:<port> ended:` and the error.

Press Ctrl+C in the server terminal to stop it gracefully. The handler cancels one token passed to `AcceptAsync`, `ReceiveAsync`, and `SendAsync`, so it ends the accept loop and every client loop at once. The server waits for those loops, disposes the sockets, and removes the console handler before it exits. The synchronous server does the same with the blocking `Accept`, `Receive`, and `Send` overloads that take a `CancellationToken`: cancellation unblocks the waiting threads, and the server joins each client thread before it exits.

## Choose between the two styles

The synchronous server is the simplest to follow, but it dedicates one thread to each connected client, which suits a few dozen connections. The asynchronous server holds no thread while a client is idle, so it scales to many connections. Both styles wait on the same readiness engine and can be mixed, even on one socket.

## Adapt the examples

Change the message in the client to send your own data. SRT transports bytes; UTF-8 encoding is just how these examples represent text. Both applications select the `File` transmission profile for reliable data transfer and enable `MessageApi` to preserve each message's boundary. The profile name does not mean that the applications read or write files.

Keep each message within the 1500-byte receive buffer in this example, or increase the buffers in both programs. The file profile also enables linger, so a closed socket keeps sending its queued data in the background for up to 180 seconds. The library's default live profile is intended for timed media traffic.

Both applications bind or connect to loopback on port 9000. To use two computers, bind the server to `IPAddress.Any`, connect the client to the server's IP address, and allow UDP port 9000 through the server's firewall. Change the port in both programs if 9000 is already in use.

These examples use the default unencrypted connection. To enable encryption, set `SrtOptions.Passphrase` to the same value on the listener before `Bind` and on the client before `Connect`. Load that value from your application's configuration. The managed API is the same regardless of the crypto backend used to build libsrt.

`using` disposes each socket. The clients wait for an echo because `Send` and `SendAsync` queue data; their result alone does not confirm that the server received it.

Asynchronous operations wait on a shared readiness engine rather than blocking a thread per socket, so one process can serve many connections with a few threads. The synchronous `Accept`, `Send`, and `Receive` methods use the same engine. See the [advanced usage guide](../docs/advanced-usage.md) for explicit polling, admission decisions, streams, and diagnostics.
