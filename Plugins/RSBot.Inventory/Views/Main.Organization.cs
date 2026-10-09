using System.Drawing;
using System.Windows.Forms;
using RSBot.Core;
using Button = SDUI.Controls.Button;

namespace RSBot.Inventory.Views;

public partial class Main
{
    private Button _organizeButton;

    private void InitializeOrganizationAction()
    {
        _organizeButton = new Button
        {
            Name = "buttonOrganizeInventory",
            Text = "Organize",
            AutoSize = true,
            MinimumSize = new Size(LogicalToDeviceUnits(85), btnSort.Height),
            Color = btnSort.Color,
            ForeColor = btnSort.ForeColor,
            Radius = btnSort.Radius,
            TabIndex = 8,
        };
        _organizeButton.Click += async (_, _) =>
        {
            var inventory = Game.Player?.Inventory;
            _organizeButton.Enabled = false;
            try
            {
                await RunItemActionAsync(() => inventory?.Organize());
            }
            finally
            {
                if (!IsDisposed) _organizeButton.Enabled = true;
            }
        };
        var actions = new FlowLayoutPanel
        {
            Name = "inventoryActions",
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            BackColor = Color.Transparent,
        };
        actions.Controls.Add(checkAutoSort);
        actions.Controls.Add(btnSort);
        actions.Controls.Add(_organizeButton);
        panel1.Controls.Add(actions);
    }
}
