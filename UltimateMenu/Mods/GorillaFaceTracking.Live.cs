using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace UltimateMenu
{
    public sealed partial class GorillaFaceTracking
    {
        const int FaceOscPort = 9015;
        const string EyeTrackingPref = "MUM_LiveFaceEyes";
        const string MouthTrackingPref = "MUM_LiveFaceMouth";
        bool _trackEyes, _trackMouth;
        UdpClient _faceOsc;
        float _retryOsc;
        bool _oscUnavailable;
        readonly Dictionary<string, (float value, float time)> _faceInputs = new Dictionary<string, (float, float)>(StringComparer.Ordinal);
        Vector2 _liveGaze;
        float _liveGazeTime = -10f;

        void PollFaceInput()
        {
            if (!_trackEyes && !_trackMouth) { StopFaceInput(); return; }
            float now = Time.realtimeSinceStartup;
            if (_faceOsc == null)
            {
                if (now < _retryOsc) return;
                try
                {
                    _faceOsc = new UdpClient(new IPEndPoint(IPAddress.Loopback, FaceOscPort));
                    _oscUnavailable = false;
                }
                catch (SocketException)
                {
                    _oscUnavailable = true;
                    _retryOsc = now + 2f;
                    return;
                }
            }
            try
            {
                IPEndPoint sender = new IPEndPoint(IPAddress.Loopback, 0);
                for (int count = 0; count < 64 && _faceOsc.Available > 0; count++)
                {
                    byte[] packet = _faceOsc.Receive(ref sender);
                    if (IPAddress.IsLoopback(sender.Address)) ReadFaceOsc(packet, 0, packet.Length, now, 0);
                }
            }
            catch (SocketException)
            {
                StopFaceInput();
                _oscUnavailable = true;
                _retryOsc = now + 2f;
            }
        }

        void StopFaceInput()
        {
            _faceOsc?.Close();
            _faceOsc = null;
            _faceInputs.Clear();
            _liveGazeTime = -10f;
            _oscUnavailable = false;
        }

        static int OscInt(byte[] data, int offset) =>
            data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3];

        static bool ReadOscString(byte[] data, ref int offset, int end, out string value)
        {
            value = "";
            int start = offset;
            while (offset < end && data[offset] != 0) offset++;
            if (offset == end) return false;
            value = Encoding.ASCII.GetString(data, start, offset - start);
            offset = (offset + 4) & ~3;
            return offset <= end;
        }

        static float OscFloat(byte[] data, int offset) => BitConverter.Int32BitsToSingle(OscInt(data, offset));

        void ReadFaceOsc(byte[] data, int start, int end, float now, int depth)
        {
            if (depth > 3 || end - start > 8192 || !ReadOscString(data, ref start, end, out string address)) return;
            if (address == "#bundle")
            {
                if (end - start < 8) return;
                start += 8;
                while (end - start >= 4)
                {
                    int length = OscInt(data, start);
                    start += 4;
                    if (length <= 0 || (length & 3) != 0 || length > end - start) return;
                    ReadFaceOsc(data, start, start + length, now, depth + 1);
                    start += length;
                }
                return;
            }
            if (!ReadOscString(data, ref start, end, out string types)) return;
            if (address == "/sl/eyeTrackedGazePoint" && types == ",fff" && end - start >= 12)
            {
                float x = OscFloat(data, start), y = OscFloat(data, start + 4), z = OscFloat(data, start + 8);
                if (Finite(x) && Finite(y) && Finite(z) && z < -.0001f)
                {
                    _liveGaze = new Vector2(Mathf.Clamp(-Mathf.Atan2(x, -z) / .45f, -1f, 1f),
                        Mathf.Clamp(Mathf.Atan2(y, -z) / .45f, -1f, 1f));
                    _liveGazeTime = now;
                }
                return;
            }
            const string prefix = "/sl/xrfb/facew/";
            if (types != ",f" || end - start < 4 || !address.StartsWith(prefix, StringComparison.Ordinal)) return;
            string key = address.Substring(prefix.Length);
            switch (key)
            {
                case "EyesClosedL": case "EyesClosedR":
                case "InnerBrowRaiserL": case "InnerBrowRaiserR":
                case "OuterBrowRaiserL": case "OuterBrowRaiserR":
                case "BrowLowererL": case "BrowLowererR":
                case "JawDrop": case "LipCornerPullerL": case "LipCornerPullerR":
                case "LipCornerDepressorL": case "LipCornerDepressorR":
                case "LipPuckerL": case "LipPuckerR": case "LipFunnelerLT": case "LipFunnelerRT":
                case "LipStretcherL": case "LipStretcherR": case "TongueOut":
                    float value = OscFloat(data, start);
                    if (Finite(value)) _faceInputs[key] = (Mathf.Clamp01(value), now);
                    break;
            }
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        bool Fresh(float stamp) => Time.realtimeSinceStartup - stamp < .5f;
        float Weight(string key, float fallback = 0f) =>
            _faceInputs.TryGetValue(key, out var sample) && Fresh(sample.time) ? sample.value : fallback;

        float Brow(string side, float fallback)
        {
            float inner = Weight("InnerBrowRaiser" + side, -1f);
            float outer = Weight("OuterBrowRaiser" + side, -1f);
            float down = Weight("BrowLowerer" + side, -1f);
            if (inner < 0f && outer < 0f && down < 0f) return fallback;
            return Mathf.Max(0f, Mathf.Max(inner, outer)) - Mathf.Max(0f, down);
        }

        int TrackedMouth()
        {
            float jaw = Weight("JawDrop", -1f);
            if (jaw < 0f) return _rigMouthPose;
            float smile = (Weight("LipCornerPullerL") + Weight("LipCornerPullerR")) * .5f;
            float frown = (Weight("LipCornerDepressorL") + Weight("LipCornerDepressorR")) * .5f;
            float round = Mathf.Max((Weight("LipPuckerL") + Weight("LipPuckerR")) * .5f,
                (Weight("LipFunnelerLT") + Weight("LipFunnelerRT")) * .5f);
            float wide = (Weight("LipStretcherL") + Weight("LipStretcherR")) * .5f;
            if (round > .35f) return 9;
            if (frown > .4f && jaw < .2f) return 8;
            if (Weight("TongueOut") > .35f && jaw > .12f) return 4;
            if (smile > .28f) return jaw > .4f ? 2 : jaw > .14f ? 1 : smile > .65f ? 3 : 0;
            if (jaw > .65f) return wide > .35f ? 5 : 7;
            if (jaw > .16f) return 6;
            return NeutralRigMouth;
        }
    }
}
