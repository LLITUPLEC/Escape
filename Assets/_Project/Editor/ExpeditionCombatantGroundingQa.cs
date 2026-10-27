using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Project.Character;
using Project.Match3;
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
    /// <summary>Actual runtime figures, source-alpha metadata, and the shipping EnvelopeParent background in disposable Edit-mode scenes. No account/controller/network lifecycle.</summary>
    public static class ExpeditionCombatantGroundingQa
    {
        private const string Output=".cache/qa/legacy-0.3.11/grounding";
        [Serializable] private sealed class Case
        {
            public string sprite,viewport;
            public float feetErrorPixels,aspectError,solidHeightPixels,solidWidthPixels;
        }
        [Serializable] private sealed class Report
        {
            public bool passed,offline=true,networkOrAccountActions,sourcePngModified;
            public string checkedUtc,error;
            public int sourceSprites,placementCases;
            public float maximumFeetErrorPixels;
            public Case[] cases;
            public string[] screenshots;
        }
        private sealed class Profile
        {
            public string Name;public int Width,Height,Top,Bottom,Side;
            public Profile(string n,int w,int h,int top=0,int bottom=0,int side=0){Name=n;Width=w;Height=h;Top=top;Bottom=bottom;Side=side;}
        }
        public static string Run() => RunTo(Output);

        public static string RunTo(string output)
        {
            Require(!EditorApplication.isPlaying,"Grounding QA runs only in Edit mode; it never opens a production battle.");
            var profiles=new[]{new Profile("portrait-1080",1080,1920),new Profile("portrait-1080-safe",1080,1920,80,40,18),
                new Profile("tall-1440",1440,3088),new Profile("tall-1440-safe",1440,3088,120,80,32)};
            var report=new Report{checkedUtc=DateTime.UtcNow.ToString("O")};var cases=new List<Case>();var shots=new List<string>();
            Directory.CreateDirectory(output);
            var originalScene=SceneManager.GetActiveScene();var previous=RenderTexture.active;
            try
            {
                var hero=Resources.Load<Sprite>("Expedition/Art/hero");Require(hero!=null,"The original hero is missing.");
                var heroes=new List<Sprite>{hero};
                foreach(var theme in CharacterAppearance.Themes)foreach(var gender in new[]{"male","female"})
                {
                    var sprite=Resources.Load<Sprite>(CharacterAppearance.ResourceRoot+theme.Id+"-"+gender+"-body");
                    Require(sprite!=null,"Missing body: "+theme.Id+"/"+gender);heroes.Add(sprite);
                }
                var catalog=AssetDatabase.LoadAssetAtPath<MonsterCatalog>("Assets/_Project/Data/Match3/Monsters/MainMonsterCatalog.asset");
                var monsters=Enumerable.Range(1,12).Select(i=>catalog.GetByFloor(i)?.Icon).ToArray();Require(monsters.All(s=>s!=null),"All twelve actual monster sprites are required.");
                foreach(var sprite in heroes.Concat(monsters))
                {
                    Require(ExpeditionCombatantGrounding.TryGet(sprite,out var footprint),"Unbaked source: "+sprite.name);
                    var source=AssetDatabase.GetAssetPath(sprite);
                    using var hash=SHA256.Create();var digest=BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(source))).Replace("-","").ToLowerInvariant();
                    Require(digest==footprint.sha256,"Source-alpha metadata is stale: "+source);
                    Require(footprint.footY>=footprint.y&&footprint.footY<=footprint.y+.02f,"Foot is outside the solid bottom band: "+source);
                }
                report.sourceSprites=heroes.Count+monsters.Length;
                for(var index=0;index<profiles.Length;index++)
                {
                    var profile=profiles[index];var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                    var roots=new List<GameObject>();RenderTexture target=null;Camera camera=null;
                    try
                    {
                        var cameraObject=new GameObject("GroundingQaCamera",typeof(Camera));SceneManager.MoveGameObjectToScene(cameraObject,scene);roots.Add(cameraObject);
                        camera=cameraObject.GetComponent<Camera>();camera.orthographic=true;camera.orthographicSize=profile.Height*.5f;
                        camera.transform.position=new Vector3(0,0,-10);camera.nearClipPlane=.1f;camera.farClipPlane=100;
                        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=ExpeditionTheme.Panel;camera.cullingMask=1<<31;
                        target=new RenderTexture(profile.Width,profile.Height,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;
                        var root=new GameObject("GroundingQaCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));SceneManager.MoveGameObjectToScene(root,scene);roots.Add(root);
                        var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
                        canvas.additionalShaderChannels=AdditionalCanvasShaderChannels.TexCoord1|AdditionalCanvasShaderChannels.Normal|AdditionalCanvasShaderChannels.Tangent;
                        var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ConstantPixelSize;scaler.scaleFactor=1;canvas.scaleFactor=1;
                        var background=ExpeditionUi.Fill(root.transform,"ExpeditionBattleBackdrop",0,0,1,1,Color.white);
                        background.sprite=Resources.Load<Sprite>("Expedition/Art/battle-world-v5");Require(background.sprite!=null,"The v5 floor is missing.");
                        var fit=background.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.EnvelopeParent;fit.aspectRatio=background.sprite.rect.width/background.sprite.rect.height;
                        var safe=ExpeditionUi.Rect(root.transform,"ExpeditionBattleSafeArea",profile.Side/(float)profile.Width,profile.Bottom/(float)profile.Height,
                            1-profile.Side/(float)profile.Width,1-profile.Top/(float)profile.Height);
                        var leftHeader=Header(safe,false);var rightHeader=Header(safe,true);
                        var left=Figure(safe,"ExpeditionPlayerFigure");var right=Figure(safe,"ExpeditionOpponentFigure");
                        left.BindPreview(hero,true);right.BindPreview(monsters[7],false);
                        left.GroundTo(background.rectTransform,leftHeader,.27f);right.GroundTo(background.rectTransform,rightHeader,.73f);
                        // Only the figures/background use runtime layout. The neutral board
                        // silhouette makes the complete phone frame readable without a Duel manager.
                        ExpeditionUi.Panel(safe,"OfflineBoardRegion",.04f,.17f,.96f,.655f,ExpeditionTheme.Panel).raycastTarget=false;
                        ExpeditionUi.Text(safe,"PreviewNotice","Опора на полу · offline-предпросмотр",27,.06f,.08f,.94f,.13f,TextAlignmentOptions.Center);
                        Canvas.ForceUpdateCanvases();
                        foreach(var sprite in heroes){left.BindPreview(sprite,true);left.RefreshGrounding();Verify(left,sprite,background.rectTransform,safe,leftHeader,.27f,camera,profile,cases);}
                        foreach(var sprite in monsters){right.BindPreview(sprite,false);right.RefreshGrounding();Verify(right,sprite,background.rectTransform,safe,rightHeader,.73f,camera,profile,cases);}
                        var theme=index==0?"dawnwarden-male":index==1?"railwarden-female":index==2?"beastbreaker-male":"phoenix-female";
                        left.BindPreview(Resources.Load<Sprite>(CharacterAppearance.ResourceRoot+theme+"-body"),true);
                        right.BindPreview(index%2==0?monsters[7]:monsters[2],false);
                        left.RefreshGrounding();right.RefreshGrounding();
                        var leftPoint=camera.WorldToScreenPoint(left.FootWorld);var rightPoint=camera.WorldToScreenPoint(right.FootWorld);
                        Require(Mathf.Abs(leftPoint.y-rightPoint.y)<.05f,"Both feet do not share the illustrated floor.");
                        foreach(var tr in root.GetComponentsInChildren<Transform>(true))tr.gameObject.layer=31;
                        Canvas.ForceUpdateCanvases();foreach(var text in root.GetComponentsInChildren<TMP_Text>()){text.ForceMeshUpdate();Require(!text.isTextOverflowing,"QA text clipped: "+text.name);}
                        if(GraphicsSettings.currentRenderPipeline!=null)RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=target});else camera.Render();
                        var pixels=new Texture2D(profile.Width,profile.Height,TextureFormat.RGB24,false);
                        try
                        {
                            RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,profile.Width,profile.Height),0,0);pixels.Apply();
                            var colors=new HashSet<int>();var data=pixels.GetPixels32();for(var i=0;i<data.Length;i+=997){var c=data[i];colors.Add((c.r<<16)|(c.g<<8)|c.b);}
                            Require(colors.Count>32,"The fixture rendered a blank frame.");
                            var path=output+"/"+profile.Name+".png";File.WriteAllBytes(path,pixels.EncodeToPNG());shots.Add(path);
                        }
                        finally{UnityEngine.Object.DestroyImmediate(pixels);}
                    }
                    finally
                    {
                        if(camera!=null)camera.targetTexture=null;RenderTexture.active=previous;
                        foreach(var root in roots)if(root!=null)UnityEngine.Object.DestroyImmediate(root);
                        if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}EditorSceneManager.CloseScene(scene,true);
                    }
                }
                report.placementCases=cases.Count;report.maximumFeetErrorPixels=cases.Max(c=>c.feetErrorPixels);report.passed=true;
            }
            catch(Exception error){report.error=error.ToString();}
            finally{RenderTexture.active=previous;if(originalScene.IsValid()&&originalScene.isLoaded)SceneManager.SetActiveScene(originalScene);}
            report.cases=cases.ToArray();report.screenshots=shots.ToArray();File.WriteAllText(output+"/grounding-validation.json",JsonUtility.ToJson(report,true));
            if(!report.passed)throw new InvalidOperationException(report.error);return JsonUtility.ToJson(report);
        }
        private static RectTransform Header(RectTransform parent,bool opponent)
        {
            var root=ExpeditionUi.Panel(parent,opponent?"OpponentHeader":"PlayerHeader",opponent ? .51f : .012f,.918f,opponent ? .988f : .49f,.992f,ExpeditionTheme.Panel).rectTransform;
            ExpeditionUi.Text(root,"Name",opponent?"Монстр":"Герой",25,.04f,.62f,.96f,.94f,TextAlignmentOptions.Center);
            ExpeditionUi.Fill(root,"Health",.05f,.32f,.95f,.56f,new Color32(197,53,40,255));
            ExpeditionUi.Fill(root,"Mana",.05f,.06f,.95f,.27f,new Color32(18,130,175,255));return root;
        }
        private static ExpeditionCombatantFigure Figure(RectTransform parent,string name)
        {
            var image=ExpeditionUi.Fill(parent,name,0,0,1,1,Color.white);image.preserveAspect=true;image.raycastTarget=false;return image.gameObject.AddComponent<ExpeditionCombatantFigure>();
        }
        private static void Verify(ExpeditionCombatantFigure figure,Sprite sprite,RectTransform background,RectTransform safe,RectTransform header,float x,Camera camera,Profile profile,List<Case> cases)
        {
            Require(figure.HasOpaqueMetadata,"Runtime fell back to a rectangle: "+sprite.name);
            ExpeditionCombatantGrounding.TryGet(sprite,out var data);var rect=figure.transform as RectTransform;
            var expected=background.TransformPoint(new Vector2(background.rect.xMin+background.rect.width*x,background.rect.yMin+background.rect.height*.75f));
            var sourceFoot=rect.TransformPoint(new Vector2((data.footX-rect.pivot.x)*rect.rect.width,(data.footY-rect.pivot.y)*rect.rect.height));
            var error=Vector2.Distance(camera.WorldToScreenPoint(sourceFoot),camera.WorldToScreenPoint(expected));Require(error<.05f,"Opaque feet missed the actual cropped floor: "+sprite.name+" / "+error);
            var before=sourceFoot;typeof(ExpeditionCombatantFigure).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(figure,null);
            sourceFoot=rect.TransformPoint(new Vector2((data.footX-rect.pivot.x)*rect.rect.width,(data.footY-rect.pivot.y)*rect.rect.height));
            Require(Vector3.Distance(before,sourceFoot)<.0001f,"Idle moved the feet: "+sprite.name);
            var aspect=Mathf.Abs(rect.rect.width/rect.rect.height-sprite.rect.width/sprite.rect.height);Require(aspect<.0001f,"A figure was stretched: "+sprite.name);
            var points=new[]{new Vector2(data.x,data.y),new Vector2(data.x,data.y+data.h),new Vector2(data.x+data.w,data.y),new Vector2(data.x+data.w,data.y+data.h)};
            var min=new Vector2(float.PositiveInfinity,float.PositiveInfinity);var max=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
            foreach(var p in points){var world=rect.TransformPoint(new Vector2((p.x-rect.pivot.x)*rect.rect.width,(p.y-rect.pivot.y)*rect.rect.height));var screen=(Vector2)camera.WorldToScreenPoint(world);min=Vector2.Min(min,screen);max=Vector2.Max(max,screen);}
            var corners=new Vector3[4];header.GetWorldCorners(corners);var top=camera.WorldToScreenPoint(corners[0]).y;
            Require(max.y<top-1,"Opaque figure overlaps the header: "+sprite.name);
            Require(min.x>=profile.Side-1&&max.x<=profile.Width-profile.Side+1,"Opaque figure is cropped by the safe area: "+sprite.name);
            cases.Add(new Case{sprite=sprite.name,viewport=profile.Name,feetErrorPixels=error,aspectError=aspect,solidHeightPixels=max.y-min.y,solidWidthPixels=max.x-min.x});
        }
        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    }
}
