using System.Windows.Forms;
using RSBot.Core;
using RSBot.Core.Plugins;
using RSBot.ManagerLink.Components;

namespace RSBot.ManagerLink;

public class ManagerLinkPlugin : IPlugin
{
    /// <inheritdoc />
    public string Author => "RSBot Team";

    /// <inheritdoc />
    public string Description => "Lets RSBot.Manager control this bot and read its status.";

    /// <inheritdoc />
    public string Name => "RSBot.ManagerLink";

    /// <inheritdoc />
    public string Title => "Manager link";

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public bool Enabled { get; set; }

    /// <inheritdoc />
    public bool DisplayAsTab => false;

    /// <inheritdoc />
    public int Index => 101;

    /// <inheritdoc />
    public bool RequireIngame => false;

    /// <inheritdoc />
    public Control View => null;

    /// <inheritdoc />
    public void Initialize()
    {
        StatusTracker.Initialize();

        if (Enabled)
            PipeServer.Start();

        Log.Notify($"[Manager link] Plugin initialized, pipe [{PipeServer.PipeName}]");
    }

    /// <inheritdoc />
    public void Translate()
    {
        // nothing to translate
    }

    /// <inheritdoc />
    public void OnLoadCharacter()
    {
        // do nothing
    }

    /// <inheritdoc />
    public void Enable()
    {
        PipeServer.Start();
    }

    /// <inheritdoc />
    public void Disable()
    {
        PipeServer.Stop();
    }
}
