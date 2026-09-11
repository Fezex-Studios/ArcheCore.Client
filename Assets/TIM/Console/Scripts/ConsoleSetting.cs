using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace TIM
{
    public class ConsoleSetting : ScriptableObject
    {
        [BoxGroup("0", false), ToggleLeft] public bool Enabled = true;
        [Tooltip("Console will work in build only if you check 'DevelopmentBuild' in BuildSettings")]
        [BoxGroup("0", false), ToggleLeft, ShowIf(nameof(Enabled))] public bool EnabledInDevelopmentBuildOnly = true;
        [Tooltip("messages from Debug.Log(), print(), exceptions etc will be displayed")]
        [BoxGroup("0", false), ToggleLeft, ShowIf(nameof(Enabled))] public bool CopyDebugLogs = true;
        [Tooltip("Disable if you don't need nice audio of Console")]
        [BoxGroup("0", false), ToggleLeft, ShowIf(nameof(Enabled))] public bool AudioEnabled = true;
        [Tooltip("If you don't need Console's EventSystem, if you have yours, you can disable it.")]
        [BoxGroup("0", false), ToggleLeft, ShowIf(nameof(Enabled))] public bool EventSystemEnabled = true;

        [BoxGroup("Parameters"), LabelWidth(150)] public KeyCode ToggleKey = KeyCode.BackQuote;
        [BoxGroup("Parameters"), LabelWidth(150)] public int MaxTitleLength = 3000;
        [BoxGroup("Parameters"), LabelWidth(150)] public int MaxStacktraceLength = 3000;
        
        [Title("Stacktrace", "Highlights file paths and code paths in stacktrace.")]
        [FoldoutGroup("Colors"), LabelWidth(180)] public Color StacktraceDefaultColor = Color.white;
        [FoldoutGroup("Colors"), LabelWidth(180)] public Color StacktraceFilePathColor = Color.cyan;
        [FoldoutGroup("Colors"), LabelWidth(180)] public Color StacktraceCodePathColor = Color.yellow;
        
        [Title("Messages", "You can't change it for now.")]
        [FoldoutGroup("Colors"), DisableIf("@true")] public Color DefaultColor = Color.white;
        [FoldoutGroup("Colors"), DisableIf("@true")] public Color ErrorColor = Color.red;
        [FoldoutGroup("Colors"), DisableIf("@true")] public Color WarningColor = Color.yellow;
        [FoldoutGroup("Colors"), DisableIf("@true")] public Color NetworkColor = new Color(0, 0.5f, 1);
        
        [FoldoutGroup("Links")] public Console ConsoleCanvasPrefab;
        [FoldoutGroup("Links")] public Sprite ErrorMessageIcon;
        [FoldoutGroup("Links")] public Sprite WarningMessageIcon;
        [FoldoutGroup("Links")] public Sprite DefaultMessageIcon;
        [FoldoutGroup("Links")] public Sprite NetworkMessageIcon;

        public Sprite GetIcon(MessageType messageType)
        {
            switch (messageType)
            {
                case MessageType.Default: return DefaultMessageIcon;
                case MessageType.Error: return ErrorMessageIcon;
                case MessageType.Warning: return WarningMessageIcon;
                case MessageType.Network: return NetworkMessageIcon;
                default:
                    throw new ArgumentOutOfRangeException(nameof(messageType), messageType, null);
            }
        }
        
        public Color GetColor(MessageType messageType)
        {
            switch (messageType)
            {
                case MessageType.Default: return DefaultColor;
                case MessageType.Error: return ErrorColor;
                case MessageType.Warning: return WarningColor;
                case MessageType.Network: return NetworkColor;
                default:
                    throw new ArgumentOutOfRangeException(nameof(messageType), messageType, null);
            }
        }

        public Color GetBgColor(MessageType messageType, bool highlight = false)
        {
            Color c = GetColor(messageType);
            c.a = highlight ? 25 / 255f : 5 / 255f;
            return c;
        }

        #region Static realization

        public static ConsoleSetting Instance
        {
            get
            {
                if (!_instance)
                {
                    _instance = Resources.Load<ConsoleSetting>("TIM/Console Setting");
                }

                return _instance;
            }
        }

        private static ConsoleSetting _instance;

        #endregion
    }
}