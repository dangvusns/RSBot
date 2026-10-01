namespace RSBot.Social.Views;

internal class View
{
    private static Main _instance;

    /// <summary>
    ///     Gets the instance.
    /// </summary>
    public static Main Instance => _instance ??= new();
}
