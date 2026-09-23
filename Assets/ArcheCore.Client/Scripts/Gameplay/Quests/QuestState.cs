using System;
using System.Collections.Generic;
using ArcheCore.Network.Shared.Packets.W2C;

namespace ArcheCore.Client.Gameplay.Quests
{
    /// <summary>
    /// The client's copy of quests: the catalogue (every quest's text and
    /// objectives, sent once) and this character's state (what's accepted and
    /// how far along). Static, like LocalCharacterState, and for the same
    /// reason - it arrives before any UI exists to hear it.
    ///
    /// Having the whole catalogue locally is what lets the log, the tracker,
    /// the giver window and the "!" markers all be drawn without asking the
    /// server anything.
    /// </summary>
    public static class QuestState
    {
        private static readonly Dictionary<int, QuestDefinitionData> Definitions = new();
        private static readonly Dictionary<int, QuestProgressData> Progress = new();

        /// <summary>Anything changed - the log, tracker and markers redraw.</summary>
        public static event Action Changed;

        public static IReadOnlyDictionary<int, QuestProgressData> Active => Progress;

        public static void Reset()
        {
            Definitions.Clear();
            Progress.Clear();
            Changed?.Invoke();
        }

        public static void SetCatalog(QuestDefinitionData[] quests)
        {
            Definitions.Clear();
            if (quests != null)
                foreach (var quest in quests)
                    Definitions[quest.Id] = quest;

            Changed?.Invoke();
        }

        public static void SetLog(QuestProgressData[] quests)
        {
            Progress.Clear();
            if (quests != null)
                foreach (var quest in quests)
                    Progress[quest.QuestId] = quest;

            Changed?.Invoke();
        }

        public static void Apply(QuestProgressData quest, bool removed)
        {
            if (quest == null) return;

            if (removed) Progress.Remove(quest.QuestId);
            else Progress[quest.QuestId] = quest;

            Changed?.Invoke();
        }

        public static bool TryGetDefinition(int questId, out QuestDefinitionData definition) =>
            Definitions.TryGetValue(questId, out definition);

        public static QuestStatus StatusOf(int questId) =>
            Progress.TryGetValue(questId, out var p) ? (QuestStatus)p.Status : QuestStatus.None;

        /// <summary>Counts for this quest, or an empty array if it isn't in the log.</summary>
        public static int[] CountsOf(int questId) =>
            Progress.TryGetValue(questId, out var p) && p.Counts != null ? p.Counts : Array.Empty<int>();

        /// <summary>Quests in the log that aren't handed in yet, for the tracker.</summary>
        public static IEnumerable<QuestProgressData> Tracked()
        {
            foreach (var quest in Progress.Values)
                if ((QuestStatus)quest.Status is QuestStatus.Active or QuestStatus.Complete)
                    yield return quest;
        }

        /// <summary>
        /// What to float over an NPC: "?" when something can be handed in to
        /// them, "!" when they have something to give, nothing otherwise.
        ///
        /// Worked out from the catalogue and our own state, so it costs no
        /// packets. It can be optimistic - the catalogue doesn't carry quest
        /// chains, so a "!" may appear for a quest whose prerequisite isn't
        /// done. Talking to them shows the truth; the server decides what is
        /// actually offered.
        /// </summary>
        public static string MarkerFor(int npcTemplateId, int playerLevel)
        {
            bool available = false;

            foreach (var quest in Definitions.Values)
            {
                var status = StatusOf(quest.Id);

                if (quest.TurnInNpcTemplateId == npcTemplateId && status == QuestStatus.Complete)
                    return "?";   // ready to hand in beats everything

                if (quest.GiverNpcTemplateId != npcTemplateId || status != QuestStatus.None)
                    continue;

                if (playerLevel > 0 && playerLevel < quest.MinLevel)
                    continue;

                available = true;
            }

            return available ? "!" : null;
        }
    }
}
