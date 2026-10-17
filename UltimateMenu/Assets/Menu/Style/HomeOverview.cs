using UnityEngine;

namespace UltimateMenu
{
    public partial class MenuUI
    {
        void DrawHomeOverview(int total, int active)
        {
            float unit = UI.Scale;
            float height = Mathf.Max(82f * unit, UI.Px(20) + UI.Px(10) + 26f * unit);
            Rect row = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(height), GUILayout.ExpandWidth(true));
            float gap = 10f * unit;
            float donationWidth = Mathf.Clamp(row.width * .22f, 78f * unit, 115f * unit);
            float combinedWidth = row.width - donationWidth - gap;
            float statWidth = combinedWidth * .5f;
            UI.DrawGlass(new Rect(row.x,row.y,combinedWidth,height),MenuStyle.Surface,Mathf.RoundToInt(16f*unit));
            Color separator = Theme.Text; separator.a = .2f;
            UI.DrawRounded(new Rect(row.x+statWidth-unit*.5f,row.y+16f*unit,unit,height-32f*unit),separator,Mathf.RoundToInt(unit*.5f));
            for (int i = 0; i < 2; i++)
            {
                Rect card = new Rect(row.x + i * statWidth, row.y, statWidth, height);
                float badgeSize = Mathf.Min(40f * unit, card.width * .27f);
                Rect badge = new Rect(card.x + 12f * unit, card.center.y - badgeSize * .5f, badgeSize, badgeSize);
                Color background = Theme.Accent; background.a = .18f;
                UI.DrawRounded(badge, background, Mathf.CeilToInt(badgeSize * .5f));
                float glyph = badgeSize * .64f;
                Rect glyphRect = new Rect(badge.center.x-glyph*.5f,badge.center.y-glyph*.5f,glyph,glyph);
                if (_navigationIcons[15+i] != null) GUI.DrawTexture(glyphRect,_navigationIcons[15+i],ScaleMode.ScaleToFit);
                float x = badge.xMax + 12f * unit;
                float numberHeight = UI.Px(20) + 4f * unit;
                float captionHeight = UI.Px(10) + 6f * unit;
                float y = card.center.y - (numberHeight + captionHeight) * .5f;
                var numberStyle = UI.Sty(Theme.Text, 20, true, TextAnchor.MiddleLeft);
                var captionStyle = UI.Sty(UI.SecondaryText, 10, false, TextAnchor.MiddleLeft);
                numberStyle.wordWrap = captionStyle.wordWrap = false;
                GUI.Label(new Rect(x,y,card.xMax-x-10f*unit,numberHeight),(i == 0 ? total : active).ToString(),numberStyle);
                GUI.Label(new Rect(x,y+numberHeight,card.xMax-x-10f*unit,captionHeight),I18n.T(i == 0 ? "Home.Mods" : "Home.Active"),captionStyle);
            }
            Rect donate = new Rect(row.xMax-donationWidth,row.y,donationWidth,height);
            bool hover = donate.Contains(Event.current.mousePosition);
            UI.DrawGlass(donate,hover ? MenuStyle.Raised : MenuStyle.Surface,Mathf.RoundToInt(16f*unit));
            DrawDonateOrbit(donate,16f*unit);
            float heart = Mathf.Min(38f*unit,height*.45f);
            DrawMenuIcon(new Rect(donate.center.x-heart*.5f,donate.y+11f*unit,heart,heart),4);
            GUI.Label(new Rect(donate.x,donate.yMax-UI.Px(9)*2-8f*unit,donate.width,UI.Px(9)*2+4f*unit),I18n.T("Core.donate_caption"),UI.Sty(Theme.Text,9,true,TextAnchor.MiddleCenter));
            if (ControlButton(donate,new GUIContent("",I18n.T("Core.glass_support")),GUIStyle.none)) Application.OpenURL(DonateUrl);
        }

        void DrawDonateOrbit(Rect rect,float radius)
        {
            if (Event.current.type != EventType.Repaint || _navigationIcons[17] == null) return;
            float straightX = rect.width - radius*2f;
            float straightY = rect.height - radius*2f;
            float arc = Mathf.PI*radius*.5f;
            float perimeter = 2f*(straightX+straightY)+4f*arc;
            float head = Mathf.Repeat(Time.unscaledTime*.18f,1f);
            Color previous = GUI.color;
            for(int i=0;i<48;i++)
            {
                float distance = Mathf.Repeat(head-i*.004f,1f)*perimeter;
                Vector2 p=BorderPoint(rect,radius,straightX,straightY,arc,distance);
                Color color = Theme.Accent; color.a *= 1f-(float)i/48f;
                GUI.color=previous*color;
                float diameter = 3.5f*UI.Scale;
                GUI.DrawTexture(new Rect(p.x-diameter*.5f,p.y-diameter*.5f,diameter,diameter),_navigationIcons[17]);
            }
            GUI.color=previous;
        }
        static Vector2 BorderPoint(Rect rect,float r,float horizontal,float vertical,float arc,float distance)
        {
            int segment=0;
            float length=horizontal;
            while(segment<7 && distance>length)
            {
                distance-=length;segment++;
                length=segment%2==1?arc:segment%4==0?horizontal:vertical;
            }
            float t=length>0f?distance/length:0f;
            switch(segment)
            {
                case 0:return new Vector2(rect.x+r+t*horizontal,rect.y);
                case 2:return new Vector2(rect.xMax,rect.y+r+t*vertical);
                case 4:return new Vector2(rect.xMax-r-t*horizontal,rect.yMax);
                case 6:return new Vector2(rect.x,rect.yMax-r-t*vertical);
            }
            Vector2 center=segment==1?new Vector2(rect.xMax-r,rect.y+r):segment==3?new Vector2(rect.xMax-r,rect.yMax-r):segment==5?new Vector2(rect.x+r,rect.yMax-r):new Vector2(rect.x+r,rect.y+r);
            float angle=(-Mathf.PI*.5f+(segment-1)*Mathf.PI*.25f)+t*Mathf.PI*.5f;
            return center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*r;
        }
    }
}
