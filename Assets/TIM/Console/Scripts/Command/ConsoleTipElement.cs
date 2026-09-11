using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

namespace TIM
{
#if UNITY_EDITOR
    [Icon("Assets/TIM/Console/Sprites/Console icon mini.png"), HideMonoScript]
#endif
    public class ConsoleTipElement : ConsoleButton
    {
        [BoxGroup, SerializeField] private TMP_Text _text;
        
        public CmdInputResult CmdInputResult { private set; get; }
        public int Index { private set; get; }
        public ConsoleTipMaster TipMaster { private set; get; }

        public void Init(CmdInputResult cmdInputResult, int index, ConsoleTipMaster tipMaster)
        {
            CmdInputResult = cmdInputResult;
            Index = index;
            TipMaster = tipMaster;
            _text.text = cmdInputResult.GetColoredTipText();
        }

        protected override void OnHighlight(bool highlight)
        {
            if(highlight)
                TipMaster.HighlightElement(Index);
        }

        protected override void OnClick()
        {
            Console.Instance.InputPanel.OnEnterPressed();
        }
    }
}