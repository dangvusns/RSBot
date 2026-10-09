using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using RSBot.Core.Event;
using Xunit;

namespace RSBot.Core.Tests;

public class UiEventSubscriptionsTests
{
    [Fact]
    public void Dispatch_RetainsStartupEvents_CoalescesHiddenRendering_AndUnsubscribes()
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new Form();
                using var owner = new RecreatingPanel { Dock = DockStyle.Fill };
                form.Controls.Add(owner);
                using var subscriptions = new UiEventSubscriptions(owner);
                var name = Guid.NewGuid().ToString();
                var applied = new List<int>();
                var uiThread = Environment.CurrentManagedThreadId;
                subscriptions.Subscribe<int>(name, value =>
                {
                    Assert.Equal(uiThread, Environment.CurrentManagedThreadId);
                    applied.Add(value);
                });
                Task.Run(() =>
                {
                    EventManager.FireEvent(name, 1);
                    EventManager.FireEvent(name, 2);
                }).GetAwaiter().GetResult();
                Assert.Empty(applied);
                Assert.False(owner.IsHandleCreated);

                // Essential callbacks must run even for a tab that has never been opened.
                PumpUntil(() => applied.Count == 2);
                Assert.False(owner.IsHandleCreated);
                Assert.Equal(new[] { 1, 2 }, applied);
                form.Show();
                PumpUntil(() => applied.Count == 2);
                Assert.Equal(new[] { 1, 2 }, applied);
                var nestedOrder = new List<char>();
                Task.Run(() =>
                {
                    subscriptions.Post(() =>
                    {
                        nestedOrder.Add('A');
                        subscriptions.Post(() => nestedOrder.Add('C'));
                    });
                    subscriptions.Post(() => nestedOrder.Add('B'));
                }).GetAwaiter().GetResult();
                PumpUntil(() => nestedOrder.Count == 3);
                Assert.Equal(new[] { 'A', 'B', 'C' }, nestedOrder);

                owner.Visible = false;
                var rendered = 0;
                Task.Run(() =>
                {
                    subscriptions.Post(() => rendered = 1, "render");
                    subscriptions.Post(() => rendered = 2, "render");
                    subscriptions.Post(() => applied.Add(3));
                }).GetAwaiter().GetResult();
                PumpUntil(() => applied.Count == 3);
                PumpFor(TimeSpan.FromMilliseconds(600));
                Assert.Equal(0, rendered);
                owner.Visible = true;
                PumpUntil(() => rendered == 2);

                form.WindowState = FormWindowState.Minimized;
                subscriptions.Post(() => rendered = 3, "render");
                PumpFor(TimeSpan.FromMilliseconds(600));
                Assert.Equal(2, rendered);
                form.WindowState = FormWindowState.Normal;
                PumpUntil(() => rendered == 3);

                owner.HandleDestroyed += (_, _) =>
                {
                    Task.Run(() => subscriptions.Post(() => applied.Add(4))).GetAwaiter().GetResult();
                };
                owner.Recreate();
                PumpUntil(() => applied.Count == 4);
                Assert.Equal(new[] { 1, 2, 3, 4 }, applied);

                owner.Dispose();
                EventManager.FireEvent(name, 4);
                subscriptions.Post(() => applied.Add(5));
                PumpFor(TimeSpan.FromMilliseconds(300));
                Assert.Equal(new[] { 1, 2, 3, 4 }, applied);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "UI dispatcher test timed out.");
        if (failure != null) throw failure;
    }

    private sealed class RecreatingPanel : Panel
    {
        public void Recreate() => RecreateHandle();
    }

    private static void PumpUntil(Func<bool> completed)
    {
        var deadline = Stopwatch.StartNew();
        while (!completed() && deadline.Elapsed < TimeSpan.FromSeconds(3))
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
        Assert.True(completed(), "Expected UI work was not applied.");
    }

    private static void PumpFor(TimeSpan duration)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < duration)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }
}
