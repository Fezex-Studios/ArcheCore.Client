using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

namespace TIM
{
#if UNITY_EDITOR
    [Icon("Assets/TIM/Console/Sprites/Console icon mini.png"), HideMonoScript]
#endif
    public class CommandsListElement : MonoBehaviour
    {
        [SerializeField] private TMP_Text _text;
        
        public CmdFormula Formula { private set; get; }
        
        public void Init(CmdFormula formula)
        {
            Formula = formula;
            RefreshVisuals();
        }

        public void RefreshVisuals()
        {
            _text.text = Formula.GetPreview(true);
        }
    }
}