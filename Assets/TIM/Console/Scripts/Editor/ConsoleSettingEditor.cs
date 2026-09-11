#if UNITY_EDITOR

using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using TIM;
using UnityEditor;
using UnityEngine;

namespace TIM.Editor
{
    public class ConsoleSettingEditor : OdinEditorWindow
    {
        [MenuItem("Tools/TIM.Console")]
        private static void ShowWindow()
        {
            var window = GetWindow<ConsoleSettingEditor>();
            window.titleContent = new GUIContent("TIM.Console", AssetDatabase.LoadAssetAtPath<Texture>("Assets/TIM/Console/Sprites/Console icon mini.png"));
            window.Show();
        }
        
        [ShowInInspector, EnableGUI]
        [InlineEditor(InlineEditorModes.GUIOnly, InlineEditorObjectFieldModes.Hidden)]
        [PropertySpace(15), HideLabel] private object Setting => ConsoleSetting.Instance;
    }
}

#endif