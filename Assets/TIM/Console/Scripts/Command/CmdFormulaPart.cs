using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace TIM
{
    [System.Serializable]
    public class CmdFormulaPart
    {
        [HorizontalGroup("h", 150)]
        [LabelWidth(40)] public CmdPartType Type;

        [HorizontalGroup("h")]
        [InfoBox("@"+nameof(GetSentenceSpaceContainsError), InfoMessageType.Error, VisibleIf = nameof(ContainsSpace))]
        [Tooltip("ex: 'Aimbot' or 'InfiniteHealth'")]
        [ShowIf("@Type == CmdPartType.Sentence"), LabelWidth(70)] public string Sentence = "";
        
        [HorizontalGroup("h")]
        [Tooltip("ex: 'Color' or 'Weapon'")]
        [ShowIf("@Type != CmdPartType.Sentence"), LabelWidth(40)] public string Title = "";
        
        [Tooltip("ex: 'Red', 'Green', 'Blue' or 'AK-47', 'Deagle', 'AWP'")]
        [InfoBox("@"+nameof(GetEnumSpaceContainsError), InfoMessageType.Error, VisibleIf = nameof(ContainsSpace))]
        [ShowIf("@Type == CmdPartType.Enum")] public string[] EnumVariants = {"Variant-1", "Variant-2"};

        [HideInInspector] public CmdFormula Formula;
        
        public string GetPreviewString(bool coloring, bool underline)
        {
            string s = GetString();
            if (underline)
                s = "<u>" + s + "</u>";

            return s;

            // functions:
            string GetString()
            {
                switch (Type)
                {
                    case CmdPartType.Sentence:  return Sentence ?? "";
                    case CmdPartType.String:    return $"[{GetColored("string")}: " +(Title ?? "")+ "]";
                    case CmdPartType.Enum:      return $"[{GetColored("enum")}: " +(Title ?? "")+ "]";
                    case CmdPartType.Bool:      return $"[{GetColored("bool")}: " +(Title ?? "")+ " ]";
                    case CmdPartType.Integer:   return $"[{GetColored("int")}: " +(Title ?? "")+ "]";
                    case CmdPartType.Float:     return $"[{GetColored("float")}: " +(Title ?? "")+ "]";
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            string GetColored(string str, int variation = 0)
            {
                if (!coloring)
                    return str;
                
                if (variation == 0) // type name
                    return "<color=#0aebf5>"+str+"</color>";

                return str;
            }
        }
        
        private bool ContainsSpace()
        {
            if (Formula == null)
                return false;

            if (Type == CmdPartType.Sentence)
            {
                return Sentence.Contains(Formula.GetSpaceString());
            }
            else if (Type == CmdPartType.Enum)
            {
                if (EnumVariants == null)
                    return false;
                
                foreach (string enumVariant in EnumVariants)
                {
                    if (enumVariant.Contains(Formula.GetSpaceString()))
                        return true;
                }
            }
            
            return false;
        }

        private string GetSentenceSpaceContainsError => Formula == null ? "" : "Sentence can not contain space symbol '" + Formula.GetSpaceString() + "' from Formula!";
        private string GetEnumSpaceContainsError => Formula == null ? "" : "Enum elements can not contain space symbol '" + Formula.GetSpaceString() + "' from Formula!";
        
        public CmdFormulaPart(){}

        public CmdFormulaPart(string sentence)
        {
            Sentence = sentence;
        }

        public CmdFormulaPart(string title, string[] enumVariants)
        {
            Title = title;
            EnumVariants = enumVariants;
        }
        
        public CmdFormulaPart(CmdPartType partType, string title)
        {
            Type = partType;
            Title = title;
        }
    }
}