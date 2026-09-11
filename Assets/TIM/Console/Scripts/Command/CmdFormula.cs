using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;

namespace TIM
{
    [System.Serializable]
    public class CmdFormula
    {
        [BoxGroup("Preview"), HideLabel, ShowInInspector, PropertyOrder(-1), EnableGUI, DisplayAsString, GUIColor(0,1,0)]
        public string Preview
        {
            get => GetPreview(false);
        }
        [BoxGroup, EnumToggleButtons, LabelWidth(100)] public CmdSpaceType SpaceType;
        public List<CmdFormulaPart> Parts = new List<CmdFormulaPart>() {new CmdFormulaPart("Command"), new CmdFormulaPart(CmdPartType.Bool, "state")};
        
        public string GetSpaceString()
        {
            switch (SpaceType)
            {
                case CmdSpaceType.Space: return " ";
                case CmdSpaceType.Underline: return "_";
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        [OnInspectorInit]
        private void OnInspectorInit()
        {
            foreach (CmdFormulaPart part in Parts)
            {
                part.Formula = this;
            }
        }

        /// <summary>
        /// Get preview string of formula
        /// </summary>
        /// <param name="colored">If you need to have colored symbols with Rich text. You can set it to true.</param>
        /// <returns></returns>
        public string GetPreview(bool colored)
        {
            if (Parts == null || Parts.Count == 0)
                return "";
                                              
            string result = "";
            for (var i = 0; i < Parts.Count; i++)
            {
                result += Parts[i].GetPreviewString(colored, false);
                if (i < Parts.Count - 1)
                    result += GetSpaceString();
            }
                              
            return result;
        }
    }
}