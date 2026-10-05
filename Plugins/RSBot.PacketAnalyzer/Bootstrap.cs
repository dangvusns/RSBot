using System.Windows.Forms;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Plugins;
using RSBot.PacketAnalyzer.Components;

namespace RSBot.PacketAnalyzer;

public class Bootstrap : IPlugin
{
    /// <inheritdoc />
    public string Author => "RSBot Team";

    /// <inheritdoc />
    public string Description =>
        "Captures every packet exchanged between the client, the bot and the server. Packets can be inspected live, filtered, injected and recorded to a text log for debugging.";

    /// <inheritdoc />
    public string Name => "RSBot.PacketAnalyzer";

    /// <inheritdoc />
    public string Title => "Packet Analyzer";

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public bool Enabled { get; set; }

    /// <inheritdoc />
    public bool DisplayAsTab => true;

    /// <inheritdoc />
    public int Index => 98;

    /// <inheritdoc />
    public bool RequireIngame => false;

    /// <inheritdoc />
    public void Initialize()
    {
        PacketHub.Initialize();
    }

    /// <inheritdoc />
    public Control View => Views.View.Instance;

    /// <inheritdoc />
    public void Translate()
    {
        LanguageManager.Translate(View, Kernel.Language);
    }

    /// <inheritdoc />
    public void OnLoadCharacter()
    {
        // do nothing
    }

    /// <inheritdoc />
    public void Enable()
    {
        if (View != null)
            View.Enabled = true;
    }

    /// <inheritdoc />
    public void Disable()
    {
        if (View != null)
            View.Enabled = false;
    }
}
