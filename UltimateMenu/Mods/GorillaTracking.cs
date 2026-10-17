using System;
using System.Reflection;
using UnityEngine;

namespace UltimateMenu
{
    // Optional OpenVR reader: no Valve types in the assembly metadata.
    internal sealed class GorillaTracking
    {
        object _system;
        Array _poses;
        MethodInfo _read,_deviceClass;
        FieldInfo _connected,_valid,_matrix;
        FieldInfo[] _elements;
        object[] _readArgs;
        readonly object[] _classArgs=new object[1];
        bool _stopped;

        internal void Stop(){_stopped=true;_system=null;_poses=null;}
        bool Initialize()
        {
            if(_stopped)return false;
            if(_system!=null)return true;
            Type openVR=null;
            foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                openVR=assembly.GetType("Valve.VR.OpenVR",false);
                if(openVR!=null)break;
            }
            if(openVR==null)return false;
            var system=openVR.GetProperty("System",BindingFlags.Public|BindingFlags.Static)?.GetValue(null);
            if(system==null)return false;
            var type=system.GetType();
            _read=type.GetMethod("GetDeviceToAbsoluteTrackingPose");
            _deviceClass=type.GetMethod("GetTrackedDeviceClass");
            if(_read==null||_deviceClass==null)return false;
            var parameters=_read.GetParameters();
            Type poseType=parameters[2].ParameterType.GetElementType();
            _poses=Array.CreateInstance(poseType,64);
            _connected=poseType.GetField("bDeviceIsConnected");_valid=poseType.GetField("bPoseIsValid");
            _matrix=poseType.GetField("mDeviceToAbsoluteTracking");
            _elements=new FieldInfo[12];
            for(int i=0;i<12;i++)_elements[i]=_matrix.FieldType.GetField("m"+i);
            _readArgs=new[]{Enum.ToObject(parameters[0].ParameterType,1),(object)0f,_poses};
            _system=system;
            return true;
        }
        internal int Read(TrackerData[] trackers,uint[] active)
        {
            if(!Initialize())return 0;
            _read.Invoke(_system,_readArgs);
            int count=0;
            for(int i=0;i<Math.Min(_poses.Length,trackers.Length);i++)
            {
                var tracker=trackers[i];tracker.IsConnected=false;tracker.IsValid=false;
                object pose=_poses.GetValue(i);
                if(!(bool)_connected.GetValue(pose)||!(bool)_valid.GetValue(pose))continue;
                _classArgs[0]=(uint)i;
                // OpenVR GenericTracker = 3; controllers and the headset are not body trackers.
                if(Convert.ToInt32(_deviceClass.Invoke(_system,_classArgs))!=3)continue;
                object matrix=_matrix.GetValue(pose);
                float M(int index)=>(float)_elements[index].GetValue(matrix);
                Vector3 position=new Vector3(M(3),M(7),-M(11));
                Vector3 forward=new Vector3(M(2),M(6),-M(10)),up=new Vector3(M(1),M(5),-M(9));
                if(!Finite(position)||!Finite(forward)||!Finite(up)||forward.sqrMagnitude<.001f||up.sqrMagnitude<.001f)continue;
                tracker.RawPosition=position;tracker.RawRotation=Quaternion.LookRotation(forward,up);
                tracker.IsConnected=tracker.IsValid=true;
                if(string.IsNullOrEmpty(tracker.Serial))tracker.Serial="Tracker "+i;
                active[count++]=(uint)i;
            }
            return count;
        }
        static bool Finite(Vector3 value)=>Finite(value.x)&&Finite(value.y)&&Finite(value.z);
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    }
}
