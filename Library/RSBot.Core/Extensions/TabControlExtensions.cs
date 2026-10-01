using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace RSBot.Core.Extensions;

/// <summary>
///     Keeps tab captions readable after translation, font changes, and DPI changes.
/// </summary>
internal static class TabControlExtensions
{
    private static readonly ConditionalWeakTable<TabControl, HeaderSizing> Sizing = new();

    public static void AutoSizeHeaders(Control root)
    {
        if (root is TabControl tabs)
            Sizing.GetValue(tabs, control => new HeaderSizing(control)).Update();

        foreach (Control child in root.Controls)
            AutoSizeHeaders(child);
    }

    private sealed class HeaderSizing
    {
        private readonly TabControl _tabs;
        private readonly HashSet<TabPage> _pages = new();

        public HeaderSizing(TabControl tabs)
        {
            _tabs = tabs;
            // SDUI resets ItemSize during handle creation unless sizing is fixed.
            _tabs.SizeMode = TabSizeMode.Fixed;
            _tabs.HandleCreated += OnChanged;
            _tabs.FontChanged += OnChanged;
            _tabs.DpiChangedAfterParent += OnChanged;
            _tabs.ControlAdded += OnControlAdded;
            _tabs.ControlRemoved += OnControlRemoved;
            _tabs.Disposed += OnDisposed;
            foreach (TabPage page in _tabs.TabPages)
                Attach(page);
        }

        private void Attach(TabPage page)
        {
            if (!_pages.Add(page)) return;
            page.TextChanged += OnChanged;
            page.FontChanged += OnChanged;
        }

        private void Detach(TabPage page)
        {
            if (!_pages.Remove(page)) return;
            page.TextChanged -= OnChanged;
            page.FontChanged -= OnChanged;
        }

        private void OnControlAdded(object sender, ControlEventArgs e)
        {
            if (e.Control is TabPage page) Attach(page);
            Update();
        }

        private void OnControlRemoved(object sender, ControlEventArgs e)
        {
            if (e.Control is TabPage page) Detach(page);
            Update();
        }

        private void OnChanged(object sender, EventArgs e) => Update();

        private void OnDisposed(object sender, EventArgs e)
        {
            foreach (var page in new List<TabPage>(_pages)) Detach(page);
        }

        public void Update()
        {
            if (_tabs.IsDisposed || _tabs.Disposing || _tabs.TabCount == 0) return;
            var scale = _tabs.DeviceDpi / 96f;
            var padding = (int)Math.Ceiling(28 * scale);
            var width = (int)Math.Ceiling(80 * scale);
            var height = (int)Math.Ceiling(24 * scale);
            foreach (TabPage page in _tabs.TabPages)
            {
                // SDUI draws captions with the page font; include the native tab font too.
                var text = TextRenderer.MeasureText(page.Text, page.Font);
                var nativeText = TextRenderer.MeasureText(page.Text, _tabs.Font);
                var imageWidth = _tabs.ImageList != null && (page.ImageIndex >= 0 || !string.IsNullOrEmpty(page.ImageKey))
                    ? _tabs.ImageList.ImageSize.Width + padding / 2 : 0;
                width = Math.Max(width, Math.Max(text.Width, nativeText.Width) + padding + imageWidth);
                height = Math.Max(height, Math.Max(text.Height, nativeText.Height) + (int)Math.Ceiling(12 * scale));
            }
            var size = new Size(width, height);
            if (_tabs.ItemSize != size) _tabs.ItemSize = size;
        }
    }
}
