// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Srt.Net;
using Srt.Net.Interop;

namespace Srt.Net.Benchmarks;

/// <summary>Per-call cost and allocations of nonblocking operations on an idle connection.</summary>
/// <remarks>
/// Rows are grouped by category, so each ratio compares an operation only with the baseline of its own category: the
/// native would-block receive for the managed receive paths. The other categories have no native counterpart here and
/// report absolute costs only.
/// </remarks>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class OperationBenchmarks
{
    private const string Receive = "Receive";
    private const string Options = "Options";
    private const string Status = "Status";
    private const string Poll = "Poll";

    private readonly byte[] buffer = new byte[65536];
    private readonly SrtPollEvent[] events = new SrtPollEvent[1];
    private ConnectedPair pair = null!;
    private SrtPoller readPoller = null!;
    private SrtPoller writePoller = null!;
    private int peerId;

    [GlobalSetup]
    public void Setup()
    {
        pair = new();
        readPoller = new();
        writePoller = new();
        readPoller.Add(pair.Peer, SrtPollEvents.Read);
        writePoller.Add(pair.Caller, SrtPollEvents.Write);

        // The pair outlives every benchmark invocation, so the borrowed ID stays valid.
        peerId = pair.Peer.Handle.DangerousGetHandle().ToInt32();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        writePoller.Dispose();
        readPoller.Dispose();
        pair.Dispose();
    }

    [Benchmark(Baseline = true, Description = "Native srt_recv would-block")]
    [BenchmarkCategory(Receive)]
    public unsafe int NativeReceiveWouldBlock()
    {
        fixed (byte* pointer = buffer)
        {
            return NativeMethods.srt_recv(peerId, pointer, buffer.Length) + NativeMethods.srt_getlasterror(out _);
        }
    }

    [Benchmark]
    [BenchmarkCategory(Receive)]
    public bool TryReceiveWouldBlock()
    {
        return pair.Peer.TryReceive(buffer.AsSpan(), out _);
    }

    [Benchmark]
    [BenchmarkCategory(Receive)]
    public bool TryReceiveMessageWouldBlock()
    {
        return pair.Peer.TryReceiveMessage(buffer.AsSpan(), out _);
    }

    [Benchmark]
    [BenchmarkCategory(Status)]
    public SrtSocketState State()
    {
        return pair.Peer.State;
    }

    [Benchmark]
    [BenchmarkCategory(Options)]
    public int GetOptionInt32()
    {
        return pair.Peer.GetOption(SrtOptions.ReceiveLatency);
    }

    [Benchmark]
    [BenchmarkCategory(Options)]
    public long GetOptionInt64()
    {
        return pair.Peer.GetOption(SrtOptions.MaximumBandwidth);
    }

    [Benchmark]
    [BenchmarkCategory(Options)]
    public void SetOptionInt64()
    {
        pair.Caller.SetOption(SrtOptions.MaximumBandwidth, -1L);
    }

    [Benchmark]
    [BenchmarkCategory(Status)]
    public ulong GetSendBuffer()
    {
        return pair.Caller.GetSendBuffer().Bytes;
    }

    [Benchmark]
    [BenchmarkCategory(Status)]
    public SrtStatistics GetStatistics()
    {
        return pair.Caller.GetStatistics();
    }

    [Benchmark]
    [BenchmarkCategory(Poll)]
    public int PollIdleIntoSpan()
    {
        return readPoller.Wait(events.AsSpan(), TimeSpan.Zero);
    }

    [Benchmark]
    [BenchmarkCategory(Poll)]
    public int PollWritableIntoSpan()
    {
        return writePoller.Wait(events.AsSpan(), TimeSpan.Zero);
    }

    [Benchmark]
    [BenchmarkCategory(Poll)]
    public SrtPollEvent[] PollWritableArray()
    {
        return writePoller.Wait(TimeSpan.Zero);
    }
}
