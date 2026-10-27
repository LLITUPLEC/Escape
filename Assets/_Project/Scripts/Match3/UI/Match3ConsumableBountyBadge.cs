using Project.Character;
using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>Display of the server-frozen PvE scroll window; never changes rewards.</summary>
    public sealed class Match3ConsumableBountyBadge : MonoBehaviour
    {
        private Image _panel;private TMP_Text _label;private long _ends,_sample;private float _received,_next;
        public static Match3ConsumableBountyBadge Ensure(Match3PlayerPanel player)
        {
            if(player==null)return null;
            var canvas=player.GetComponentInParent<Canvas>()?.rootCanvas;if(canvas==null)return null;
            var view=ExpeditionUi.Ensure<Match3ConsumableBountyBadge>(canvas.gameObject);
            if(view._panel==null)
            {
                var parent=canvas.transform.Find("ExpeditionBattleSafeArea")??canvas.transform;
                view._panel=ExpeditionUi.Panel(parent,"ExpeditionBountyBadge",.30f,.737f,.70f,.767f,ExpeditionTheme.Panel);
                view._panel.pixelsPerUnitMultiplier=7;view._panel.raycastTarget=false;
                var icon=ExpeditionUi.Fill(view._panel.transform,"ScrollIcon",.015f,.12f,.12f,.88f,Color.white);
                icon.sprite=ConsumablePresentation.Icon(ConsumablePresentation.Bounty);icon.preserveAspect=true;
                view._label=ExpeditionUi.Text(view._panel.transform,"BountyTime","",22,.13f,.07f,.98f,.93f,TextAlignmentOptions.Center);
                view._label.color=ExpeditionTheme.Amber;view._label.enableAutoSizing=true;view._label.fontSizeMin=15;view._label.fontSizeMax=22;
                view._panel.gameObject.SetActive(false);
            }
            return view;
        }
        public void Bind(long ends,long serverNow)
        {if(_ends!=ends||_sample!=serverNow){_ends=ends;_sample=serverNow;_received=Time.realtimeSinceStartup;}Refresh();}
        private void Update(){if(Time.unscaledTime>=_next){_next=Time.unscaledTime+1;Refresh();}}
        private void Refresh()
        {
            if(_panel==null)return;
            var remaining=System.Math.Max(0,_ends-_sample-(long)(Time.realtimeSinceStartup-_received));
            _panel.gameObject.SetActive(_ends>0&&remaining>0);
            if(remaining>0)_label.text="Добыча PvE ×1,5 · "+(remaining/60)+":"+(remaining%60).ToString("00");
        }
    }
}
