// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Srt.Net;
using Xunit;

namespace Srt.Net.Tests;

/// <summary>Exercises every option on <see cref="SrtOptions"/> on a fresh, unbound socket.</summary>
public sealed class OptionTests : IClassFixture<RuntimeFixture>
{
    /// <summary>Marks an option that libsrt reports but does not let the application set.</summary>
    private static readonly object ReadOnly = new();

    /// <summary>
    /// A value for each option that libsrt 1.5.7 accepts before binding and, where readable, reports back unchanged.
    /// Every option needs an entry, so a new option fails <see cref="EveryOptionWorksOnAFreshSocket"/> until it is added.
    /// </summary>
    /// <remarks>
    /// The values differ from libsrt's live-profile defaults so that a round trip proves the value was stored. Buffer sizes
    /// are whole multiples of 1472 bytes (the default 1500-byte segment less 28 bytes of IP and UDP headers), because
    /// libsrt stores them as packet counts.
    /// </remarks>
    private static readonly Dictionary<string, object> Samples = new(StringComparer.Ordinal)
    {
        [nameof(SrtOptions.MaximumSegmentSize)] = 1400,
        [nameof(SrtOptions.InitialSequenceNumber)] = ReadOnly,
        [nameof(SrtOptions.FlowControlWindow)] = 10000,
        [nameof(SrtOptions.Linger)] = new SrtLinger(true, 10),
        [nameof(SrtOptions.UdpSendBufferSize)] = 131072,
        [nameof(SrtOptions.UdpReceiveBufferSize)] = 131072,
        [nameof(SrtOptions.AvailableEvents)] = ReadOnly,
        [nameof(SrtOptions.PendingSendPackets)] = ReadOnly,
        [nameof(SrtOptions.PendingReceivePackets)] = ReadOnly,
        [nameof(SrtOptions.TimestampBasedDelivery)] = false,
        [nameof(SrtOptions.IpTimeToLive)] = 32,
        [nameof(SrtOptions.IpTypeOfService)] = 0x10,
        [nameof(SrtOptions.SendDropDelay)] = 100,
        [nameof(SrtOptions.DriftTracer)] = false,
        [nameof(SrtOptions.MinimumInputBandwidth)] = 1_000_000L,
        [nameof(SrtOptions.MaximumReorderTolerance)] = 10,
        [nameof(SrtOptions.MinimumPeerVersion)] = 0x010300,
        [nameof(SrtOptions.CongestionController)] = "file",
        [nameof(SrtOptions.KeyRefreshRate)] = 0x100000,
        [nameof(SrtOptions.KeyPreannounce)] = 0x400,
        [nameof(SrtOptions.BindToDevice)] = "lo",
        [nameof(SrtOptions.PacketFilter)] = "fec,cols:10,rows:5",
        [nameof(SrtOptions.RetransmissionAlgorithm)] = 0,
        [nameof(SrtOptions.TransmissionType)] = SrtTransmissionType.File,
        [nameof(SrtOptions.MessageApi)] = false,
        [nameof(SrtOptions.StreamId)] = "option-test-東京",
        [nameof(SrtOptions.Passphrase)] = "option test passphrase",
        [nameof(SrtOptions.EncryptionKeySize)] = 24,
        [nameof(SrtOptions.EnforcedEncryption)] = false,
        [nameof(SrtOptions.ReceiveLatency)] = 250,
        [nameof(SrtOptions.PeerLatency)] = 300,
        [nameof(SrtOptions.PayloadSize)] = 1000,
        [nameof(SrtOptions.SendBufferSize)] = 1472 * 1000,
        [nameof(SrtOptions.ReceiveBufferSize)] = 1472 * 1000,
        [nameof(SrtOptions.MaximumBandwidth)] = 10_000_000L,
        [nameof(SrtOptions.InputBandwidth)] = 2_000_000L,
        [nameof(SrtOptions.OverheadBandwidth)] = 50,
        [nameof(SrtOptions.ConnectTimeout)] = 2000,
        [nameof(SrtOptions.PeerIdleTimeout)] = 3000,
        [nameof(SrtOptions.Rendezvous)] = true,
        [nameof(SrtOptions.ReuseAddress)] = false,
        [nameof(SrtOptions.IPv6Only)] = 1,
        [nameof(SrtOptions.TooLatePacketDrop)] = false,
        [nameof(SrtOptions.NegativeAcknowledgmentReports)] = false,
        [nameof(SrtOptions.SendEncryptionState)] = ReadOnly,
        [nameof(SrtOptions.ReceiveEncryptionState)] = ReadOnly,
        [nameof(SrtOptions.PeerVersion)] = ReadOnly
    };

    private static readonly MethodInfo ExerciseMethod = typeof(OptionTests).GetMethod(nameof(Exercise), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static TheoryData<string> OptionNames => [.. FindOptions().Select(option => option.Key)];

    [Fact]
    public void FindsTheOptions()
    {
        // Guards the reflection itself: an empty enumeration would make the theory pass vacuously.
        Assert.True(FindOptions().Count >= 47, $"Found only {FindOptions().Count} options on SrtOptions.");
    }

    [Fact]
    public void EverySampleNamesAnExistingOption()
    {
        Dictionary<string, object> options = FindOptions();

        foreach (string name in Samples.Keys)
        {
            Assert.True(options.ContainsKey(name), $"{nameof(Samples)} has an entry for {name}, which is not an option on SrtOptions.");
        }
    }

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void EveryOptionWorksOnAFreshSocket(string name)
    {
        Assert.True(Samples.TryGetValue(name, out object? sample),
            $"SrtOptions.{name} has no sample value. Add one to {nameof(OptionTests)}.{nameof(Samples)} so the option is tested.");

        object option = FindOptions()[name];
        Type valueType = option.GetType().GetGenericArguments()[0];

        try
        {
            ExerciseMethod.MakeGenericMethod(valueType).Invoke(null, [option, sample]);
        }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
        }
    }

    private static void Exercise<T>(SrtOption<T> option, object sample)
    {
        if (ReferenceEquals(option, SrtOptions.BindToDevice) && !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // libsrt implements SO_BINDTODEVICE only on Linux and reports any other platform as unsupported.
            using SrtSocket unsupported = new();
            Assert.Throws<SrtException>(() => unsupported.GetOption(option));
            Assert.Throws<SrtException>(() => unsupported.SetOption(option, (T)sample));

            return;
        }

        using SrtSocket socket = new();

        if (option.CanRead)
        {
            // Every readable option reports a value before the socket is bound or connected.
            _ = socket.GetOption(option);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => socket.GetOption(option));
        }

        if (!option.CanWrite)
        {
            Assert.Same(ReadOnly, sample);
            Assert.True(option.CanRead, $"{option.Name} is neither readable nor writable.");
            Assert.Throws<ArgumentException>(() => socket.SetOption(option, default!));

            return;
        }

        Assert.NotSame(ReadOnly, sample);
        T value = Assert.IsType<T>(sample);
        socket.SetOption(option, value);

        if (option.CanRead)
        {
            Assert.Equal(value, socket.GetOption(option));
        }
    }

    private static Dictionary<string, object> FindOptions()
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static;
        Dictionary<string, object> options = new(StringComparer.Ordinal);

        foreach (MemberInfo member in typeof(SrtOptions).GetMembers(Flags))
        {
            (Type? type, object? value) = member switch
            {
                PropertyInfo property => (property.PropertyType, property.GetValue(null)),
                FieldInfo field => (field.FieldType, field.GetValue(null)),
                _ => (null, null)
            };

            if (type is { IsGenericType: true } && type.GetGenericTypeDefinition() == typeof(SrtOption<>))
            {
                options.Add(member.Name, value ?? throw new InvalidOperationException($"SrtOptions.{member.Name} is null."));
            }
        }

        return options;
    }
}
