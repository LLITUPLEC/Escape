using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Character.UI
{
    /// <summary>Server snapshot + unscaled presentation countdown. No activation/stat mutation or RPC.</summary>
    public sealed class ConsumableStatusStrip : MonoBehaviour
    {
        private TMP_Text _armed, _bounty;
        private Image _armedIcon, _bountyIcon;
        private ConsumableStatus _status;
        private float _received, _next;
        public static ConsumableStatusStrip Ensure(Transform parent)
        {
            var root=parent.Find("ConsumableStatusStrip") as RectTransform;
            if(root==null)root=ExpeditionUi.Rect(parent,"ConsumableStatusStrip",.03f,.735f,.97f,.855f);
            var view=ExpeditionUi.Ensure<ConsumableStatusStrip>(root.gameObject);view.Build();return view;
        }
        private void Build()
        {
            if(_armed!=null)return;
            var bg=gameObject.GetComponent<Image>()??gameObject.AddComponent<Image>();bg.color=new Color(.025f,.08f,.10f,.88f);bg.raycastTarget=false;
            _armedIcon=ExpeditionUi.Fill(transform,"ArmedIcon",.012f,.13f,.083f,.87f,Color.white);_armedIcon.preserveAspect=true;
            _bountyIcon=ExpeditionUi.Fill(transform,"BountyIcon",.51f,.13f,.585f,.87f,Color.white);_bountyIcon.preserveAspect=true;
            _armed=ExpeditionUi.Text(transform,"ArmedStatus","",22,.095f,.07f,.49f,.93f,TextAlignmentOptions.MidlineLeft);
            _bounty=ExpeditionUi.Text(transform,"BountyStatus","",22,.60f,.07f,.99f,.93f,TextAlignmentOptions.MidlineLeft);
            foreach(var label in new[]{_armed,_bounty}){label.enableAutoSizing=true;label.fontSizeMin=16;label.fontSizeMax=22;label.color=ExpeditionTheme.Muted;label.raycastTarget=false;}
        }
        public void Bind(ConsumableStatus status){Build();_status=status;ConsumablePresentation.MarkReceived(status);_received=Time.realtimeSinceStartup;_next=0;Refresh();}
        private void Update(){if(Time.unscaledTime>=_next){_next=Time.unscaledTime+1;Refresh();}}
        private void Refresh()
        {
            if(_armed==null)return;
            var id=_status?.armed_item_id;
            _armedIcon.sprite=ConsumablePresentation.Icon(id);_armedIcon.enabled=_armedIcon.sprite!=null;
            _armed.text=string.IsNullOrEmpty(id)?"Эликсир не подготовлен":"<color=#EDAA53>"+ConsumablePresentation.Name(id)+"</color>\nСледующий PvE / Дуэль Pro";
            var remaining=_status==null?0:System.Math.Max(0,_status.bounty_ends_at-ConsumablePresentation.ServerNow(_status));
            var active=_status?.bounty_active==true&&remaining>0;
            _bountyIcon.sprite=ConsumablePresentation.Icon(ConsumablePresentation.Bounty);_bountyIcon.enabled=active;
            _bounty.text=active?"<color=#EDAA53>PvE: золото и руда ×1,5</color>\n"+(remaining/60)+":"+(remaining%60).ToString("00")+" до окончания":"Свиток не действует";
        }
    }
}
