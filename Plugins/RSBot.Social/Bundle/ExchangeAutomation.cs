using System;
using System.Linq;
using RSBot.Core;
using RSBot.Core.Event;
using RSBot.Core.Objects;

namespace RSBot.Social.Bundle;

/// <summary>
///     How exchange invitations are answered.
/// </summary>
internal enum ExchangeMode
{
    Manual = 0,
    Refuse = 1,
    AcceptAll = 2,
    AcceptFromList = 3,
}

/// <summary>
///     Answers exchange invitations and confirms / approves exchanges automatically, like the following sequence of
///     the game: both sides confirm (lock), then both approve.
/// </summary>
internal static class ExchangeAutomation
{
    internal const string ModeKey = "RSBot.Social.Exchange.Mode";
    internal const string AutoConfirmKey = "RSBot.Social.Exchange.AutoConfirm";
    internal const string AutoApproveKey = "RSBot.Social.Exchange.AutoApprove";

    /// <summary>
    ///     The commander list of the Party plugin ("Listen commands in list").
    /// </summary>
    private const string CommanderListKey = "RSBot.Party.Commands.PlayersList";

    private static bool _initialized;
    private static volatile bool _partnerConfirmed;
    private static volatile bool _selfConfirmed;
    private static volatile bool _approved;

    /// <summary>
    ///     Subscribes the game events.
    /// </summary>
    internal static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;

        EventManager.SubscribeEvent("OnExchangeRequest", OnExchangeRequest);
        EventManager.SubscribeEvent("OnExchangePartnerConfirmed", OnPartnerConfirmed);
        EventManager.SubscribeEvent("OnExchangeConfirmed", OnSelfConfirmed);
        EventManager.SubscribeEvent("OnCancelExchange", Reset);
        EventManager.SubscribeEvent("OnApproveExchange", Reset);
    }

    private static void OnExchangeRequest()
    {
        var request = Game.AcceptanceRequest;
        if (request == null || request.Type != InviteRequestType.Exchange)
            return;

        Reset();

        var name = request.Player?.Name;

        switch ((ExchangeMode)PlayerConfig.Get(ModeKey, (int)ExchangeMode.Manual))
        {
            case ExchangeMode.Refuse:
                request.Refuse();
                Log.Notify($"[Social] Exchange invitation from {name} refused");
                break;

            case ExchangeMode.AcceptAll:
                request.Accept();
                Log.Notify($"[Social] Exchange invitation from {name} accepted");
                break;

            case ExchangeMode.AcceptFromList:
                var list = PlayerConfig.GetArray<string>(CommanderListKey);
                if (name == null || list == null || !list.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                    return;

                request.Accept();
                Log.Notify($"[Social] Exchange invitation from {name} accepted (commander list)");
                break;
        }
    }

    private static void OnPartnerConfirmed()
    {
        _partnerConfirmed = true;

        if (!_selfConfirmed && PlayerConfig.Get(AutoConfirmKey, false))
            Game.Player?.Exchange?.Confirm();
        else
            TryApprove();
    }

    private static void OnSelfConfirmed()
    {
        _selfConfirmed = true;

        TryApprove();
    }

    /// <summary>
    ///     Approves once both sides confirmed.
    /// </summary>
    private static void TryApprove()
    {
        if (_approved || !_partnerConfirmed || !_selfConfirmed || !PlayerConfig.Get(AutoApproveKey, false))
            return;

        _approved = true;
        Game.Player?.Exchange?.Approve();
    }

    private static void Reset()
    {
        _partnerConfirmed = false;
        _selfConfirmed = false;
        _approved = false;
    }
}
