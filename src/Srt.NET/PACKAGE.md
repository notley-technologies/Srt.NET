# Srt.NET

[![CI](https://github.com/notley-technologies/Srt.NET/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/notley-technologies/Srt.NET/actions/workflows/build.yml) [![CodeQL](https://github.com/notley-technologies/Srt.NET/actions/workflows/codeql.yml/badge.svg?branch=main)](https://github.com/notley-technologies/Srt.NET/actions/workflows/codeql.yml)

Srt.NET is a .NET socket API for [Secure Reliable Transport](https://github.com/Haivision/srt) (SRT). It calls Haivision's libsrt C API directly through P/Invoke, with synchronous and `ValueTask`-based asynchronous operations, and ships libsrt with statically linked OpenSSL for seven runtimes. It targets .NET Standard 2.0 (including .NET Framework 4.8) and .NET 8 or later, where it is trimming- and Native AOT-compatible.

It supports callers, listeners, and rendezvous connections; IPv4 and IPv6; encrypted live messages and file-mode byte streams; typed options; timeouts and cancellation; readiness polling; connection admission; message metadata; transport statistics; native logging; and connection metrics.

## Send and receive

Listener:

```csharp
using System;
using System.Net;
using System.Text;
using Srt.Net;

using SrtSocket listener = new();
listener.SetOption(SrtOptions.ReceiveLatency, 120);
listener.Bind(new(IPAddress.Any, 9000));
listener.Listen();

using SrtSocket connection = await listener.AcceptAsync();
byte[] buffer = new byte[1500];
int count = await connection.ReceiveAsync(buffer);
Console.WriteLine(Encoding.UTF8.GetString(buffer, 0, count));
await connection.SendAsync(Encoding.UTF8.GetBytes("received"));

// Live mode discards queued data on close, so stay connected until the caller has read the reply and closed.
try
{
    await connection.ReceiveAsync(buffer);
}
catch (SrtException error) when (error.SrtErrorCode is SrtErrorCode.ConnectionLost or SrtErrorCode.InvalidSocket)
{
}
```

Caller:

```csharp
using System;
using System.Net;
using System.Text;
using System.Threading;
using Srt.Net;

using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
using SrtSocket connection = new();
connection.SetOption(SrtOptions.StreamId, "camera-1");
await connection.ConnectAsync(new(IPAddress.Loopback, 9000), timeout.Token);
await connection.SendAsync(Encoding.UTF8.GetBytes("Hello over SRT"), timeout.Token);
byte[] reply = new byte[1500];
int count = await connection.ReceiveAsync(reply, timeout.Token);
Console.WriteLine(Encoding.UTF8.GetString(reply, 0, count));
```

Every operation also has a synchronous form (`Connect`, `Accept`, `Send`, `Receive`, and so on) that takes a timeout and a `CancellationToken`. For encryption, set `SrtOptions.Passphrase` to the same secret on the caller and listener before connecting or listening, and supply it from your application's configuration. No separate cryptographic library is needed.

## Platforms

The package contains encryption-enabled libsrt for all of these runtimes:

| Runtime identifier | Runs on |
| --- | --- |
| `win-x64` | 64-bit Windows 10 and later; no separate cryptography or MSVC runtime installation |
| `linux-x64`, `linux-arm64` | glibc 2.28 and later (for example RHEL 8, Debian 10, Ubuntu 20.04, and later); no libstdc++ dependency |
| `linux-musl-x64`, `linux-musl-arm64` | musl 1.2.5 and later (Alpine 3.22 and later) |
| `osx-x64` | Intel macOS 13 and later |
| `osx-arm64` | Apple Silicon macOS 13 and later |

Windows x86 and Windows Arm64 are not included; Windows Arm64 can run the `win-x64` build in x64 processes under emulation. .NET Framework projects must use `PackageReference` and run as x64; the package's MSBuild target copies `srt.dll` into the output. On Windows, `srt.dll` is never loaded from the current directory or `PATH`.

## Documentation

- [README](https://github.com/notley-technologies/Srt.NET/blob/main/README.md): behavior, platforms, and verifying releases
- [Advanced usage guide](https://github.com/notley-technologies/Srt.NET/blob/main/docs/advanced-usage.md)
- [Native API coverage](https://github.com/notley-technologies/Srt.NET/blob/main/docs/api-coverage.md), including deliberately deferred features such as connection bonding
- [Statistics reference](https://github.com/notley-technologies/Srt.NET/blob/main/docs/statistics.md)
- [Client and server examples](https://github.com/notley-technologies/Srt.NET/blob/main/examples/README.md)
- [Changelog](https://github.com/notley-technologies/Srt.NET/blob/main/CHANGELOG.md)
- [Verifying releases](https://github.com/notley-technologies/Srt.NET/blob/main/SECURITY.md#verifying-releases) and [reporting vulnerabilities](https://github.com/notley-technologies/Srt.NET/blob/main/SECURITY.md)
- [Third-party notices](https://github.com/notley-technologies/Srt.NET/blob/main/THIRD_PARTY_NOTICES.md): libsrt, OpenSSL, and the compiler runtime libraries keep their own licenses

## License

Srt.NET is an independent project. It is not affiliated with or endorsed by Haivision or the SRT Alliance; SRT and libsrt are their projects, used here under libsrt's MPL-2.0 license.

Srt.NET is licensed under the [Mozilla Public License 2.0](https://github.com/notley-technologies/Srt.NET/blob/main/LICENSE.txt). Applications under any license can use the package. Changed Srt.NET source files that you distribute stay under the MPL. The package copies Srt.NET's license and the dependency notices into `licenses/Srt.NET/` in build and publish output.
