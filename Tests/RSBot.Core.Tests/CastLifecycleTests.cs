using System;
using System.Collections.Generic;
using System.Reflection;
using RSBot.Core.Components;
using RSBot.Core.Client;
using RSBot.Core.Network;
using RSBot.Core.Objects;
using Xunit;
using GameAction = RSBot.Core.Objects.Action;

namespace RSBot.Core.Tests;

[Collection("Packet callbacks")]
public class CastLifecycleTests
{
    [Fact]
    public void CastOwnershipRejectsReentryAndResetCancelsOwnedCallbacks()
    {
        var previousPlayer = Game.Player;
        var previousReady = Game.Ready;
        var previousReferences = Game.ReferenceManager;
        var playerProperty = typeof(Game).GetProperty("Player");
        var readyProperty = typeof(Game).GetProperty("Ready");
        var ownership = typeof(SkillManager).GetMethod("WithCastOwnership", BindingFlags.Static | BindingFlags.NonPublic)
            .MakeGenericMethod(typeof(bool));
        var callbackField = typeof(SkillManager).GetField("_activeCastCallbacks", BindingFlags.Static | BindingFlags.NonPublic);
        var cancel = typeof(SkillManager).GetMethod("CancelPendingCasts", BindingFlags.Static | BindingFlags.NonPublic);
        try
        {
            Game.ReferenceManager = new ReferenceManager();
            var player = new Player(0);
            player.State.LifeState = LifeState.Alive;
            playerProperty.SetValue(null, player);
            readyProperty.SetValue(null, true);
            Func<int, bool> owned = _ =>
            {
                Assert.True(SkillManager.IsCasting);
                Func<int, bool> competing = __ => throw new Exception("Competing cast entered");
                Assert.Equal(false, ownership.Invoke(null, new object[] { competing, false }));
                var callback = new AwaitCallback(null, 0xB070);
                callbackField.SetValue(null, new[] { callback });
                cancel.Invoke(null, new object[] { false, false });
                Assert.Equal(AwaitCallbackState.Cancelled, callback.State);
                return true;
            };
            Assert.Equal(true, ownership.Invoke(null, new object[] { owned, false }));
            Assert.False(SkillManager.IsCasting);
            Func<int, bool> throws = _ => throw new InvalidOperationException("cast failed");
            Assert.Throws<TargetInvocationException>(() => ownership.Invoke(null, new object[] { throws, false }));
            Assert.False(SkillManager.IsCasting);
            Func<int, bool> next = _ => true;
            Assert.Equal(true, ownership.Invoke(null, new object[] { next, false }));
        }
        finally
        {
            playerProperty.SetValue(null, previousPlayer);
            readyProperty.SetValue(null, previousReady);
            Game.ReferenceManager = previousReferences;
        }
    }

    private static Packet CastStart(uint skill = 7, uint caster = 10, uint target = 20)
    {
        var packet = new Packet(0xB070);
        packet.WriteByte(1);
        packet.WriteByte(4);
        packet.WriteByte(0x30); // Global fixture
        packet.WriteUInt(skill);
        packet.WriteUInt(caster);
        packet.WriteUInt(30); // cast instance
        packet.WriteUInt(40);
        packet.WriteUInt(target);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteByte(0x7F); // first payload byte must remain unread
        packet.Lock();
        return packet;
    }

    [Theory]
    [InlineData(GameClientType.Thailand, "0104070000000A0000001E00000014000000017F")]
    [InlineData(GameClientType.Chinese, "010430070000000A0000001E00000014000000017F")]
    [InlineData(GameClientType.Global, "010430070000000A0000001E000000280000001400000055017F")]
    [InlineData(GameClientType.Japanese, "010430070000000A0000001E0000001400000055017F")]
    [InlineData(GameClientType.Rigid, "010430070000000A0000001E000000280000001400000001557F")]
    public void SharedHeaderPreservesClientLayoutAndPayload(GameClientType clientType, string payload)
    {
        using var packet = new Packet(0xB070, false, false, Convert.FromHexString(payload));
        packet.Lock();
        Assert.Equal(1, packet.ReadByte());
        var action = GameAction.ReadCastStart(packet, clientType);
        Assert.Equal((uint)7, action.SkillId);
        Assert.Equal((uint)10, action.ExecutorId);
        Assert.Equal((uint)20, action.TargetId);
        Assert.Equal((uint)30, action.Id);
        Assert.Equal((ActionStateFlag)1, action.Flag);
        Assert.Equal(0x7F, packet.ReadByte());
    }

    [Theory]
    [InlineData(8, 10, 20)]
    [InlineData(7, 11, 20)]
    [InlineData(7, 10, 21)]
    public void AnotherSkillCasterOrRecipientCannotAcceptPendingBuff(uint skill, uint caster, uint target)
    {
        using var packet = CastStart(skill, caster, target);
        Assert.Equal(AwaitCallbackResult.ConditionFailed,
            GameAction.MatchCastStart(packet, GameClientType.Global, 7, 10, 20));
    }

    [Fact]
    public void MatchingAcceptanceIsDistinctFromAnonymousRefusal()
    {
        using var accepted = CastStart();
        Assert.Equal(AwaitCallbackResult.Success,
            GameAction.MatchCastStart(accepted, GameClientType.Global, 7, 10, 20));
        using var refused = new Packet(0xB070, false, false, new byte[] { 2, 5 });
        refused.Lock();
        Assert.Equal(AwaitCallbackResult.Fail,
            GameAction.MatchCastStart(refused, GameClientType.Global, 7, 10, 20));
    }

    [Fact]
    public void ExpiredCastCannotAttributeLaterHitsAndSessionResetClearsHistory()
    {
        var history = (Dictionary<uint, (uint ExecutorId, long Tick)>)typeof(GameAction)
            .GetField("_recentCasts", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var lookup = typeof(GameAction).GetMethod("GetCastExecutor", BindingFlags.Static | BindingFlags.NonPublic);
        try
        {
            history[30] = (10, Environment.TickCount64 - 30_001);
            Assert.Equal((uint)0, lookup.Invoke(null, new object[] { (uint)30 }));
            Assert.False(history.ContainsKey(30));
            history[31] = (10, Environment.TickCount64);
            Assert.Equal((uint)10, lookup.Invoke(null, new object[] { (uint)31 }));
            typeof(SkillManager).GetMethod("CancelPendingCasts", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { true, true });
            Assert.Empty(history);
        }
        finally { history.Clear(); }
    }
}
