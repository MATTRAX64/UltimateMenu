using System.Collections.Generic;
using UnityEngine;

namespace UltimateMenu
{
    public partial class MenuUI
    {
        static readonly Dictionary<string,string> PresentationKeys = new Dictionary<string,string>
        {
            {"Voice.Title","Voice"},{"Camera.Title","Camera"},{"Mocap.Title","Mocap"},
            {"Spawner.Title","Spawner"},{"Stream.Title","Stream"},{"Tracking.Title","Tracking"},{"Miror.Title","Mirror"},{"Ragdoll.Title","Ragdoll"}
        };        void DrawModPresentation(string title)
        {
            if(!PresentationKeys.TryGetValue(title,out string kind)) return;
            float captionHeight=Mathf.Max(28f*UI.Scale,UI.Px(10)+12f*UI.Scale);
            Rect panel=GUILayoutUtility.GetRect(GUIContent.none,GUIStyle.none,GUILayout.ExpandWidth(true),GUILayout.Height(106f*UI.Scale+captionHeight));
            UI.DrawGlass(panel,MenuStyle.Input,Mathf.RoundToInt(16f*UI.Scale));
            var caption=UI.Sty(UI.SecondaryText,10,false,TextAnchor.MiddleCenter);
            GUI.Label(new Rect(panel.x+10f*UI.Scale,panel.yMax-captionHeight,panel.width-20f*UI.Scale,captionHeight),I18n.T("Preview."+kind),caption);
            if(Event.current.type==EventType.Repaint)
            {
                Rect screen=new Rect(GUIUtility.GUIToScreenPoint(panel.position),panel.size);
                if(screen.yMax>=0 && screen.yMin<Screen.height)
                    ModPresentation.Draw(new Rect(panel.x+12f*UI.Scale,panel.y+8f*UI.Scale,panel.width-24f*UI.Scale,94f*UI.Scale),kind,_navigationIcons[19],_navigationIcons[20]);
            }
            Space(12);
        }
    }

    internal static class ModPresentation
    {
        static readonly string[] TrackingDescriptions={"Tracking.ModeFangameDesc","Tracking.ModeSmoothDesc","Tracking.ModeTrackersDesc","Tracking.ModeCustomDesc"};
        static Rect _area;
        static float _time;
        static Color Ink=>Theme.Text;
        static Color Tint=>Theme.Accent;
        static Rect R(float x,float y,float w,float h)=>new Rect(_area.x+x*_area.width,_area.y+y*_area.height,w*_area.width,h*_area.height);
        static Vector2 P(float x,float y)=>new Vector2(_area.x+x*_area.width,_area.y+y*_area.height);
        static void Box(float x,float y,float w,float h,Color color,float radius=8f)=>UI.DrawRounded(R(x,y,w,h),color,Mathf.RoundToInt(radius*UI.Scale));
        static void Dot(float x,float y,float radius,Color color)
        {
            Vector2 p=P(x,y);float r=radius*_area.height;
            UI.DrawRounded(new Rect(p.x-r,p.y-r,r*2,r*2),color,Mathf.CeilToInt(r));
        }
        static void Text(string text,float x,float y,float w,float h,int size=10)
            =>GUI.Label(R(x,y,w,h),text,UI.Sty(Ink,size,true,TextAnchor.MiddleCenter));
        static Color Fade(Color color,float opacity){color.a*=opacity;return color;}
        static void Link(float x1,float y1,float x2,float y2,Color color)
        {
            Vector2 a=P(x1,y1),b=P(x2,y2);var matrix=GUI.matrix;
            try
            {
                GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg,a);
                UI.DrawRounded(new Rect(a.x,a.y-UI.Scale,Vector2.Distance(a,b),2f*UI.Scale),color,1);
            }
            finally{GUI.matrix=matrix;}
        }
        static void Avatar(float x,float y,float size,Color color,bool pose=false)
        {
            float swing=pose?Mathf.Sin(_time*2f)*.12f:0f;
            Dot(x,y,size,color);
            Dot(x-.012f,y-.012f,size*.15f,MenuStyle.Input);Dot(x+.012f,y-.012f,size*.15f,MenuStyle.Input);
            Box(x-.035f,y+size,.07f,size*1.8f,color,12);
            Link(x-.03f,y+size*1.4f,x-.08f,y+size+swing,color);
            Link(x+.03f,y+size*1.4f,x+.08f,y+size-swing,color);
        }
        static void Icon(Texture2D texture,Vector2 center,float height,Color color)
        {
            if(texture==null)return;
            Color old=GUI.color;GUI.color=old*color;
            GUI.DrawTexture(new Rect(center.x-height*.5f,center.y-height*.5f,height,height),texture,ScaleMode.ScaleToFit);
            GUI.color=old;
        }
        static Vector2 Project(Vector3 point,Quaternion rotation)
        {
            Vector3 p=rotation*point;
            return P(.5f+p.x*.24f,.77f-p.y*.55f-p.z*.13f);
        }
        static void BodyRotation(float pitch,float yaw,float roll,float headPitch,float headYaw)
        {
            Quaternion body=Quaternion.Euler(pitch,yaw,roll);
            Vector3[] corners={new Vector3(-.25f,0,0),new Vector3(.25f,0,0),new Vector3(.25f,.65f,0),new Vector3(-.25f,.65f,0)};
            for(int i=0;i<4;i++)
            {
                Vector2 a=Project(corners[i],body),b=Project(corners[(i+1)%4],body);
                Link((a.x-_area.x)/_area.width,(a.y-_area.y)/_area.height,(b.x-_area.x)/_area.width,(b.y-_area.y)/_area.height,Ink);
            }
            Vector2 neck=Project(new Vector3(0,.9f,0),body);
            float radius=_area.height*.09f;
            UI.DrawRounded(new Rect(neck.x-radius,neck.y-radius,radius*2,radius*2),Ink,Mathf.CeilToInt(radius));
            Vector2 gaze=neck+new Vector2(Mathf.Sin(headYaw*Mathf.Deg2Rad)*radius*.8f,Mathf.Sin(headPitch*Mathf.Deg2Rad)*radius*.8f);
            UI.DrawRounded(new Rect(gaze.x-2*UI.Scale,gaze.y-2*UI.Scale,4*UI.Scale,4*UI.Scale),Tint,2);
        }
        internal static void Draw(Rect area,string kind,Texture2D person,Texture2D ring)
        {
            _area=area;_time=Time.unscaledTime;
            float phase=Mathf.Repeat(_time/6f,1f);
            switch(kind)
            {
                case "Ragdoll":
                    Text(I18n.T(phase<.75f?"Ragdoll.Trigger":"Ragdoll.Restore"),.1f,0,.8f,.2f,9);
                    Link(.22f,.9f,.78f,.9f,MenuStyle.Raised);
                    float fall=Mathf.SmoothStep(0,1,Mathf.Clamp01((phase-.15f)/.35f));
                    float returnFade=phase>.75f?Mathf.Clamp01((1-phase)*4f):1f;
                    Vector2 pivot=P(.5f,.54f+fall*.19f);
                    var previousMatrix=GUI.matrix;
                    try
                    {
                        GUIUtility.RotateAroundPivot(fall*78f,pivot);
                        Icon(person,pivot,area.height*.6f,Fade(Ink,returnFade));
                        float flail=Mathf.Sin(phase*40f)*fall*.1f;
                        Link(.48f,.54f+fall*.19f,.41f,.51f+fall*.19f+flail,Fade(Ink,returnFade));
                        Link(.52f,.54f+fall*.19f,.59f,.51f+fall*.19f-flail,Fade(Ink,returnFade));
                    }
                    finally{GUI.matrix=previousMatrix;}
                    if(phase>.75f)Icon(person,P(.5f,.54f),area.height*.6f,Fade(Tint,1f-returnFade));
                    break;
                case "Voice":
                    float outer=Mathf.Max(.5f,MenuPrefs.GetFloat("MUM_VC_Outer",15f));
                    float inner=Mathf.Clamp(MenuPrefs.GetFloat("MUM_VC_Inner",5f)/outer,.12f,.85f);
                    Vector2 middle=P(.5f,.5f);
                    float radius=area.height*.48f;
                    Icon(ring,middle,radius*2f,Fade(Tint,.4f));
                    Icon(ring,middle,radius*2f*inner,Fade(Tint,.9f));
                    Icon(person,middle,area.height*.28f,Ink);
                    for(int i=0;i<2;i++)
                    {
                        float movement=.5f-.5f*Mathf.Cos(_time*.9f+i*Mathf.PI);
                        float distance=radius*(.25f+movement*1.8f);
                        Vector2 position=middle+new Vector2(i==0?-distance:distance,i==0?-radius*.18f:radius*.18f);
                        float strength=distance<radius*inner?1f:Mathf.Lerp(1f,.25f,Mathf.Clamp01((distance-radius*inner)/(radius*(1-inner))));
                        Icon(person,position,area.height*.24f,Fade(Ink,strength));
                    }
                    break;
                case "Camera":
                    bool preparing=phase<.5f;
                    if(preparing)
                    {
                        float manualX=.5f+Mathf.Sin(phase*Mathf.PI*4f)*.18f;
                        float manualY=.48f+Mathf.Cos(phase*Mathf.PI*4f)*.10f;
                        Box(manualX-.04f,manualY-.1f,.08f,.2f,Ink,4);Dot(manualX,manualY,.045f,MenuStyle.Input);
                        Text(I18n.T("Camera.Prepare"),.1f,.01f,.8f,.2f,9);
                        Text(GorillaCamera.MovementKeys,.1f,.74f,.8f,.2f,9);
                    }
                    else
                    {
                        float progress=Mathf.SmoothStep(0f,1f,(phase-.5f)*2f);
                        for(int i=0;i<3;i++)
                        {
                            float nodeX=.26f+i*.24f;
                            if(i<2)Link(nodeX,.67f,nodeX+.24f,.67f,MenuStyle.Raised);
                            Dot(nodeX,.67f,.065f,Tint);Text((i+1).ToString(),nodeX-.03f,.56f,.06f,.22f,9);
                        }
                        float cameraX=.26f+progress*.48f;
                        Box(cameraX-.04f,.27f,.08f,.26f,Ink,4);Dot(cameraX,.4f,.055f,MenuStyle.Input);
                        Text(I18n.T("Camera.Start"),.15f,.01f,.7f,.22f,9);
                    }
                    break;
                case "Mocap":
                    int step=Mathf.Min(2,(int)(phase*3f));
                    Text(I18n.T(step==0?"Mocap.ToRecord":step==1?"Mocap.StartRecord":"Mocap.StopSave"),.12f,.01f,.76f,.23f,9);
                    Icon(person,P(.36f,.58f),area.height*.62f,Ink);
                    if(step==0) {Dot(.28f,.36f,.03f,Tint);}
                    if(step==1)
                    {
                        Icon(person,P(.62f,.58f),area.height*.62f,Fade(Tint,.7f));
                        float motion=Mathf.Sin(_time*3f)*.12f;
                        Link(.33f,.57f,.27f,.59f+motion,Ink);
                        Link(.39f,.57f,.45f,.59f-motion,Ink);
                        Link(.59f,.57f,.53f,.59f+motion,Tint);
                        Link(.65f,.57f,.71f,.59f-motion,Tint);
                        Dot(.22f,.35f,.035f,new Color(1f,.25f,.3f));
                        Box(.18f,.88f,.65f*Mathf.Repeat(phase*3f,1f),.035f,Tint,2);
                    }
                    if(step==2)
                    {
                        Box(.56f,.34f,.12f,.45f,Tint,4);
                        for(int i=0;i<3;i++) Box(.58f,.43f+i*.08f,.08f,.018f,Ink,0);
                    }
                    break;
                case "Spawner":
                    int spawnStep=Mathf.Min(2,(int)(phase*3f));
                    Text(I18n.T(spawnStep==0?"Spawner.SelectFile":spawnStep==1?"Spawner.Spawn":"Spawner.Held"),.15f,.01f,.7f,.23f,9);
                    Box(.21f,.85f,.58f,.025f,MenuStyle.Raised,0);
                    if(spawnStep==0)
                    {
                        Box(.35f,.33f,.3f,.4f,MenuStyle.Surface);
                        Text(".bundle",.35f,.33f,.3f,.4f,11);
                    }
                    else
                    {
                        float grab=Mathf.SmoothStep(0f,1f,Mathf.Clamp01((phase-.66f)/.22f));
                        float objectX=.42f+grab*.15f;
                        float objectY=.61f-grab*.25f;
                        Box(objectX,objectY,.12f,.22f,Tint,4);
                        if(spawnStep==2) {Dot(objectX+.13f,objectY+.18f,.06f,Ink);Link(objectX+.13f,objectY+.18f,objectX+.2f,objectY+.27f,Ink);}
                    }
                    break;
                case "Stream":
                    float center=Mathf.SmoothStep(0f,1f,Mathf.Clamp01((phase-.25f)/.4f));
                    float overlayX=Mathf.Lerp(.16f,.34f,center);
                    Box(.12f,.25f,.76f,.6f,MenuStyle.Raised);
                    Box(overlayX,.34f,.32f,.4f,MenuStyle.Surface);
                    Text("ABCD",overlayX,.35f,.32f,.2f,10);Text("4",overlayX,.55f,.32f,.16f,9);
                    Text(I18n.T("Stream.CenterHorizontal"),.12f,.01f,.76f,.23f,9);
                    break;
                case "Tracking":
                    var tracking=GorillaTrackingSafe.Instance;
                    var mode=tracking!=null?tracking.currentMode:GorillaTrackingSafe.TrackingMode.Fangame;
                    float headPitch=25f+25f*Mathf.Sin(_time*1.5f),headYaw=55f*Mathf.Sin(_time*1.5f);
                    float pitch=headPitch,yaw=headYaw,roll=0;
                    if(mode==GorillaTrackingSafe.TrackingMode.Smooth){pitch=0;yaw=55f*Mathf.Sin(_time*1.5f-.45f);}
                    if(mode==GorillaTrackingSafe.TrackingMode.Trackers){pitch=20f*Mathf.Sin(_time);yaw=40f*Mathf.Sin(_time*.8f);roll=12f*Mathf.Sin(_time*1.3f);}
                    if(mode==GorillaTrackingSafe.TrackingMode.Custom&&tracking!=null)
                    {pitch=(tracking.customPitch?headPitch:0)+tracking.customOffsetX;yaw=(tracking.customYaw?headYaw:0)+tracking.customOffsetY;roll=tracking.customOffsetZ;}
                    Text(I18n.T(TrackingDescriptions[Mathf.Clamp((int)mode,0,3)]),.06f,.01f,.88f,.23f,9);
                    BodyRotation(pitch,yaw,roll,headPitch,headYaw);
                    if(mode==GorillaTrackingSafe.TrackingMode.Trackers)Dot(.5f,.55f,.045f,Tint);
                    break;
                case "Mirror":
                    // Coarse sampling is only an illustration; it never touches the game camera.
                    float smooth=Mathf.SmoothStep(0f,1f,Mathf.Clamp01((phase-.2f)/.45f));
                    float pixels=area.height*.58f;
                    Vector2 location=P(.5f,.52f);
                    for(int y=0;y<12;y++)for(int x=0;x<10;x++)
                    {
                        float dx=(x-4.5f)/10f,dy=(y-2f)/12f;
                        bool headPixel=dx*dx+dy*dy<.035f;
                        bool torso=y>=5&&y<=10&&Mathf.Abs(dx)<.3f;
                        if(headPixel||torso)
                            UI.DrawRounded(new Rect(location.x+(x-5)*pixels/10f,location.y+(y-6)*pixels/12f,pixels/10f,pixels/12f),Fade(Ink,1f-smooth),0);
                    }
                    Icon(person,location,pixels,Fade(Ink,smooth));
                    Box(.32f,.94f,.36f*smooth,.025f,Tint,1);
                    break;
            }
        }
    }
}
