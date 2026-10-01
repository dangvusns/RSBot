using System;
using System.Threading;
using System.Threading.Tasks;
using RSBot.Core.Event;
using Xunit;

namespace RSBot.Core.Tests;

public class EventManagerTests
{
    [Fact]
    public void Unsubscribe_RemovesOnlyTheRequestedHandler_AndCanBeRepeated()
    {
        var name = Guid.NewGuid().ToString();
        var removedCalls = 0;
        var retainedCalls = 0;
        Action removed = () => removedCalls++;
        Action retained = () => retainedCalls++;
        EventManager.SubscribeEvent(name, removed);
        EventManager.SubscribeEvent(name, retained);
        try
        {
            EventManager.UnsubscribeEvent(name, removed);
            EventManager.UnsubscribeEvent(name, removed);
            EventManager.FireEvent(name);
            Assert.Equal(0, removedCalls);
            Assert.Equal(1, retainedCalls);
        }
        finally
        {
            EventManager.UnsubscribeEvent(name, removed);
            EventManager.UnsubscribeEvent(name, retained);
        }
    }

    [Fact]
    public void UnsubscribeDuringDispatch_UsesTheCurrentSnapshot_ThenStopsFutureCalls()
    {
        var name = Guid.NewGuid().ToString();
        var calls = 0;
        Action removed = () => calls++;
        Action remove = () => EventManager.UnsubscribeEvent(name, removed);
        EventManager.SubscribeEvent(name, remove);
        EventManager.SubscribeEvent(name, removed);
        try
        {
            EventManager.FireEvent(name);
            EventManager.FireEvent(name);
            Assert.Equal(1, calls);
        }
        finally
        {
            EventManager.UnsubscribeEvent(name, remove);
            EventManager.UnsubscribeEvent(name, removed);
        }
    }

    [Fact]
    public void ConcurrentRegistrationAndRemoval_DoNotLoseHandlers()
    {
        var name = Guid.NewGuid().ToString();
        var calls = 0;
        var handlers = new Action[64];
        for (var index = 0; index < handlers.Length; index++)
            handlers[index] = () => Interlocked.Increment(ref calls);
        try
        {
            Parallel.For(0, handlers.Length, index => EventManager.SubscribeEvent(name, handlers[index]));
            EventManager.FireEvent(name);
            Assert.Equal(handlers.Length, calls);
            Parallel.For(0, handlers.Length, index => EventManager.UnsubscribeEvent(name, handlers[index]));
            EventManager.FireEvent(name);
            Assert.Equal(handlers.Length, calls);
        }
        finally
        {
            foreach (var handler in handlers) EventManager.UnsubscribeEvent(name, handler);
        }
    }
}
