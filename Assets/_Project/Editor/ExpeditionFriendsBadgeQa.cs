using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Project.Friends;
using Project.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Dedicated preview scene, actual runtime badge and deterministic local snapshots. Does not instantiate a controller, connect or alter the current account.</summary>
    public static class ExpeditionFriendsBadgeQa
    {
        private const string Output=".cache/qa/legacy-0.3.10/friends-badge";
        [Serializable] private sealed class Report
        {
            public bool passed, offline=true, rpcOrAccountActions, preferenceWrites;
            public string checkedUtc,error;
            public string[] checks,screenshots;
            public int width=1080,height=1920,minDistinctPixelSamples;
        }
        public static string Run()
        {
            Require(!EditorApplication.isPlaying,"Run the Friends badge probe in Edit mode.");
            var report=new Report{checkedUtc=DateTime.UtcNow.ToString("O")};
            var checks=new List<string>();var shots=new List<string>();
            var originalScene=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);RenderTexture target=null;Texture2D pixels=null;
            var previous=RenderTexture.active;
            try
            {
                Directory.CreateDirectory(Output);
                var cameraObject=new GameObject("FriendsBadgeQaCamera",typeof(Camera));SceneManager.MoveGameObjectToScene(cameraObject,scene);
                var camera=cameraObject.GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=ExpeditionTheme.Panel;
                camera.orthographic=true;camera.orthographicSize=960;camera.transform.position=new Vector3(0,0,-10);
                camera.nearClipPlane=.1f;camera.farClipPlane=100;camera.cullingMask=1<<31;
                target=new RenderTexture(1080,1920,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;
                var root=new GameObject("FriendsBadgeQaCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));SceneManager.MoveGameObjectToScene(root,scene);
                var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
                canvas.additionalShaderChannels=AdditionalCanvasShaderChannels.TexCoord1|AdditionalCanvasShaderChannels.Normal|AdditionalCanvasShaderChannels.Tangent;
                var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ConstantPixelSize;scaler.scaleFactor=1;canvas.scaleFactor=1;
                ExpeditionUi.Fill(root.transform,"Backdrop",0,0,1,1,ExpeditionTheme.Panel);
                ExpeditionUi.Text(root.transform,"Title","Друзья · входящие заявки",43,.07f,.77f,.93f,.85f,TextAlignmentOptions.Center);
                ExpeditionUi.Text(root.transform,"Notice","Offline-проверка настоящего компонента\nБез заявок, сообщений и изменения аккаунта",25,.07f,.69f,.93f,.77f,TextAlignmentOptions.Center);
                var button=ExpeditionUi.Button(root.transform,"ExpeditionOpenFriends","Друзья",.51f,.58f,.955f,.614f,null,false,25);
                var status=ExpeditionUi.Text(root.transform,"Scenario","",30,.07f,.37f,.93f,.51f,TextAlignmentOptions.Center);
                var scope="offline-account-A";
                var state=new FriendsIncomingInviteState(()=>scope);state.SynchronizeAccount();
                var badge=FriendsNavigationBadge.BindState(button,state);var clickCount=0;button.onClick.AddListener(()=>clickCount++);
                Action<string,string,int,bool> shot=(name,label,count,visible)=>
                {
                    Require(badge.PendingCount==count&&badge.IsVisible==visible,"Wrong badge state: "+name);
                    status.text=label;foreach(var tr in root.GetComponentsInChildren<Transform>(true))tr.gameObject.layer=31;
                    Canvas.ForceUpdateCanvases();
                    foreach(var text in root.GetComponentsInChildren<TMP_Text>()){text.ForceMeshUpdate();Require(!text.isTextOverflowing,"UI text clipped: "+text.text);}
                    var corners=new Vector3[4];badge.BadgeRect.GetWorldCorners(corners);
                    foreach(var corner in corners){var p=camera.WorldToViewportPoint(corner);Require(p.x>=0&&p.x<=1&&p.y>=0&&p.y<=1,"Badge outside the native canvas.");}
                    Require(badge.BadgeRect.GetComponentsInChildren<Graphic>(true).All(g=>!g.raycastTarget),"The badge intercepts the Friends button.");
                    var group=badge.BadgeRect.GetComponent<CanvasGroup>();Require(!group.blocksRaycasts&&!group.interactable,"Badge CanvasGroup blocks input.");
                    if(GraphicsSettings.currentRenderPipeline!=null)
                        RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=target});
                    else camera.Render();
                    RenderTexture.active=target;pixels=new Texture2D(1080,1920,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,1080,1920),0,0);pixels.Apply();
                    var colors=new HashSet<int>();var samples=pixels.GetPixels32();
                    for(var i=0;i<samples.Length;i+=997){var p=samples[i];colors.Add((p.r<<16)|(p.g<<8)|p.b);}
                    Require(colors.Count>16,"Capture has no rendered UI: only "+colors.Count+" distinct sampled colors.");
                    report.minDistinctPixelSamples=report.minDistinctPixelSamples==0?colors.Count:Math.Min(report.minDistinctPixelSamples,colors.Count);
                    var path=Output+"/"+name+".png";File.WriteAllBytes(path,pixels.EncodeToPNG());UnityEngine.Object.DestroyImmediate(pixels);pixels=null;shots.Add(path);
                };
                shot("zero","Входящих заявок нет",0,false);
                Require(state.Publish(scope,state.Generation,1),"One-request snapshot rejected.");shot("one","Одна входящая заявка",1,true);
                state.Publish(scope,state.Generation,12);shot("twelve","Двенадцать входящих заявок",12,true);
                Require(!state.Publish(scope,state.Generation,-1)&&state.Count==12,"A failed read erased a trusted count.");
                var oldGeneration=state.Generation;state.Invalidate();
                Require(!state.Publish(scope,oldGeneration,12),"A pre-mutation response can restore a handled request.");
                state.Publish(scope,state.Generation,0);shot("cleared","После принятия или отклонения: список обновлён, счётчик скрыт",0,false);
                checks.Add("Zero/one/twelve counts; no badge for0; unknown reads preserve the trusted count; completing a friend action invalidates pre-action responses before the refreshed count clears it.");
                state.Publish(scope,state.Generation,12);var accountAScope=scope;var accountAGeneration=state.Generation;
                scope="offline-account-B";state.SynchronizeAccount();shot("account-switch","Переход на другой аккаунт сразу очищает счётчик",0,false);
                Require(!state.Publish(accountAScope,accountAGeneration,12),"A late response leaked another account's requests.");
                state.Publish(scope,state.Generation,1);shot("account-b","Новый аккаунт показывает только свой снимок",1,true);
                scope=null;state.SynchronizeAccount();shot("signed-out","После выхода из аккаунта счётчик скрыт",0,false);
                scope=accountAScope;state.SynchronizeAccount();Require(!state.Publish(accountAScope,accountAGeneration,12),"An old account session's result replayed after logging back in.");
                checks.Add("Account switch and sign-out clear within SynchronizeAccount; old responses and old generations remain rejected even after returning to the same account.");
                var mixed=new[]{new FriendListEntry{UserId="in",State=FriendRelationState.InviteReceived},new FriendListEntry{UserId="in",State=FriendRelationState.InviteReceived},new FriendListEntry{UserId="out",State=FriendRelationState.InviteSent},new FriendListEntry{UserId="mutual",State=FriendRelationState.Mutual},new FriendListEntry{UserId="blocked",State=FriendRelationState.Blocked},null};
                Require(FriendsIncomingInviteState.CountIncoming(mixed)==1,"Outgoing/mutual/blocked/duplicate entries inflate pending count.");
                state.Publish(scope,state.Generation,123);Require(badge.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="123"),"The actual three-digit count is missing.");
                badge.enabled=false;badge.enabled=true;Require(badge.PendingCount==123&&badge.IsVisible,"Enable/disable lost a live binding.");
                FriendsNavigationBadge.BindState(button,state);button.onClick.Invoke();Require(clickCount==1,"Binding duplicates or replaces the existing open action.");
                Require(badge.BadgeRect.sizeDelta.x>60&&badge.BadgeRect.sizeDelta.y==44&&badge.BadgeRect.anchoredPosition==new Vector2(-10,-8)&&badge.BadgeRect.GetComponent<ExpeditionGoldCountGraphic>()!=null,"Three-digit badge does not use its adaptive gold bevel.");
                checks.Add("Only distinct incoming state2 IDs count; actual123; enable/rebind preserves live state and the existing button action; adaptive gold bevel, all non-raycast and unclipped.");
                checks.Add("URP render request or built-in Camera.Render produced non-blank whole1080x1920 frames; only the disposable scene's layer31 is visible.");
                report.passed=true;
            }
            catch(Exception error){report.error=error.ToString();}
            finally
            {
                RenderTexture.active=previous;if(pixels!=null)UnityEngine.Object.DestroyImmediate(pixels);
                EditorSceneManager.CloseScene(scene,true);
                if(originalScene.IsValid()&&originalScene.isLoaded)SceneManager.SetActiveScene(originalScene);
                if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}
            }
            report.checks=checks.ToArray();report.screenshots=shots.ToArray();Directory.CreateDirectory(Output);
            File.WriteAllText(Output+"/friends-badge-validation.json",JsonUtility.ToJson(report,true));
            if(!report.passed)throw new InvalidOperationException(report.error);
            return JsonUtility.ToJson(report);
        }
        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    }
}
