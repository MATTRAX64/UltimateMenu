using UnityEngine;

namespace UltimateMenu
{
    public sealed partial class GorillaFaceTracking : MonoBehaviour
    {
        const string Title = "FaceTracking.Title";
        const int Cell = 64, Atlas = Cell * 4;
        readonly Color32[] _pixels = new Color32[Atlas * Atlas];
        readonly Color32[] _portraitPixels = new Color32[Cell * Cell];
        Texture2D _texture, _portrait;
        MeshRenderer _face;
        Material _preview, _previous;
        int _textureSlot;
        const float NeutralGazeY = -.2f;
        Vector2 _gaze;
        float _pupilSize = 1f;
        float _lastPupilSize;
        float _left, _right, _browLeft, _browRight;
        bool _mouthOpen;
        float _browTiltLeft, _browTiltRight;
        const int NeutralRigMouth = 10;
        int _rigMouthPose = NeutralRigMouth, _lastRigMouthFrame = -1;
        bool _rigLoaded;
        Color32[] _rigMouthPixels, _rigBasePixels, _rigOriginalPixels, _rigEyeMask, _rigEyes;
        Vector4 _lastAngles = Vector4.one * -999;
        static readonly string[] RigMouthPoses =
        {
            "FaceTracking.Smile", "FaceTracking.SmileSlightlyOpen", "FaceTracking.SmileOpen",
            "FaceTracking.MouthTeeth", "FaceTracking.MouthTeethOpen", "FaceTracking.MouthWide",
            "FaceTracking.MouthOpen", "FaceTracking.MouthTall", "FaceTracking.Frown",
            "FaceTracking.Round", "FaceTracking.MouthNeutral"
        };
        Vector2Int _lastGaze = new Vector2Int(999, 999);
        Vector4 _lastShape = Vector4.one * -999;
        bool _registered;
        bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled(Title);

        void Awake()
        {
            _trackEyes = MenuPrefs.GetInt(EyeTrackingPref) != 0;
            _trackMouth = MenuPrefs.GetInt(MouthTrackingPref) != 0;
        }

        void LateUpdate()
        {
            if (!_registered && MenuUI.Instance != null)
            {
                MenuUI.Instance.RegisterPage(Title, DrawPage, MenuCategory.Tracking);
                _registered = true;
            }
            if (!Enabled || !EnsureRigFace())
            {
                StopFaceInput();
                UpdateFaceNetwork(false);
                Restore();
                return;
            }
            PollFaceInput();
            EnsureTexture();
            DrawPixels();
            UpdateFaceNetwork(true);
            if (GorillaFaces.Instance == null || !GorillaFaces.Instance.TryGetFaceSurface(out var face, out var original))
            { Restore(); return; }
            if (_face != face || _preview == null)
            {
                Restore();
                _face = face;
                _preview = new Material(original) { name = "UltimateMenu · Pixel Face" };
                if (_preview.HasProperty("_MouthMap")) _textureSlot = Shader.PropertyToID("_MouthMap");
                else if (_preview.HasProperty("_BaseMap")) _textureSlot = Shader.PropertyToID("_BaseMap");
                else if (_preview.HasProperty("_MainTex")) _textureSlot = Shader.PropertyToID("_MainTex");
                else { Restore(); return; }
                _preview.SetTexture(_textureSlot, _texture);
            }
            if (!UsesTrackingTexture(_face.sharedMaterial))
            {
                _previous = _face.sharedMaterial;
                _face.sharedMaterial = _preview;
            }
        }

        void EnsureTexture()
        {
            if (_texture != null) return;
            _texture = new Texture2D(Atlas, Atlas, TextureFormat.RGBA32, false)
            { name = "UltimateMenu Pixel Face Atlas", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            _portrait = new Texture2D(Cell, Cell, TextureFormat.RGBA32, false)
            { name = "UltimateMenu Face Portrait", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        }
        void DrawPixels()
        {
            float gazeY = _gaze.y < 0f
                ? Mathf.Lerp(NeutralGazeY, -1f, -_gaze.y)
                : Mathf.Lerp(NeutralGazeY, 1f, _gaze.y);
            Vector2 aim = _trackEyes && Fresh(_liveGazeTime) ? _liveGaze : new Vector2(_gaze.x, gazeY);
            var gaze = new Vector2Int(Mathf.RoundToInt(aim.x * 4f), Mathf.RoundToInt(aim.y * 3f));
            // The texture is viewed from the front, so its left half is the player's right eye.
            float leftLid = _trackEyes ? Weight("EyesClosedR", _left) : _left;
            float rightLid = _trackEyes ? Weight("EyesClosedL", _right) : _right;
            float leftBrow = _trackEyes ? Brow("R", _browLeft) : _browLeft;
            float rightBrow = _trackEyes ? Brow("L", _browRight) : _browRight;
            var shape = new Vector4(Mathf.Round(leftLid * 60f) / 10f, Mathf.Round(rightLid * 60f) / 10f,
                Mathf.Round(leftBrow * 4f), Mathf.Round(rightBrow * 4f));
            int rigFrame = _trackMouth ? TrackedMouth() : _rigMouthPose;
            var angles = new Vector4(Mathf.Round(_browTiltLeft), Mathf.Round(_browTiltRight), 0f, 0f);
            float pupilSize = Mathf.Round(_pupilSize * 20f) / 20f;
            if (gaze == _lastGaze && shape == _lastShape && angles == _lastAngles && rigFrame == _lastRigMouthFrame && pupilSize == _lastPupilSize) return;
            _lastPupilSize = pupilSize;
            _lastAngles = angles; _lastRigMouthFrame = rigFrame;
            _lastGaze = gaze;
            _lastShape = shape;
            RenderFace(_texture, true, gaze, shape, angles, pupilSize, rigFrame);
        }

        void RenderFace(Texture2D texture, bool portrait, Vector2Int gaze, Vector4 shape, Vector4 angles, float pupilSize, int rigFrame)
        {
            bool originalMouth = rigFrame == NeutralRigMouth;
            float leftSlope = Mathf.Tan(angles.x * Mathf.Deg2Rad), rightSlope = Mathf.Tan(angles.y * Mathf.Deg2Rad);
            var eyePose = new Vector3(gaze.x, gaze.y, pupilSize);
            // GorillaFaces uses a 4x4 mouth atlas. Repeat the live expression in each cell
            // so the game's voice-driven tile changes do not change the prototype's pose.
            for (int y = 0; y < Cell; y++) for (int x = 0; x < Cell; x++)
            {
                Color32 pixel = DrawRigFacePixel(x, y, x, y, eyePose, x < 30 ? shape.x : shape.y, rigFrame, originalMouth);
                bool left = x < 30;
                int dx = x - (left ? 21 : 39);
                float browY = 40f + (left ? shape.z : shape.w);
                float browLine = Mathf.Clamp(browY - Mathf.Abs(dx) / 5f + dx * (left ? leftSlope : rightSlope), 39f, 44f);
                if (Mathf.Abs(dx) <= 5)
                {
                    float coverage = Mathf.Clamp01(1.25f - Mathf.Abs(y - browLine));
                    pixel = Color32.Lerp(pixel, new Color32(24, 24, 27, 255), coverage);
                }
                if (portrait)
                {
                    float headX = (x - 31f) / (y >= 23 && y <= 39 ? 31f : 27f);
                    float headY = (y - 32f) / 32f;
                    _portraitPixels[y * Cell + x] = headX * headX + headY * headY > 1f ? default : pixel;
                }
                // The in-game UVs sit one texel lower than the portrait. Shift each
                // tile up independently and extend its bottom edge by one texel.
                if (y < Cell - 1)
                {
                    for (int ty = 0; ty < 4; ty++) for (int tx = 0; tx < 4; tx++)
                    {
                        int bottom = (ty * Cell) * Atlas + x + tx * Cell;
                        _pixels[bottom + (y + 1) * Atlas] = pixel;
                        if (y == 0) _pixels[bottom] = pixel;
                    }
                }
            }
            texture.SetPixels32(_pixels);
            texture.Apply(false, false);
            if (portrait)
            {
                _portrait.SetPixels32(_portraitPixels);
                _portrait.Apply(false, false);
            }
        }

        Color32 DrawRigFacePixel(int x, int y, int mouthX, int mouthY, Vector3 eyePose, float lid,
            int mouthPose, bool originalMouth)
        {
            int index = (y + 1) * Cell + x;
            Color32 original = _rigOriginalPixels[index];
            Color32 clean = _rigBasePixels[index];
            Color32 pixel = !originalMouth && y >= 6 && y <= 9 ? clean : original;
            bool leftEye = x < 30;
            int eyeX = leftEye ? 21 : 39;
            const int eyeY = 33;
            const int closedLine = 32;
            float closure = lid / 6f;
            float upperEdge = Mathf.Lerp(42f, closedLine, closure);
            float lowerEdge = Mathf.Lerp(24f, closedLine, closure);
            Color32 eyeMask = _rigEyeMask[index];
            if (eyeMask.g > 127 && eyeMask.r <= 127 && original.r < 105 && clean.r > original.r)
                pixel = Color32.Lerp(original, clean, .55f);
            if (eyeMask.g > 127 && lid > 0f && (y >= upperEdge || y <= lowerEdge))
            {
                bool crease = lid >= 6f && y == closedLine && eyeMask.r > 127;
                pixel = crease ? new Color32(45, 40, 37, 255) : clean;
            }
            else if (eyeMask.r > 127)
            {
                // Like the Blender shader, use a stationary sclera beneath the moving pupil texture.
                pixel = new Color32(235, 233, 229, 255);
                float pupilSize = eyePose.z;
                var eyeTravel = Vector2.ClampMagnitude(new Vector2(eyePose.x, eyePose.y), 2.5f / Mathf.Max(1f, pupilSize));
                int pupilX = Mathf.RoundToInt(10.5f + (x - eyeX - eyeTravel.x) * 2f / pupilSize);
                int pupilY = Mathf.RoundToInt(10.5f + (y - eyeY - eyeTravel.y) * 2f / pupilSize);
                if (pupilX >= 0 && pupilX < 22 && pupilY >= 0 && pupilY < 22)
                    pixel = Composite(pixel, _rigEyes[(56 + pupilY) * 128 + (leftEye ? 32 : 68) + pupilX]);
            }
            if (originalMouth)
            {
                // Snap only the retouched lip pixels to solid shades. The nose and
                // the muzzle around the original mouth keep their original pixels.
                if (y >= 6 && y <= 9 && clean.r - original.r >= 26)
                {
                    byte shade = original.r < 65 ? (byte)36 : original.r < 105 ? (byte)76 : (byte)112;
                    return new Color32(shade, shade, shade, 255);
                }
                return pixel;
            }
            if (mouthX < 0 || mouthX >= Cell || mouthY < 0 || mouthY >= Cell)
                return pixel;
            int mouthSheetWidth = 128 * RigMouthPoses.Length;
            Color32 mouth = _rigMouthPixels[(mouthY * 2 + 1) * mouthSheetWidth + mouthPose * 128 + mouthX * 2 + 1];
            // Keep a hard pixel edge between the sprite and the face.
            return mouth.a < 128 ? pixel : new Color32(mouth.r, mouth.g, mouth.b, 255);
        }

        static Color32 Composite(Color32 background, Color32 foreground)
        {
            if (foreground.a == 0) return background;
            return Color32.Lerp(background, new Color32(foreground.r, foreground.g, foreground.b, 255), foreground.a / 255f);
        }

        bool EnsureRigFace()
        {
            if (!_rigLoaded)
            {
                _rigLoaded = true;
                _rigBasePixels = LoadRigPixels("FaceBase", 64, 65);
                _rigOriginalPixels = LoadRigPixels("FaceOriginal", 64, 65);
                _rigEyeMask = LoadRigPixels("EyeMask", 64, 65);
                _rigEyes = LoadRigPixels("Pupils", 128, 128);
                _rigMouthPixels = LoadRigPixels("MouthAtlas", 128 * RigMouthPoses.Length, 128);
            }
            return _rigBasePixels != null && _rigOriginalPixels != null && _rigEyeMask != null
                && _rigEyes != null && _rigMouthPixels != null;
        }

        Color32[] LoadRigPixels(string asset, int width, int height)
        {
            Texture2D sheet = null;
            try
            {
                using var resource = typeof(GorillaFaceTracking).Assembly.GetManifestResourceStream("UltimateMenu.Assets.GorillaFaceTracking." + asset + ".png");
                if (resource == null) return null;
                using var bytes = new System.IO.MemoryStream();
                resource.CopyTo(bytes);
                sheet = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!sheet.LoadImage(bytes.ToArray()) || sheet.width != width || sheet.height != height) return null;
                return sheet.GetPixels32();
            }
            catch (System.Exception e) { Log.Err(asset, e); return null; }
            finally { if (sheet != null) Destroy(sheet); }
        }

        // Reading Renderer.material can clone the tracking material. Recognize copies
        // by the owned texture so a copy is never saved as the player's previous face.
        bool UsesTrackingTexture(Material material) => _preview != null && material != null
            && material.HasProperty(_textureSlot) && material.GetTexture(_textureSlot) == _texture;

        void Restore()
        {
            if (_face != null && UsesTrackingTexture(_face.sharedMaterial))
                _face.sharedMaterial = _previous;
            if (_preview != null) Destroy(_preview);
            _face = null; _previous = _preview = null;
        }
        void OnDisable()
        {
            StopFaceInput();
            StopFaceNetwork();
            Restore();
        }
        void OnDestroy()
        {
            Restore();
            if (_texture != null) Destroy(_texture);
            if (_portrait != null) Destroy(_portrait);
        }

        void DrawPage(MenuUI ui)
        {
            ui.Section(I18n.T("FaceTracking.Preview"));
            ui.BeginCard();
            if (!EnsureRigFace())
            {
                ui.Label(I18n.T("Face.RigNotFound"), Theme.Dim, 9);
                ui.EndCard();
                return;
            }
            EnsureTexture(); DrawPixels();
            GUILayout.BeginHorizontal();
            var rect = GUILayoutUtility.GetRect(110f * UI.Scale, 110f * UI.Scale, GUILayout.ExpandWidth(false));
            if (Event.current.type == EventType.Repaint) GUI.DrawTexture(rect, _portrait, ScaleMode.ScaleToFit);
            GUILayout.Space(10f * UI.Scale);
            GUILayout.BeginVertical();
            ui.Label(I18n.T("FaceTracking.Gaze"), Theme.Text, 10, true);
            _gaze = ui.DrawFaceGazePad(_gaze, "gaze");
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            bool eyes = ui.Toggle(I18n.T("FaceTracking.LiveEyes"), _trackEyes);
            bool mouth = ui.Toggle(I18n.T("FaceTracking.LiveMouth"), _trackMouth);
            if (eyes != _trackEyes) MenuPrefs.SetInt(EyeTrackingPref, (_trackEyes = eyes) ? 1 : 0);
            if (mouth != _trackMouth) MenuPrefs.SetInt(MouthTrackingPref, (_trackMouth = mouth) ? 1 : 0);
            if (_trackEyes || _trackMouth)
            {
                ui.Label(I18n.T("FaceTracking.Hardware"), Theme.Dim, 9);
                if (_oscUnavailable) ui.Label(I18n.T("FaceTracking.OscBusy"), Theme.Dim, 9);
                else if (_trackEyes && (Fresh(_liveGazeTime) || Weight("EyesClosedL", -1f) >= 0f || Weight("EyesClosedR", -1f) >= 0f))
                    ui.Label(I18n.T("FaceTracking.OscEyes"), Theme.Dim, 9);
                if (_trackMouth && Weight("JawDrop", -1f) >= 0f)
                    ui.Label(I18n.T("FaceTracking.OscMouth"), Theme.Dim, 9);
                if (!_oscUnavailable && !Fresh(_liveGazeTime) && Weight("EyesClosedL", -1f) < 0f
                    && Weight("EyesClosedR", -1f) < 0f && Weight("JawDrop", -1f) < 0f)
                    ui.Label(I18n.T("FaceTracking.OscWait"), Theme.Dim, 9);
            }
            _pupilSize = ui.Slider(I18n.T("FaceTracking.PupilSize"), _pupilSize, .5f, 1.25f, "0.00x");
            _left=ui.Slider(I18n.T("FaceTracking.Left"),_left,0,1,"0.00");
            _right=ui.Slider(I18n.T("FaceTracking.Right"),_right,0,1,"0.00");
            const float browNeutral = 1f;
            _browLeft = ui.Slider(I18n.T("FaceTracking.BrowLeft"), _browLeft + browNeutral, browNeutral - 1f, browNeutral + 1f, "0.00") - browNeutral;
            _browRight = ui.Slider(I18n.T("FaceTracking.BrowRight"), _browRight + browNeutral, browNeutral - 1f, browNeutral + 1f, "0.00") - browNeutral;
            _browTiltLeft=ui.Slider(I18n.T("FaceTracking.TiltLeft"),_browTiltLeft,-35,35,"0°");
            _browTiltRight=ui.Slider(I18n.T("FaceTracking.TiltRight"),_browTiltRight,-35,35,"0°");
            ui.Label(I18n.T(!Enabled?"FaceTracking.Enable":_preview!=null?"FaceTracking.Ready":"Face.RigNotFound"),Theme.Dim,9);
            ui.EndCard();
            ui.ExpandableSection(I18n.T("FaceTracking.RigFrame"), I18n.T(RigMouthPoses[_rigMouthPose]), ref _mouthOpen, () =>
            {
                _rigMouthPose = ui.ModePicker(System.Array.ConvertAll(RigMouthPoses, key => I18n.T(key)), _rigMouthPose, 2);
            });
            if(ui.Btn(I18n.T("FaceTracking.Reset")))
            {
                _gaze = Vector2.zero;
                _left = _right = _browLeft = _browRight = _browTiltLeft = _browTiltRight = 0f;
                _rigMouthPose = NeutralRigMouth;
                _pupilSize = 1f;
            }
        }
    }

    public partial class MenuUI
    {
        internal Vector2 DrawFaceGazePad(Vector2 value, string controlKey)
        {
            float size = 92f * UI.Scale;
            Rect pad = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
            int id = GUIUtility.GetControlID(FocusType.Passive, pad);
            var ev = Event.current;
            if (!_vrInDraw)
            {
                if (ev.type == EventType.MouseDown && ev.button == 0 && pad.Contains(ev.mousePosition)) { GUIUtility.hotControl = id; }
                if (GUIUtility.hotControl == id && (ev.type == EventType.MouseDown || ev.type == EventType.MouseDrag || ev.type == EventType.MouseUp))
                {
                    value = Vector2.ClampMagnitude((ev.mousePosition-pad.center)/(size*.42f)*new Vector2(1,-1),1f);
                    if (ev.type == EventType.MouseUp) GUIUtility.hotControl = 0;
                    ev.Use();
                }
            }
            UI.DrawGlass(pad,MenuStyle.Surface,Mathf.RoundToInt(size*.5f));
            if (_vrInDraw)
                for (int y=0;y<3;y++) for (int x=0;x<3;x++)
                {
                    float step=size/3f;
                    var cell=new Rect(pad.x+x*step,pad.y+y*step,step,step);
                    if(ControlButton(cell,new GUIContent("", controlKey+":"+x+":"+y),GUIStyle.none)) value=Vector2.ClampMagnitude(new Vector2(x-1,1-y),1f);
                }
            Vector2 dot=pad.center+new Vector2(value.x,-value.y)*(size*.42f);
            float radius=size*.085f;
            UI.DrawRounded(new Rect(dot.x-radius,dot.y-radius,2*radius,2*radius),Theme.Accent,Mathf.CeilToInt(radius));
            return value;
        }
    }
}
