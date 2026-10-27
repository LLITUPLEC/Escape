using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>UI presentation only. Never calculates or mutates battle stats and never sends a network action.</summary>
    [DisallowMultipleComponent]
    public sealed class Match3AbilityFx : MonoBehaviour
    {
        public const float FlightSeconds = .66f;
        public const float ImpactSeconds = .44f;
        private RectTransform _layer;
        private Image _projectile, _blastA, _blastB, _shield;
        private Image[] _sparks;
        private Coroutine _impactRoutine, _shieldRoutine;
        public bool PetardFlying { get; private set; }
        public bool ImpactIsActive { get; private set; }
        public int LaunchCount { get; private set; }
        public int ImpactCount { get; private set; }
        public int ShieldCount { get; private set; }
        public Vector2 LastFlightFrom { get; private set; }
        public Vector2 LastFlightTo { get; private set; }

        public static Match3AbilityFx Ensure(Match3PlayerPanel panel)
        {
            if (panel == null) return null;
            var canvas = panel.GetComponentInParent<Canvas>()?.rootCanvas;
            if (canvas == null) return null;
            var fx = canvas.GetComponent<Match3AbilityFx>();
            if (fx == null) fx = canvas.gameObject.AddComponent<Match3AbilityFx>();
            fx.Build(); return fx;
        }

        private void Build()
        {
            if (_layer != null) return;
            _layer = new GameObject("ExpeditionAbilityEffects", typeof(RectTransform)).GetComponent<RectTransform>();
            _layer.SetParent(transform, false); _layer.anchorMin = Vector2.zero; _layer.anchorMax = Vector2.one;
            _layer.offsetMin = _layer.offsetMax = Vector2.zero; _layer.SetAsLastSibling();
            _projectile = Image("FlyingPetard", 86, 112);
            _blastA = Image("ImpactA", 295, 295); _blastB = Image("ImpactB", 295, 295);
            _shield = Image("ShieldActivation", 200, 240);
            _sparks = new Image[18];
            for (var i=0; i<_sparks.Length; i++) _sparks[i] = Image("Ember" + i, 4, 9);
            HideAll();
        }

        private Image Image(string name, float w, float h)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(_layer, false); image.raycastTarget = false; image.preserveAspect = true;
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(.5f,.5f);
            image.rectTransform.sizeDelta = new Vector2(w,h); return image;
        }

        /// <summary>Returns exactly at contact. The caller applies the authoritative state/popups in that frame.</summary>
        public IEnumerator FlyPetard(Match3PlayerPanel source, Match3PlayerPanel target, bool manaDrain)
        {
            Build();
            // A subsequent attack may overlap the tail of shield activation; only replace projectile-owned visuals.
            if (_impactRoutine != null) StopCoroutine(_impactRoutine);
            _impactRoutine = null; PetardFlying = ImpactIsActive = false;
            _projectile.gameObject.SetActive(false); _blastA.gameObject.SetActive(false); _blastB.gameObject.SetActive(false); HideSparks();
            if (source == null || target == null) yield break;
            _layer.SetAsLastSibling();
            LastFlightFrom = Point(source); LastFlightTo = Point(target);
            var a = LastFlightFrom; var b = LastFlightTo;
            _projectile.sprite = Match3BattleFxSprites.Petard;
            if (_projectile.sprite == null) yield break;
            PetardFlying = true; LaunchCount++;
            _projectile.gameObject.SetActive(true);
            var elapsed = 0f;
            while (elapsed < FlightSeconds)
            {
                var t = Mathf.Clamp01(elapsed / FlightSeconds);
                _projectile.rectTransform.anchoredPosition = FlightPoint(a,b,t);
                _projectile.rectTransform.localRotation = Quaternion.Euler(0,0,Mathf.Lerp(24,-245,t) * Mathf.Sign(b.x-a.x));
                Trail(a,b,t);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            _projectile.gameObject.SetActive(false); HideSparks(); PetardFlying = false;
            ImpactCount++; ImpactIsActive = true;
            _impactRoutine = StartCoroutine(Impact(b, manaDrain));
        }

        public IEnumerator WaitForImpact()
        {
            while (ImpactIsActive) yield return null;
        }

        public void PlayShield(Match3PlayerPanel actor)
        {
            Build(); if (actor == null || Match3BattleFxSprites.Shield == null) return;
            if (_shieldRoutine != null) StopCoroutine(_shieldRoutine);
            _shieldRoutine = StartCoroutine(Shield(Point(actor)));
        }

        public static Vector2 FlightPoint(Vector2 from, Vector2 to, float progress)
        {
            var t = Mathf.Clamp01(progress);
            return Vector2.Lerp(from,to,t) + Vector2.up * (4 * t * (1-t) * 138f);
        }

        private Vector2 Point(Match3PlayerPanel panel)
        {
            var content = transform.Find("ExpeditionBattleSafeArea");
            var opponent = panel.transform is RectTransform rt && rt.anchorMin.x >= .5f;
            var figure = content != null ? content.Find(opponent ? "ExpeditionOpponentFigure" : "ExpeditionPlayerFigure") as RectTransform : null;
            var anchor = figure != null ? figure : panel.PortraitFrame;
            if (anchor == null) return Vector2.zero;
            var canvas = GetComponent<Canvas>();
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var screen = RectTransformUtility.WorldToScreenPoint(camera, anchor.TransformPoint(anchor.rect.center));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_layer,screen,camera,out var local);
            return local;
        }

        private void Trail(Vector2 from, Vector2 to, float t)
        {
            for(var i=0; i<_sparks.Length; i++)
            {
                var lag = (i+1)*.012f; var p = t-lag;
                var spark = _sparks[i]; spark.gameObject.SetActive(p >= 0);
                spark.rectTransform.anchoredPosition = FlightPoint(from,to,p) + new Vector2(Mathf.Sin(i*2.4f+t*31)*8, Mathf.Cos(i*1.8f+t*23)*7);
                spark.rectTransform.localRotation = Quaternion.Euler(0,0,t*700+i*49);
                spark.rectTransform.sizeDelta = new Vector2(2+i%3, 5+i%4);
                spark.color = new Color(1,Mathf.Lerp(.94f,.30f,i/18f),.11f,1-i/20f);
            }
        }

        private IEnumerator Impact(Vector2 at, bool manaDrain)
        {
            var frames = Match3BattleFxSprites.Explosion;
            _blastA.gameObject.SetActive(frames.Length > 0); _blastB.gameObject.SetActive(frames.Length > 0);
            _blastA.rectTransform.anchoredPosition = _blastB.rectTransform.anchoredPosition = at;
            var elapsed = 0f;
            while(elapsed < ImpactSeconds)
            {
                var t = Mathf.Clamp01(elapsed / ImpactSeconds);
                if(frames.Length > 0)
                {
                    var f = t*(frames.Length-1); var n = Mathf.FloorToInt(f);
                    _blastA.sprite = frames[n]; _blastB.sprite = frames[Mathf.Min(n+1,frames.Length-1)];
                    _blastA.color = new Color(1,1,1,1-(f-n)); _blastB.color = new Color(1,1,1,f-n);
                    var size = Mathf.Lerp(230,340,t); _blastA.rectTransform.sizeDelta = _blastB.rectTransform.sizeDelta = Vector2.one*size;
                }
                for(var i=0;i<_sparks.Length;i++)
                {
                    var angle = i*2.39996f; var direction = new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
                    var spark = _sparks[i]; spark.gameObject.SetActive(t < .82f);
                    spark.rectTransform.anchoredPosition = at + direction * (20+t*(65+i%5*13)) + Vector2.down*t*t*44;
                    spark.rectTransform.localRotation = Quaternion.Euler(0,0,angle*Mathf.Rad2Deg-90);
                    spark.rectTransform.sizeDelta = new Vector2(3,Mathf.Lerp(16,3,t));
                    spark.color = manaDrain ? new Color(.3f,.93f,1,1-t) : new Color(1,.68f,.16f,1-t);
                }
                elapsed += Time.unscaledDeltaTime; yield return null;
            }
            _blastA.gameObject.SetActive(false); _blastB.gameObject.SetActive(false); HideSparks();
            ImpactIsActive = false; _impactRoutine = null;
        }

        private IEnumerator Shield(Vector2 at)
        {
            ShieldCount++; _shield.sprite = Match3BattleFxSprites.Shield; _shield.gameObject.SetActive(true);
            _shield.rectTransform.anchoredPosition = at;
            var elapsed = 0f;
            while(elapsed < .78f)
            {
                var t = elapsed/.78f;
                var grow = t < .28f ? Mathf.Lerp(.35f,1.1f,Mathf.Sin(t/.28f*Mathf.PI*.5f)) : Mathf.Lerp(1.1f,1,t);
                _shield.rectTransform.localScale = Vector3.one*grow;
                _shield.rectTransform.localRotation = Quaternion.Euler(0,Mathf.Lerp(-40,0,Mathf.Clamp01(t/.3f)),Mathf.Lerp(-8,0,Mathf.Clamp01(t/.3f)));
                _shield.color = new Color(1,1,1,Mathf.Clamp01(t/.10f)*Mathf.Clamp01((1-t)/.35f));
                elapsed += Time.unscaledDeltaTime; yield return null;
            }
            _shield.gameObject.SetActive(false); _shieldRoutine = null;
        }

        public void CancelTransient()
        {
            StopAllCoroutines(); _impactRoutine = _shieldRoutine = null;
            PetardFlying = ImpactIsActive = false; HideAll();
        }
        private void HideSparks() { if (_sparks != null) foreach(var s in _sparks) if(s != null) s.gameObject.SetActive(false); }
        private void HideAll()
        {
            if (_projectile != null) _projectile.gameObject.SetActive(false);
            if (_blastA != null) _blastA.gameObject.SetActive(false); if(_blastB != null) _blastB.gameObject.SetActive(false);
            if (_shield != null) _shield.gameObject.SetActive(false); HideSparks();
        }
        private void OnDisable() => CancelTransient();
        private void OnDestroy() { if(_layer != null) Destroy(_layer.gameObject); }
    }
}
