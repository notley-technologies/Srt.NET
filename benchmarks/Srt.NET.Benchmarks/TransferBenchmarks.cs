// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using BenchmarkDotNet.Attributes;

namespace Srt.NET.Benchmarks;

/// <summary>Loopback send-then-receive exchanges, including the receive-side wait for data to arrive.</summary>
[MemoryDiagnoser]
public class TransferBenchmarks
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private readonly byte[] received = new byte[1500];
    private ConnectedPair pair = null!;
    private byte[] payload = [];

    [Params(1, 1316)]
    public int PayloadBytes
    {
        get; set;
    }

    [GlobalSetup]
    public void Setup()
    {
        pair = new();
        payload = new byte[PayloadBytes];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        pair.Dispose();
    }

    [Benchmark(Baseline = true)]
    public int SendReceive()
    {
        pair.Caller.Send(payload.AsSpan(), Deadline);

        return pair.Peer.Receive(received.AsSpan(), Deadline);
    }

    [Benchmark]
    public int SendReceiveMessage()
    {
        pair.Caller.SendMessage(payload.AsSpan(), timeout: Deadline);

        return pair.Peer.ReceiveMessage(received.AsSpan(), Deadline).BytesTransferred;
    }
}
