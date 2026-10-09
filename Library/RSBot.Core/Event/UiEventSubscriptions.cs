using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace RSBot.Core.Event;

// UI event subscriptions retain startup work and batch rendering without changing core event dispatch.
public sealed class UiEventSubscriptions : IDisposable
{
    private readonly Control _owner;
    private readonly int _uiThread = Environment.CurrentManagedThreadId;
    private readonly object _gate = new();
    private readonly Queue<Action> _ordered = new();
    private readonly Dictionary<string, Action> _rendering = new();
    private readonly List<(string Name, Delegate Handler)> _subscriptions = new();
    private readonly System.Windows.Forms.Timer _timer;
    private bool _posted;
    private bool _draining;
    private bool _disposed;

    public UiEventSubscriptions(Control owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
        _timer = new System.Windows.Forms.Timer { Interval = 250 };
        _timer.Tick += (_, _) => Drain(true, allowBeforeHandle: true);
        _timer.Start();
        _owner.HandleCreated += HandleCreated;
        _owner.Disposed += OwnerDisposed;
    }

    public void Subscribe(string name, Action handler, bool coalesce = false) =>
        Add(name, (Action)(() => Post(() => handler(), coalesce ? name : null)));
    public void Subscribe<T>(string name, Action<T> handler, bool coalesce = false) =>
        Add(name, (Action<T>)(a => Post(() => handler(a), coalesce ? name : null)));
    public void Subscribe<T1, T2>(string name, Action<T1, T2> handler, bool coalesce = false) =>
        Add(name, (Action<T1, T2>)((a, b) => Post(() => handler(a, b), coalesce ? name : null)));
    public void Subscribe<T1, T2, T3>(string name, Action<T1, T2, T3> handler, bool coalesce = false) =>
        Add(name, (Action<T1, T2, T3>)((a, b, c) => Post(() => handler(a, b, c), coalesce ? name : null)));
    public void Subscribe<T1, T2, T3, T4>(string name, Action<T1, T2, T3, T4> handler, bool coalesce = false) =>
        Add(name, (Action<T1, T2, T3, T4>)((a, b, c, d) => Post(() => handler(a, b, c, d), coalesce ? name : null)));

    private void Add(string name, Delegate handler)
    {
        _subscriptions.Add((name, handler));
        EventManager.SubscribeEvent(name, handler);
    }

    public void Post(Action action, string renderingKey = null)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (renderingKey != null) _rendering[renderingKey] = action;
            else _ordered.Enqueue(action);
        }
        if (renderingKey != null) return;
        if (Environment.CurrentManagedThreadId == _uiThread) Drain(false, allowBeforeHandle: true);
        else Schedule();
    }

    private void HandleCreated(object sender, EventArgs e) => Schedule();
    private void OwnerDisposed(object sender, EventArgs e) => Dispose();

    private void Schedule()
    {
        lock (_gate)
        {
            if (_disposed || _posted || _ordered.Count == 0 || !_owner.IsHandleCreated) return;
            _posted = true;
        }
        try { _owner.BeginInvoke((Action)(() => Drain(false))); }
        catch (InvalidOperationException) { lock (_gate) _posted = false; }
    }

    private void Drain(bool includeRendering, bool allowBeforeHandle = false)
    {
        if (_draining) return;
        Action[] actions;
        lock (_gate)
        {
            _posted = false;
            if (_disposed || (!allowBeforeHandle && !_owner.IsHandleCreated)) return;
            actions = _ordered.ToArray();
            _ordered.Clear();
            if (includeRendering && _owner.IsHandleCreated && _owner.Visible && _owner.Enabled
                && _owner.FindForm()?.WindowState != FormWindowState.Minimized)
            {
                actions = actions.Concat(_rendering.Values).ToArray();
                _rendering.Clear();
            }
        }
        _draining = true;
        try
        {
            foreach (var action in actions)
            {
                if (_owner.IsDisposed || _owner.Disposing) break;
                try { action(); }
                catch (Exception e) { Log.Fatal(e); }
            }
        }
        finally
        {
            _draining = false;
            Schedule();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _ordered.Clear();
            _rendering.Clear();
        }
        _timer.Dispose();
        _owner.HandleCreated -= HandleCreated;
        _owner.Disposed -= OwnerDisposed;
        foreach (var subscription in _subscriptions)
            EventManager.UnsubscribeEvent(subscription.Name, subscription.Handler);
        _subscriptions.Clear();
    }
}
