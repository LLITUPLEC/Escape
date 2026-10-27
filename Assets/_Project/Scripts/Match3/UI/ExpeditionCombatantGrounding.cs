using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Match3
{
    /// <summary>Source-alpha geometry baked before import. Runtime never reads texture pixels or assumes transparent padding is a foot.</summary>
    public static class ExpeditionCombatantGrounding
    {
        public const float FloorUvY = .75f;
        [Serializable] public sealed class Footprint
        {
            public string name,source,sha256;
            public int width,height;
            public float x,y,w,h,footX,footY;
            public Rect SolidBounds => new Rect(x,y,w,h);
            public Vector2 Contact => new Vector2(footX,footY);
        }
        [Serializable] private sealed class Catalog { public int schema; public Footprint[] entries; }
        private static Dictionary<string,Footprint> _entries;
        public static bool TryGet(Sprite sprite,out Footprint result)
        {
            if(_entries==null)
            {
                _entries=new Dictionary<string,Footprint>(StringComparer.Ordinal);
                var asset=Resources.Load<TextAsset>("Expedition/Art/combatant-grounding");
                var catalog=asset!=null?JsonUtility.FromJson<Catalog>(asset.text):null;
                if(catalog?.schema==1&&catalog.entries!=null)
                    foreach(var entry in catalog.entries)
                        if(entry!=null&&!string.IsNullOrEmpty(entry.name)&&entry.w>0&&entry.h>0)_entries[entry.name]=entry;
            }
            result=null;return sprite!=null&&_entries.TryGetValue(sprite.name,out result);
        }
        public static Vector2 FullImageSize(Sprite sprite,Footprint footprint,float maximumWidth,float maximumHeight)
        {
            var source=sprite.rect.size;
            var solid=footprint?.SolidBounds??new Rect(0,0,1,1);
            var scale=Mathf.Min(maximumWidth/Mathf.Max(1,source.x*solid.width),maximumHeight/Mathf.Max(1,source.y*solid.height));
            return source*Mathf.Max(.0001f,scale);
        }
        public static Vector2 GroundPoint(RectTransform backdrop,RectTransform parent,float x,float floorUvY=FloorUvY)
        {
            var rect=backdrop.rect;
            var world=backdrop.TransformPoint(new Vector2(Mathf.Lerp(rect.xMin,rect.xMax,x),Mathf.Lerp(rect.yMin,rect.yMax,floorUvY)));
            return parent.InverseTransformPoint(world);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()=>_entries=null;
    }
}
