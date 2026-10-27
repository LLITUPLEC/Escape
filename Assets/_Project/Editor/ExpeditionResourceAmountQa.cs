using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Project.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Exercises the shipping HUD resource widgets without profile, wallet or network calls.</summary>
    public static class ExpeditionResourceAmountQa
    {
        [Serializable] private sealed class Report { public bool passed; public string checkedUtc; public int examples; public string[] screenshots; }
        public static string Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Run the HUD render probe in Edit mode.");
            var examples = new (long value, string text)[] { (0,"0"),(3450,"3450"),(9999,"9999"),(10000,"10К"),(11435,"11.4К"),(231222,"231К"),(999949,"1М"),(1000000,"1М"),(11435000,"11.4М"),(1000000000,"1Б"),(long.MaxValue,"9.2Кн") };
            foreach (var sample in examples)
                if (CompactResourceAmount.Format(sample.value) != sample.text) throw new InvalidOperationException("Resource formatting differs for " + sample.value);
            const string output = ".cache/qa/legacy-0.3.9/hud";
            Directory.CreateDirectory(output);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var root = new GameObject("OfflineResourceHudQa"); SceneManager.MoveGameObjectToScene(root, scene);
            var previous = RenderTexture.active;
            var target = new RenderTexture(1080,1920,24,RenderTextureFormat.ARGB32); target.Create();
            var shots = new List<string>();
            try
            {
                var camera = new GameObject("Camera",typeof(Camera)).GetComponent<Camera>(); camera.transform.SetParent(root.transform);
                camera.orthographic = true; camera.orthographicSize = 960; camera.transform.position = new Vector3(0,0,-10);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = ExpeditionTheme.Panel; camera.cullingMask = 1 << 31; camera.targetTexture = target;
                var canvas = new GameObject("Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler)).GetComponent<Canvas>(); canvas.transform.SetParent(root.transform);
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1080,1920); scaler.matchWidthOrHeight = .5f;
                var header = canvas.gameObject.AddComponent<ExpeditionResourceHeader>();
                var panel = ExpeditionUi.Panel(canvas.transform,"ActualHudGeometry",.014f,.929f,.986f,.995f).rectTransform;
                var resource = typeof(ExpeditionResourceHeader).GetMethod("Resource",BindingFlags.Instance|BindingFlags.NonPublic);
                var fields = new TMP_Text[3];
                fields[0] = (TMP_Text)resource.Invoke(header,new object[]{panel,"Энергия +",.325f,.496f,new UnityEngine.Events.UnityAction(()=>{})});
                fields[1] = (TMP_Text)resource.Invoke(header,new object[]{panel,"Золото",.498f,.669f,new UnityEngine.Events.UnityAction(()=>{})});
                fields[2] = (TMP_Text)resource.Invoke(header,new object[]{panel,"Руда",.671f,.842f,new UnityEngine.Events.UnityAction(()=>{})});
                var values = new[]{new long[]{3450,11435,231222},new long[]{9999,10000,999949},new long[]{1000000,11435000,long.MaxValue}};
                for(var index=0;index<values.Length;index++)
                {
                    for(var i=0;i<3;i++) fields[i].text = CompactResourceAmount.Format(values[index][i]);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(panel); Canvas.ForceUpdateCanvases();
                    foreach(var text in fields)
                    {
                        text.ForceMeshUpdate();
                        if (text.text.Contains("…") || text.text.Contains("...") || text.renderedWidth > text.rectTransform.rect.width + .5f)
                            throw new InvalidOperationException("Actual HUD truncates/overflows: " + text.text);
                    }
                    typeof(ExpeditionNotificationStakeQa).GetMethod("SaveStake",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{camera,target,canvas.transform,"hud-amounts-"+index,shots,output});
                }
                File.WriteAllText(Path.Combine(output,"resource-amount-validation.json"),JsonUtility.ToJson(new Report{passed=true,checkedUtc=DateTime.UtcNow.ToString("O"),examples=examples.Length,screenshots=shots.ToArray()},true));
                return "PASS HUD: eleven formatting examples and three actual resource-widget renders; no ellipsis/overflow, no account/network actions.";
            }
            finally
            {
                RenderTexture.active = previous;
                foreach(var camera in root.GetComponentsInChildren<Camera>()) camera.targetTexture = null;
                target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.CloseScene(scene,true);
            }
        }
    }
}
