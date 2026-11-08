// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using BenchmarkDotNet.Attributes;
using Srt.Net.Interop;

namespace Srt.NET.Benchmarks;

/// <summary>
/// The native cost of one readiness check: a poll created for each wait versus a long-lived poll, which is
/// why the async engine keeps one shared poll for all sockets. Both use a zero timeout, so only overhead is measured.
/// </summary>
[MemoryDiagnoser]
public class WaitStrategyBenchmarks
{
    private ConnectedPair pair = null!;
    private int peerId;
    private int cachedPoll = NativeConstants.SRT_ERROR;

    [GlobalSetup]
    public void Setup()
    {
        pair = new();
        peerId = pair.Peer.Handle.DangerousGetHandle().ToInt32();
        cachedPoll = NativeMethods.srt_epoll_create();

        if (cachedPoll == NativeConstants.SRT_ERROR)
        {
            throw new InvalidOperationException("srt_epoll_create failed.");
        }

        int events = NativeConstants.SRT_EPOLL_IN | NativeConstants.SRT_EPOLL_ERR;

        if (NativeMethods.srt_epoll_add_usock(cachedPoll, peerId, ref events) == NativeConstants.SRT_ERROR)
        {
            throw new InvalidOperationException("srt_epoll_add_usock failed.");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (cachedPoll != NativeConstants.SRT_ERROR)
        {
            _ = NativeMethods.srt_epoll_release(cachedPoll);
            cachedPoll = NativeConstants.SRT_ERROR;
        }

        pair.Dispose();
    }

    [Benchmark(Baseline = true, Description = "Poll created per wait")]
    public int PollPerWait()
    {
        int poll = NativeMethods.srt_epoll_create();
        int events = NativeConstants.SRT_EPOLL_IN | NativeConstants.SRT_EPOLL_ERR;
        _ = NativeMethods.srt_epoll_add_usock(poll, peerId, ref events);
        int ready = NativeMethods.srt_epoll_uwait(poll, out _, 1, 0);
        _ = NativeMethods.srt_epoll_release(poll);

        return ready;
    }

    [Benchmark(Description = "Reused poll")]
    public int ReusedPoll()
    {
        return NativeMethods.srt_epoll_uwait(cachedPoll, out _, 1, 0);
    }
}
