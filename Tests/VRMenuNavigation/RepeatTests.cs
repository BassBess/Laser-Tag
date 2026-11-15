using System;
using LaserTag.Menu;
using UnityEngine;

// Standalone managed tests against the compiled production assembly.
public static class RepeatTests
{
    static int assertions;
    static void Check(bool condition,string message)
    {
        if(!condition)throw new Exception(message);
        assertions++;
    }
    public static void Main()
    {
        var repeat=new MenuAnalogNavigation.StickRepeat();
        Vector2Int direction;
        Check(!repeat.Step(new Vector2(.49f,.49f),0,.5f,.35f,.15f,out direction),"Deadzone must reject diagonal noise");
        Check(repeat.Step(Vector2.up,0,.5f,.35f,.15f,out direction) && direction==Vector2Int.up,"Flick must move immediately");
        Check(!repeat.Step(Vector2.up,.34f,.5f,.35f,.15f,out direction),"Hold must wait for initial delay");
        Check(repeat.Step(Vector2.up,.351f,.5f,.35f,.15f,out direction),"Initial hold repeat missing");
        Check(!repeat.Step(Vector2.up,.49f,.5f,.35f,.15f,out direction),"Repeat interval must be enforced");
        Check(repeat.Step(Vector2.up,.502f,.5f,.35f,.15f,out direction),"Subsequent repeat missing");
        Check(repeat.Step(Vector2.up,10,.5f,.35f,.15f,out direction),"Slow frame should make one movement");
        Check(!repeat.Step(Vector2.up,10,.5f,.35f,.15f,out direction),"Slow frame must not accumulate skipped movement");
        Check(!repeat.Step(Vector2.zero,10.01f,.5f,.35f,.15f,out direction),"Release must stop repeating");
        Check(repeat.Step(Vector2.up,10.02f,.5f,.35f,.15f,out direction),"New flick must respond immediately");
        Check(repeat.Step(Vector2.left,10.03f,.5f,.35f,.15f,out direction) && direction==Vector2Int.left,"Direction change must respond immediately");
        Check(MenuAnalogNavigation.Direction(new Vector2(.7f,.9f),.5f)==Vector2Int.up,"Dominant vertical axis");
        Check(MenuAnalogNavigation.Direction(new Vector2(-.9f,.7f),.5f)==Vector2Int.left,"Dominant horizontal axis");
        Check(MenuAnalogNavigation.Direction(new Vector2(.8f,.8f),.5f)==Vector2Int.up,"Tie must produce one direction");
        repeat.Reset();
        Check(repeat.Step(Vector2.left,10.04f,.5f,.35f,.15f,out direction),"Panel change must reset repeat state");
        Console.WriteLine("PASS: "+assertions+" navigation timing/direction assertions");
    }
}
