using System.Windows.Forms;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Plugins;
using RSBot.Python.Components;

namespace RSBot.Python;

public class Bootstrap : IPlugin
{
    /// <inheritdoc />
    public string Author => "RSBot Team";

    /// <inheritdoc />
    public string Description =>
        "Runs Python plugins from Data\\Python\\Plugins. Plugins can read the game state, react to bot events and packets, add their own tab and add commands to walk scripts.";

    /// <inheritdoc />
    public string Name => "RSBot.Python";

    /// <inheritdoc />
    public string Title => "Python";

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public bool Enabled { get; set; }

    /// <inheritdoc />
    public bool DisplayAsTab => true;

    /// <inheritdoc />
    public int Index => 97;

    /// <inheritdoc />
    public bool RequireIngame => false;

    /// <inheritdoc />
    public void Initialize()
    {
        if (!Enabled)
            return;

        // Created here, on the UI thread, before the Python thread can log to it
        var view = Views.View.Instance;

        PythonWorker.Start();
        PythonWorker.Post(() =>
        {
            PythonPluginManager.Initialize();
            view.RefreshPlugins();

            if (Game.Player != null)
                PythonPluginManager.LoadEnabledPlugins();
        });
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
        // Enabled plugins are loaded from the OnLoadCharacter event
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
