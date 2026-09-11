using System;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

namespace TIM
{
#if UNITY_EDITOR
    [Icon("Assets/TIM/Console/Sprites/Console icon mini.png"), HideMonoScript]
#endif
    public class ConsoleConfigurator : MonoBehaviour
    {
        [FoldoutGroup("Links")] public Console Console;
        [FoldoutGroup("Links")] public TMP_Text ErrorsCount;
        [FoldoutGroup("Links")] public TMP_Text WarningsCount;
        [FoldoutGroup("Links")] public TMP_Text NetworkCount;
        [FoldoutGroup("Links")] public TMP_Text DefaultCount;
        [FoldoutGroup("Links")] public ConsoleToggle ErrorToggle;
        [FoldoutGroup("Links")] public ConsoleToggle WarningToggle;
        [FoldoutGroup("Links")] public ConsoleToggle NetworkToggle;
        [FoldoutGroup("Links")] public ConsoleToggle DefaultToggle;
        [FoldoutGroup("Links")] public ConsoleToggle AnimationToggle;
        [FoldoutGroup("Links")] public ConsoleToggle HiddenToggle;
        [FoldoutGroup("Links")] public ConsoleToggle CollapseToggle;
        [FoldoutGroup("Links")] public ConsoleToggle LogchainToggle;

        public static ConsoleConfigurator Instance => Console.Instance.ConsoleConfigurator;

        public bool IsCategoryActive(MessageType messageType)
        {
            switch (messageType)
            {
                case MessageType.Default: return DefaultToggle.Selected;
                case MessageType.Error: return ErrorToggle.Selected;
                case MessageType.Warning: return WarningToggle.Selected;
                case MessageType.Network: return NetworkToggle.Selected;
                default:
                    throw new ArgumentOutOfRangeException(nameof(messageType), messageType, null);
            }
        }


        public void OnMessageCountChanged()
        {
            ErrorsCount.text = ConsoleAlgorithms.GetCountString(Console.MessageCount_Error);
            WarningsCount.text = ConsoleAlgorithms.GetCountString(Console.MessageCount_Warning);
            NetworkCount.text = ConsoleAlgorithms.GetCountString(Console.MessageCount_Network);
            DefaultCount.text = ConsoleAlgorithms.GetCountString(Console.MessageCount_Default);
        }

        public void OnSelectedCategoriesChanged()
        {
            Console.MessagesContent.OnSelectedCategoriesChanged();
        }

        public void OnLogchainToggle()
        {
            Console.LogchainWindow.SetOpened(LogchainToggle.Selected);
        }

        public void OnCollapseToggle()
        {
            Console.MessagesContent.Repaint();
        }

        public void OnHiddenToggle()
        {
            Console.MessagesContent.Repaint();
        }
        
        public void OnAnimationsToggle()
        {
            
        }
    }
}