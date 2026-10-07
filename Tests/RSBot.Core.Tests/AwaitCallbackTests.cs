using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using RSBot.Core.Network;
using Xunit;

namespace RSBot.Core.Tests;

[CollectionDefinition("Packet callbacks", DisableParallelization = true)]
public class PacketCallbackCollection { }

[Collection("Packet callbacks")]
public class AwaitCallbackTests
{
    private static void Invoke(AwaitCallback callback, Packet packet)
    {
        packet.Lock();
        typeof(AwaitCallback).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(callback, new object[] { packet });
    }

    private static void Register(AwaitCallback callback)
    {
        // Exercise cleanup without opening a game connection or sending a packet.
        var callbacks = (System.Collections.Generic.List<AwaitCallback>)typeof(PacketManager)
            .GetField("_callbacks", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        callbacks.Add(callback);
    }

    [Fact]
    public void MissingConnectionClosesCallbackWithoutWaiting()
    {
        var before = PacketManager.PendingCallbackCount;
        var callback = new AwaitCallback(null, 0xB034);
        using var request = new Packet(0x7034);
        PacketManager.SendPacket(request, PacketDestination.Server, callback);
        Assert.Equal(AwaitCallbackState.NotSent, callback.State);
        Assert.Equal(before, PacketManager.PendingCallbackCount);
    }

    [Fact]
    public void CancellationRemovesRegistrationWithoutAnotherPacket()
    {
        var before = PacketManager.PendingCallbackCount;
        var callback = new AwaitCallback(null, 0xB034);
        Register(callback);
        callback.Cancel();
        callback.Cancel();
        Assert.Equal(AwaitCallbackState.Cancelled, callback.State);
        Assert.False(callback.IsCompleted);
        Assert.Equal(before, PacketManager.PendingCallbackCount);
    }

    [Fact]
    public async Task CancelledWaitClosesAndCannotBeCompletedByLateResponse()
    {
        var callback = new AwaitCallback(null, 0xB034);
        using var cancellation = new CancellationTokenSource();
        var waiting = callback.AwaitResponseAsync(30_000, cancellation.Token);
        cancellation.Cancel();
        await waiting.WaitAsync(TimeSpan.FromSeconds(2));
        using var packet = new Packet(0xB034, false, false, new byte[] { 1 });
        Invoke(callback, packet);
        Assert.Equal(AwaitCallbackState.Cancelled, callback.State);
    }

    [Fact]
    public async Task TimeoutRemovesRegistrationAndIgnoresLateSuccess()
    {
        var before = PacketManager.PendingCallbackCount;
        var callback = new AwaitCallback(null, 0xB034);
        Register(callback);
        await callback.AwaitResponseAsync(1);
        using var packet = new Packet(0xB034, false, false, new byte[] { 1 });
        Invoke(callback, packet);
        Assert.Equal(AwaitCallbackState.TimedOut, callback.State);
        Assert.Equal(before, PacketManager.PendingCallbackCount);
    }

    [Fact]
    public void UnrelatedResponseDoesNotCloseCallbackAndSuccessCannotBeCancelled()
    {
        var callback = new AwaitCallback(p => p.ReadByte() == 1
            ? AwaitCallbackResult.Success : AwaitCallbackResult.ConditionFailed, 0xB034);
        using var unrelated = new Packet(0xB034, false, false, new byte[] { 2 });
        Invoke(callback, unrelated);
        Assert.False(callback.IsClosed);
        using var matching = new Packet(0xB034, false, false, new byte[] { 1 });
        Invoke(callback, matching);
        callback.Cancel();
        Assert.Equal(AwaitCallbackState.Succeeded, callback.State);
    }

    [Fact]
    public void CancelDuringPredicateWinsAgainstInFlightSuccess()
    {
        AwaitCallback callback = null;
        callback = new AwaitCallback(_ =>
        {
            callback.Cancel();
            return AwaitCallbackResult.Success;
        }, 0xB034);
        using var packet = new Packet(0xB034, false, false, new byte[] { 1 });
        Invoke(callback, packet);
        Assert.Equal(AwaitCallbackState.Cancelled, callback.State);
    }

    [Fact]
    public void DispatchUsesIndependentReaderAndSurvivesCallbackRemoval()
    {
        var before = PacketManager.PendingCallbackCount;
        var first = new AwaitCallback(p => p.ReadByte() == 7 ? AwaitCallbackResult.Success : AwaitCallbackResult.Fail, 0xB034);
        var second = new AwaitCallback(p => p.ReadByte() == 7 ? AwaitCallbackResult.Success : AwaitCallbackResult.Fail, 0xB034);
        Register(first);
        Register(second);
        using var packet = new Packet(0xB034, false, false, new byte[] { 7 });
        packet.Lock();
        typeof(PacketManager).GetMethod("CallCallback", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { packet });
        Assert.True(first.IsCompleted);
        Assert.True(second.IsCompleted);
        Assert.Equal(7, packet.ReadByte());
        Assert.Equal(before, PacketManager.PendingCallbackCount);
    }
}
