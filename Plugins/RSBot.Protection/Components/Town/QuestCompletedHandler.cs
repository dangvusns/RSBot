using System;
using RSBot.Core;
using RSBot.Core.Event;

namespace RSBot.Protection.Components.Town;

public class QuestCompletedHandler : AbstractTownHandler
{
    /// <summary>
    ///     Initializes this instance.
    /// </summary>
    public static void Initialize()
    {
        SubscribeEvents();
    }

    /// <summary>
    ///     Subscribes the events.
    /// </summary>
    private static void SubscribeEvents()
    {
        EventManager.SubscribeEvent("OnQuestObjectivesCompleted", new Action<uint>(OnQuestObjectivesCompleted));
    }

    /// <summary>
    /// </summary>
    /// <param name="questId">The quest identifier.</param>
    private static void OnQuestObjectivesCompleted(uint questId)
    {
        if (!Kernel.Bot.Running)
            return;

        if (!PlayerConfig.Get<bool>("RSBot.Protection.checkQuestCompleted"))
            return;

        if (PlayerInTownScriptRegion())
            return;

        var quest = Game.ReferenceManager.GetRefQuest(questId);

        Log.NotifyLang("ReturnToTownQuestCompleted", quest?.GetTranslatedName() ?? questId.ToString());
        Game.Player.UseReturnScroll();
    }
}
