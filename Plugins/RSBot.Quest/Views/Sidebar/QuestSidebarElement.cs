using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using RSBot.Core;
using RSBot.Core.Event;
using SDUI.Controls;

namespace RSBot.Quest.Views.Sidebar;

public partial class QuestSidebarElement : DoubleBufferedControl
{
    private readonly UiEventSubscriptions _uiEvents;

    private List<uint> TrackedQuests = new(4);

    public QuestSidebarElement()
    {

        InitializeComponent();
        _uiEvents = new UiEventSubscriptions(this);
        SubscribeEvents();
        Visible = false;
    }

    private void SubscribeEvents()
    {
        _uiEvents.Subscribe("OnUpdateQuests", RefreshQuests, coalesce: true);
        _uiEvents.Subscribe("OnLoadCharacter", RefreshQuests);
    }

    public void AddQuest(uint questId)
    {
        if (HasQuest(questId))
            return;

        if (TrackedQuests.Count >= 4 && !TrackedQuests.Contains(questId))
        {
            MessageBox.Show(
                "You can only track a maximum of 4 quests at a time",
                "Track quest",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );

            return;
        }

        TrackedQuests = PlayerConfig.GetArray<uint>("RSBot.QuestLog.TrackedQuests").ToList();
        if (!TrackedQuests.Contains(questId))
            TrackedQuests.Add(questId);

        var questItem = CreateQuestItem(questId);

        PlayerConfig.SetArray("RSBot.QuestLog.TrackedQuests", TrackedQuests);

        pQuests.Controls.Add(questItem);

        Refresh();
    }

    public void RemoveQuest(uint questId)
    {
        TrackedQuests = PlayerConfig.GetArray<uint>("RSBot.QuestLog.TrackedQuests").ToList();

        var removed = pQuests.Controls[questId.ToString()];
        pQuests.Controls.RemoveByKey(questId.ToString());
        removed?.Dispose();

        TrackedQuests.Remove(questId);

        PlayerConfig.SetArray("RSBot.QuestLog.TrackedQuests", TrackedQuests);

        Refresh();
    }

    public void RefreshQuests()
    {
        if (Game.Player?.QuestLog == null)
        {
            Visible = false;

            return;
        }

        TrackedQuests = PlayerConfig.GetArray<uint>("RSBot.QuestLog.TrackedQuests").ToList();

        lock (TrackedQuests)
        {
            var toRemove = new List<uint>();
            var toAdd = new List<uint>();

            foreach (var questId in TrackedQuests)
            {
                if (!Game.Player.QuestLog.ActiveQuests.TryGetValue(questId, out _))
                {
                    toRemove.Add(questId);

                    continue;
                }

                if (!TryGetQuestItem(questId, out var questItem))
                {
                    toAdd.Add(questId);

                    continue;
                }

                questItem!.Refresh();
            }

            foreach (var item in toRemove)
            {
                var removed = pQuests.Controls[item.ToString()];
                pQuests.Controls.RemoveByKey(item.ToString());
                removed?.Dispose();
                TrackedQuests.Remove(item);
            }
            if (toRemove.Count > 0)
                PlayerConfig.SetArray("RSBot.QuestLog.TrackedQuests", TrackedQuests);

            foreach (var item in toAdd)
                pQuests.Controls.Add(CreateQuestItem(item));
        }

        Refresh();
    }

    private static QuestItem CreateQuestItem(uint questId) => new(questId)
    {
        Name = questId.ToString(),
        Dock = DockStyle.Top,
        Tag = questId,
    };

    private bool TryGetQuestItem(uint questId, out QuestItem? questItem)
    {
        try
        {
            questItem = (QuestItem)pQuests.Controls.Find(questId.ToString(), false).FirstOrDefault();

            return questItem != null;
        }
        catch
        {
            questItem = null;

            return false;
        }
    }

    public bool HasQuest(uint questId)
    {
        return TryGetQuestItem(questId, out _);
    }

    public override void Refresh()
    {
        base.Refresh();

        Visible = TrackedQuests.Count > 0;
    }
}
