using System;
using UnityEditor;
using UnityEngine;

// Lightweight checks of production math; network and headset acceptance remain manual.
public static class VRControlPolishValidation
{
    [MenuItem("Tools/Laser Tag/Validate VR Control Polish")]
    public static void Validate()
    {
        int checks=0;
        Action<bool,string> check=(ok,label)=>{if(!ok)throw new InvalidOperationException(label);checks++;};
        foreach(float x in new[]{-.18f,-.1f,0,.1f,.18f})
            check(Mathf.Abs(RelayAvatarPlayer.AnalogTurn(x,.18f))<.0001f,"deadzone / release");
        check(Mathf.Abs(RelayAvatarPlayer.AnalogTurn(.59f,.18f)-.5f)<.0001f,"proportional analog input");
        check(RelayAvatarPlayer.AnalogTurn(-1,.18f)==-1 && RelayAvatarPlayer.AnalogTurn(1,.18f)==1,"both directions");
        foreach(float hz in new[]{72f,90f,120f})
        {
            Vector3 origin=new Vector3(3,0,7),eye=new Vector3(.9f,1.7f,-.6f);
            float yaw=37;
            Vector3 pivot=origin+Quaternion.Euler(0,yaw,0)*new Vector3(eye.x,0,eye.z);
            for(int i=0;i<(int)(hz*10);i++)
                RelayAvatarPlayer.CalculateTurnAroundHead(origin,yaw,eye,90/hz,out origin,out yaw);
            Vector3 after=origin+Quaternion.Euler(0,yaw,0)*new Vector3(eye.x,0,eye.z);
            check(Vector3.Distance(pivot,after)<.002f,"tracked eye pivot drifts at "+hz+"Hz");
            check(Mathf.Abs(Mathf.DeltaAngle(yaw,217))<.01f,"frame independent angular speed");
        }
        for(int used=0;used<=7;used++)
            check(RelayAvatarPlayer.ChargesAt(used,7,14,10)==7-used,"burst count "+used);
        check(RelayAvatarPlayer.ChargesAt(7,7,14,13.999)==0,"eighth teleport remains locked");
        check(RelayAvatarPlayer.ChargesAt(7,7,14,14)==7,"cooldown deadline restores charges");
        check(RelayAvatarPlayer.ChargesAt(3,7,14,14)==7,"idle deadline restores partial burst");
        check(RelayAvatarPlayer.ChargesAt(9,7,14,12)==0,"no negative charges");
        Debug.Log("VR_CONTROL_POLISH_CHECKS_PASSED checks="+checks);
    }
    public static void ValidateInBatch()
    {
        Validate();
        ArenaTeleportValidation.ValidateInBatch();
    }
}
