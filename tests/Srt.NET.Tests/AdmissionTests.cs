// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Net;
using Srt.Net;
using Srt.Net.Interop;
using Xunit;

namespace Srt.NET.Tests;

public sealed class AdmissionTests : IClassFixture<RuntimeFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdmissionCanConfigurePerPeerEncryption(bool ipv6)
    {
        using SrtSocket listener = new();
        using SrtSocket caller = new();
        SrtConnectionRequest? captured = null;
        listener.SetConnectionHandler(request =>
        {
            captured = request;
            request.SetOption(SrtOptions.Passphrase, "a per-stream test password");

            return request.StreamId == "stream-東京";
        });
        listener.Bind(new(ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback, 0));
        listener.Listen();
        caller.SetOption(SrtOptions.StreamId, "stream-東京");
        caller.SetOption(SrtOptions.Passphrase, "a per-stream test password");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        caller.Connect(listener.LocalEndPoint, TestSockets.Deadline);
        using SrtSocket peer = listener.Accept(TestSockets.Deadline);
        Assert.NotNull(captured);
        Assert.Equal(caller.LocalEndPoint, captured!.RemoteEndPoint);
        Assert.Equal(SrtKeyMaterialState.Secured, peer.GetOption(SrtOptions.ReceiveEncryptionState));
        Assert.Throws<InvalidOperationException>(() => captured.SetOption(SrtOptions.Passphrase, "expired request"));
    }

    [Fact]
    public void AdmissionRejectsWithAnApplicationReason()
    {
        using SrtSocket listener = new();
        using SrtSocket caller = new();
        listener.SetConnectionHandler(request =>
        {
            request.SetRejectionReason(SrtRejectReason.UserDefined + 1);

            return false;
        });
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        SrtException error = Assert.Throws<SrtException>(() => caller.Connect(listener.LocalEndPoint, TestSockets.Deadline));
        Assert.Equal(SrtErrorCode.ConnectionRejected, error.SrtErrorCode);
        Assert.Equal(SrtRejectReason.UserDefined + 1, error.RejectionReason);
    }

    [Fact]
    public void AdmissionExceptionsRejectWithoutCrossingTheNativeBoundary()
    {
        using SrtSocket listener = new();
        using SrtSocket caller = new();
        InvalidOperationException failure = new("Test rejection");
        listener.SetConnectionHandler(_ => throw failure);
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();
        Assert.Throws<SrtException>(() => caller.Connect(listener.LocalEndPoint, TestSockets.Deadline));
        Assert.Same(failure, listener.LastAdmissionException);
    }

    [Fact]
    public unsafe void StreamIdsThatAreNotUtf8ReadTheSameInTheHandlerAndOnTheAcceptedSocket()
    {
        using SrtSocket listener = new();
        using SrtSocket caller = new();
        string? admitted = null;
        listener.SetConnectionHandler(request =>
        {
            admitted = request.StreamId;

            return true;
        });
        listener.Bind(new(IPAddress.Loopback, 0));
        listener.Listen();

        // A peer can send any bytes; set them below the managed API, which only accepts valid UTF-8.
        byte[] streamId = [0x63, 0x61, 0x6D, 0xFF, 0xFE];

        fixed (byte* pointer = streamId)
        {
            Assert.NotEqual(NativeConstants.SRT_ERROR, NativeMethods.srt_setsockflag(caller.Handle.DangerousGetHandle().ToInt32(), 46, pointer, streamId.Length));
        }

        caller.Connect(listener.LocalEndPoint, TestSockets.Deadline);
        using SrtSocket accepted = listener.Accept(TestSockets.Deadline);
        string read = accepted.GetOption(SrtOptions.StreamId);

        Assert.Equal(admitted, read);
        Assert.StartsWith("cam", read, StringComparison.Ordinal);
        Assert.Contains('\uFFFD', read);
    }
}
