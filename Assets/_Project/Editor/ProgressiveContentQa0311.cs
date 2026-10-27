using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Project.Character;
using Project.Nakama;
using Project.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Editor
{
    public static class ProgressiveContentQa0311
    {
        [Serializable] private sealed class Report { public bool passed, accountOrNetworkActions; public int assertions; public string checkedUtc, error; public List<string> screenshots = new(); }
        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
            const string output = ".cache/qa/legacy-0.3.11/content-gates"; Directory.CreateDirectory(output);
            var type = typeof(PlayerGuidanceService); var hidden = BindingFlags.Static | BindingFlags.NonPublic;
            var current = type.GetField("_current",hidden); var scope = type.GetField("_scope",hidden);
            var oldCurrent = current.GetValue(null); var oldScope = scope.GetValue(null);
            var previous = SceneManager.GetActiveScene(); var previousTarget = RenderTexture.active;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            RenderTexture target = null; Camera camera = null;
            var report = new Report { checkedUtc = DateTime.UtcNow.ToString("O") };
            void Check(bool ok, string message) { report.assertions++; if(!ok)throw new InvalidOperationException(message); }
            try
            {
                camera = new GameObject("GateQaCamera",typeof(Camera)).GetComponent<Camera>(); camera.transform.position = new Vector3(0,0,-10);
                camera.orthographic = true; camera.orthographicSize = 960; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = ExpeditionTheme.Panel; camera.cullingMask = 1<<31;
                target = new RenderTexture(1080,1920,24,RenderTextureFormat.ARGB32); target.Create();camera.targetTexture=target;
                var canvas = new GameObject("GateQaCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler)).GetComponent<Canvas>();
                canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
                canvas.GetComponent<CanvasScaler>().uiScaleMode=CanvasScaler.ScaleMode.ConstantPixelSize;
                var events = new GameObject("GateQaEvents",typeof(EventSystem)).GetComponent<EventSystem>();
                ExpeditionUi.Text(canvas.transform,"Title","ОТКРЫТИЕ КОНТЕНТА",42,.06f,.88f,.94f,.94f,TMPro.TextAlignmentOptions.Center);
                var label = ExpeditionUi.Text(canvas.transform,"FixtureState","",28,.06f,.77f,.94f,.86f,TMPro.TextAlignmentOptions.Center);
                var clicks=0; var buttons=new Button[4]; var names=new[]{"Мастерская","Турнир кузнеца","Турнир руды","Турнир золота"};
                for(var i=0;i<4;i++)
                {
                    var y=.60f-i*.13f; buttons[i]=ExpeditionUi.Button(canvas.transform,"Gate"+i,names[i],.10f,y,.90f,y+.095f,()=>clicks++,false,32);
                    ProgressiveContentButton.Bind(buttons[i],i==0);
                }
                var samples=new[]{
                    new PlayerGuidanceService.State{ok=true,schema=1,mine_wins=0,tutorial_status="pending"},
                    new PlayerGuidanceService.State{ok=true,schema=1,mine_wins=9,workshop_unlocked=true,tutorial_status="completed"},
                    new PlayerGuidanceService.State{ok=true,schema=1,mine_wins=10,workshop_unlocked=true,tournaments_unlocked=true,tutorial_status="legacy"}};
                for(var s=0;s<samples.Length;s++)
                {
                    Check(PlayerGuidanceService.Valid(samples[s]),"Valid server snapshot rejected.");scope.SetValue(null,LocalAccountCacheScope.Current);current.SetValue(null,samples[s]);
                    label.text=s==0?"Новый аккаунт · рецепт ещё не получен":s==1?"Первый рецепт получен · 9 побед в шахте":"10 побед в шахте · существующий аккаунт";
                    for(var i=0;i<4;i++)
                    {
                        var component=buttons[i].GetComponent<ProgressiveContentButton>();component.GetType().GetMethod("Refresh",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(component,null);
                        var allowed=i==0?samples[s].workshop_unlocked:samples[s].tournaments_unlocked;
                        Check(buttons[i].IsInteractable()==allowed,"Wrong interaction layer.");var before=clicks;
                        buttons[i].OnPointerClick(new PointerEventData(events){button=PointerEventData.InputButton.Left});
                        Check(clicks==before+(allowed?1:0),"Locked pointer dispatched a callback.");
                    }
                    foreach(var child in canvas.GetComponentsInChildren<Transform>(true))child.gameObject.layer=31;
                    Canvas.ForceUpdateCanvases();
                    if(GraphicsSettings.currentRenderPipeline!=null)RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=target});else camera.Render();
                    RenderTexture.active=target;var pixels=new Texture2D(1080,1920,TextureFormat.RGB24,false);
                    try{pixels.ReadPixels(new Rect(0,0,1080,1920),0,0);pixels.Apply();var file=output+"/gate-state-"+s+".png";File.WriteAllBytes(file,pixels.EncodeToPNG());report.screenshots.Add(file);}finally{UnityEngine.Object.DestroyImmediate(pixels);}
                }
                Check(!PlayerGuidanceService.Valid(new PlayerGuidanceService.State{ok=true,schema=1,mine_wins=9,tournaments_unlocked=true,tutorial_status="pending"}),"Inconsistent threshold accepted.");
                scope.SetValue(null,"foreign-account");current.SetValue(null,samples[2]);Check(PlayerGuidanceService.Current==null&&!PlayerGuidanceService.WorkshopUnlocked&&!PlayerGuidanceService.TournamentsUnlocked,"Foreign-account unlock leaked.");
                report.passed=true;
            }
            catch(Exception e){report.error=e.ToString();throw;}
            finally
            {
                current.SetValue(null,oldCurrent);scope.SetValue(null,oldScope);RenderTexture.active=previousTarget;
                if(camera!=null)camera.targetTexture=null;if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}
                EditorSceneManager.CloseScene(scene,true);if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
                File.WriteAllText(output+"/content-gates-validation.json",JsonUtility.ToJson(report,true));
            }
            return "PASS: "+report.assertions+" progression interaction/scope checks and three native states, no RPC/account actions.";
        }
    }
}
