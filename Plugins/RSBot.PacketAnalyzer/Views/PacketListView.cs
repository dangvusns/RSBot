using System.ComponentModel;
using System.Windows.Forms;

namespace RSBot.PacketAnalyzer.Views;

/// <summary>
///     A double buffered virtual list, SDUI's list view sorts items which is not allowed in virtual mode.
/// </summary>
[ToolboxItem(false)]
internal class PacketListView : ListView
{
    public PacketListView()
    {
        DoubleBuffered = true;
        VirtualMode = true;
        View = System.Windows.Forms.View.Details;
        FullRowSelect = true;
        HideSelection = false;
        MultiSelect = false;
    }
}
