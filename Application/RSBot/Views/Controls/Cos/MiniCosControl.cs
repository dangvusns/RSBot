using System;
using System.ComponentModel;
using System.Drawing;
using SDUI.Controls;

namespace RSBot.Views.Controls;

[ToolboxItem(false)]
public partial class MiniCosControl : DoubleBufferedControl
{
    private bool _selected;

    public MiniCosControl()
    {
        InitializeComponent();

        // Laid out in 96 DPI pixels without an AutoScaleMode and added at runtime.
        var factor = DeviceDpi / 96f;
        if (factor != 1f)
            Scale(new SizeF(factor, factor));
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            panel.BorderColor = value ? Color.Yellow : Color.Transparent;
        }
    }

    private void OnClick_Redirector(object sender, EventArgs e)
    {
        OnClick(e);
    }
}
