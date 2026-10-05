using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using RSBot.Core.Components;
using RSBot.Core.Components.Scripting;
using SDUI;
using SDUI.Controls;
using Label = SDUI.Controls.Label;
using Panel = System.Windows.Forms.Panel;
using TextBox = SDUI.Controls.TextBox;

namespace RSBot.Views.Dialog;

public partial class CommandDialog : UIWindowBase
{
    #region Members

    private readonly IScriptCommand _command;

    #endregion Members

    #region Constructor

    public CommandDialog(IScriptCommand command)
    {
        _command = command;
        Arguments = new Dictionary<string, string>(command.Arguments.Count);
        InitializeComponent();
        Text = $"Add command: {command.Name}";

        foreach (var arg in command.Arguments)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Top,
                BackColor = ColorScheme.BackColor,
                ForeColor = ColorScheme.ForeColor,
                Size = LogicalToDeviceUnits(new Size(250, 85)),
            };

            var input = new TextBox
            {
                Location = new Point(LogicalToDeviceUnits(16), LogicalToDeviceUnits(26)),
                Size = LogicalToDeviceUnits(new Size(200, 28)),
                Name = arg.Key,
            };

            input.TextChanged += Input_TextChanged;

            panel.Controls.AddRange(
                new Control[]
                {
                    new Label
                    {
                        Location = new Point(LogicalToDeviceUnits(13), LogicalToDeviceUnits(2)),
                        Text = arg.Key,
                        BackColor = Color.Transparent,
                    },
                    input,
                    new Label
                    {
                        Location = new Point(LogicalToDeviceUnits(13), LogicalToDeviceUnits(50)),
                        Text = arg.Value,
                        Size = LogicalToDeviceUnits(new Size(230, 20)),
                    },
                    new Separator { Dock = DockStyle.Bottom },
                }
            );

            Controls.Add(panel);
        }

        var count = command.Arguments.Count == 1 ? 1 : command.Arguments.Count;

        Size = LogicalToDeviceUnits(new Size(268, 85 + 85 * count));
    }

    #endregion Constructor

    #region Events

    private void Input_TextChanged(object sender, EventArgs e)
    {
        if (sender is TextBox textBox)
            Arguments[textBox.Name] = textBox.Text;
    }

    #endregion Events

    #region Properties

    public Dictionary<string, string> Arguments { get; }

    /// <summary>
    ///     Gets the command text.
    /// </summary>
    /// <value>
    ///     The command text.
    /// </value>
    public string CommandText
    {
        get
        {
            return Arguments.Aggregate(
                _command.Name,
                (current, arg) => current + ScriptManager.ArgumentSeparator + arg.Value
            );
        }
    }

    #endregion Properties
}
