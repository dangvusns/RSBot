using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using RSBot.Core;
using RSBot.Core.Event;
using RSBot.Core.Objects;
using SDUI.Controls;

namespace RSBot.Chat.Views;

[ToolboxItem(false)]
public partial class Main : DoubleBufferedControl
{
    private const int MaximumCharacters = 60000;
    private const int RetainedCharacters = 40000;

    // Chat arrives on network threads; lines are buffered per box and written by a UI timer in batches.
    private readonly Dictionary<RichTextBox, StringBuilder> _pending = new();
    private readonly System.Windows.Forms.Timer _flushTimer;
    private int _settingsDirty;
    private volatile bool _disposed;

    public Main()
    {
        InitializeComponent();

        components ??= new Container();
        _flushTimer = new System.Windows.Forms.Timer(components) { Interval = 250 };
        _flushTimer.Tick += (s, e) => FlushPending();
        _flushTimer.Start();

        SubscribeEvents();
        VisibleChanged += (_, _) => _flushTimer.Enabled = Visible && Enabled;
        EnabledChanged += (_, _) => _flushTimer.Enabled = Visible && Enabled;
        Disposed += (_, _) =>
        {
            _disposed = true;
            EventManager.UnsubscribeEvent("OnEnterGame", OnEnterGame);
            lock (_pending) _pending.Clear();
        };
    }

    /// <summary>
    ///     Subscribes the events.
    /// </summary>
    private void SubscribeEvents()
    {
        EventManager.SubscribeEvent("OnEnterGame", OnEnterGame);
    }

    /// <summary>
    ///     Sends the chat message.
    /// </summary>
    /// <param name="sender">The sender.</param>
    private void SendChatMessage(Control sender)
    {
        if (!Enum.TryParse<ChatType>(sender.Tag.ToString(), out var chatType))
            return;

        if (chatType == ChatType.Global)
            Bundle.Chat.SendGlobalChatPacket(sender.Text);
        else
            Bundle.Chat.SendChatPacket(chatType, sender.Text, txtRecievePrivate.Text);

        if (chatType == ChatType.Private)
            PlayerConfig.Set("RSBot.Chat.LastWhisper", txtRecievePrivate.Text);
    }

    /// <summary>
    ///     Appends the message.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="sender">The sender.</param>
    /// <param name="type">The type.</param>
    public void AppendMessage(string message, string sender, ChatType type)
    {
        if (_disposed)
            return;
        RichTextBox target = type switch
        {
            ChatType.Academy => txtAcademy,
            ChatType.All or ChatType.AllGM or ChatType.Npc => txtAll,
            ChatType.Global or ChatType.Notice => txtGlobal,
            ChatType.Guild => txtGuild,
            ChatType.Party => txtParty,
            ChatType.Private => txtPrivate,
            ChatType.Union => txtUnion,
            ChatType.Stall => txtStall,
            _ => null,
        };

        if (target == null)
            return;

        var line = $"[{DateTime.Now:HH:mm:ss}]\t({sender}): {message}{Environment.NewLine}";
        AppendPending(target, line);
    }

    public void AppendUniqueMessage(string message)
    {
        if (!_disposed)
            AppendPending(UniqueText, $"[{DateTime.Now:HH:mm:ss}]\t{message}{Environment.NewLine}");
    }

    private void AppendPending(RichTextBox target, string line)
    {
        lock (_pending)
        {
            if (_disposed)
                return;
            if (!_pending.TryGetValue(target, out var buffer))
                _pending[target] = buffer = new StringBuilder();

            buffer.Append(line);
            if (buffer.Length > MaximumCharacters)
                buffer.Remove(0, buffer.Length - RetainedCharacters);
        }
    }

    private void FlushPending()
    {
        if (!Visible || !Enabled || IsDisposed || FindForm()?.WindowState == FormWindowState.Minimized)
            return;

        if (Interlocked.Exchange(ref _settingsDirty, 0) != 0)
            txtRecievePrivate.Text = PlayerConfig.Get<string>("RSBot.Chat.LastWhisper");

        List<KeyValuePair<RichTextBox, string>> batch;
        lock (_pending)
        {
            if (_pending.Count == 0)
                return;

            batch = new List<KeyValuePair<RichTextBox, string>>(_pending.Count);
            foreach (var entry in _pending)
                if (entry.Key.Visible)
                    batch.Add(new(entry.Key, entry.Value.ToString()));
            foreach (var entry in batch)
                _pending.Remove(entry.Key);
        }

        foreach (var (box, text) in batch)
        {
            box.AppendText(text);
            if (box.TextLength > MaximumCharacters)
            {
                // Cut at a line start, like the Log plugin
                var line = box.GetLineFromCharIndex(box.TextLength - RetainedCharacters);
                var removed = box.GetFirstCharIndexFromLine(line + 1);
                if (removed <= 0)
                    removed = box.TextLength - RetainedCharacters;

                box.Select(0, removed);
                box.SelectedText = string.Empty;
            }

            box.Select(box.TextLength, 0);
            box.ScrollToCaret();
        }
    }

    /// <summary>
    ///     The first event that will be fired after the player enters the game
    /// </summary>
    private void OnEnterGame()
    {
        Interlocked.Exchange(ref _settingsDirty, 1);
    }

    /// <summary>
    ///     Messages the preview key down.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The <see cref="PreviewKeyDownEventArgs" /> instance containing the event data.</param>
    private void MessagePreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
            return;
        SendChatMessage((Control)sender);
        ((Control)sender).ResetText();
    }
}
