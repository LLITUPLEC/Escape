using System;
using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Character.UI
{
    /// <summary>Concrete consumable preview/confirmation. Its callback owns the existing scoped service call.</summary>
    public sealed class ConsumableUseModalView : MonoBehaviour
    {
        private Image _icon;
        private TMP_Text _title,_effects,_explanation,_state;
        private Button _confirm,_cancel;
        private Action _submit;
        private bool _submitted;
        public Button ConfirmButton=>_confirm;
        public RectTransform PanelRect{get;private set;}
        public static ConsumableUseModalView Create(Transform parent)
        {
            var root=ExpeditionUi.Rect(parent,"ConsumableUseConfirmation",0,0,1,1);
            var view=root.gameObject.AddComponent<ConsumableUseModalView>();view.Build();root.gameObject.SetActive(false);return view;
        }
        private void Build()
        {
            PanelRect=ExpeditionUi.Panel(transform,"Panel",.07f,.235f,.93f,.765f,ExpeditionTheme.Panel).rectTransform;
            PanelRect.gameObject.AddComponent<Button>().transition=Selectable.Transition.None;
            _title=ExpeditionUi.Text(PanelRect,"ConsumableTitle","",39,.07f,.88f,.93f,.96f,TextAlignmentOptions.Center);
            _icon=ExpeditionUi.Fill(PanelRect,"ConsumableIcon",.08f,.60f,.37f,.86f,Color.white);_icon.preserveAspect=true;
            _effects=ExpeditionUi.Text(PanelRect,"ConsumableEffects","",35,.40f,.61f,.93f,.85f,TextAlignmentOptions.MidlineLeft);_effects.color=ExpeditionTheme.Amber;
            _explanation=ExpeditionUi.Text(PanelRect,"ConsumableExplanation","",27,.08f,.32f,.92f,.58f,TextAlignmentOptions.TopLeft);
            _state=ExpeditionUi.Text(PanelRect,"ConsumableState","",25,.08f,.22f,.92f,.32f,TextAlignmentOptions.Center);_state.color=ExpeditionTheme.Amber;
            _confirm=ExpeditionUi.Button(PanelRect,"ConfirmUseConsumable","Использовать 1 предмет",.08f,.115f,.92f,.205f,Confirm,true,32);
            _cancel=ExpeditionUi.Button(PanelRect,"CancelUseConsumable","Отмена",.25f,.035f,.75f,.10f,()=>gameObject.SetActive(false),false,29);
            foreach(var text in new[]{_title,_effects,_explanation,_state}){text.enableAutoSizing=true;text.fontSizeMin=text.fontSize*.8f;text.fontSizeMax=text.fontSize;text.raycastTarget=false;text.overflowMode=TextOverflowModes.Ellipsis;}
        }
        public void Show(ItemDefinition item,ConsumableStatus status,Action confirm,Action cancel)
        {
            _title.text=item.DisplayName;_icon.sprite=item.Icon;_effects.text=ConsumablePresentation.Effects(item.ItemId);
            _explanation.text=ConsumablePresentation.Explanation(item.ItemId);
            var blocked=ConsumablePresentation.BlockedReason(item.ItemId,status);
            _state.text=blocked??"Сейчас спишется один предмет из этой ячейки.";
            _submit=confirm;_submitted=false;_confirm.interactable=blocked==null;_cancel.interactable=true;
            ExpeditionUi.Label(_confirm,"Использовать 1 предмет");
            _cancel.onClick.RemoveAllListeners();_cancel.onClick.AddListener(()=>cancel?.Invoke());
            gameObject.SetActive(true);transform.SetAsLastSibling();
        }
        private void Confirm(){if(_submitted||!_confirm.interactable)return;_submitted=true;SetPending();_submit?.Invoke();}
        public void SetPending(){_confirm.interactable=false;_cancel.interactable=false;_state.text="Подтверждение активации…";}
        public void ShowError(string error)
        {
            _submitted=false;_state.text=ConsumablePresentation.Error(error);_cancel.interactable=true;
            _confirm.interactable=error!="consumable_already_armed"&&error!="bounty_already_active"&&error!="inventory_changed"&&error!="bad_use_sequence"&&error!="sequence_changed"&&error!="request_compacted"&&error!="request_id_reused";
            ExpeditionUi.Label(_confirm,"Проверить / повторить");
        }
    }
}
