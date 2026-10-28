using System;
using UnityEditor;
using UnityEngine;

// Avatar-only conversion. Reuses every existing mesh/material and reference.
public static class ScoutLegRigSetup
{
    public static ScoutProceduralLegs Configure(ScoutAvatar avatar)
    {
        if (!avatar) throw new ArgumentNullException(nameof(avatar));
        ScoutProceduralLegs rig = avatar.GetComponent<ScoutProceduralLegs>();
        if (rig && rig.IsConfigured) return rig;
        float size = avatar.StandingHeadHeight / 1.65f;
        if (!rig) rig = avatar.gameObject.AddComponent<ScoutProceduralLegs>();
        rig.avatar = avatar;
        rig.pelvis = Pivot(avatar.transform, "PelvisMotion", Vector3.zero);
        foreach (string name in new[] { "PelvicUndersuit", "PelvicFrontGuard", "LeftIliacGuard", "RightIliacGuard", "SacralGuard", "LeftIliacFlange", "RightIliacFlange" })
            Reparent(avatar.transform, name, rig.pelvis);
        rig.left = ConfigureLeg(avatar.transform, "Left", size);
        rig.right = ConfigureLeg(avatar.transform, "Right", size);
        rig.thighLength = .43f * size; rig.shinLength = .35f * size; rig.ankleHeight = .145f * size;
        rig.standingDrop = .012f * size;
        rig.soleExtents = new Vector3(.073f * .89f, .190f * .95f, .091f * .95f) * size;
        EditorUtility.SetDirty(rig);
        return rig;
    }
    static ScoutProceduralLegs.Leg ConfigureLeg(Transform root, string side, float size)
    {
        Transform leg = root.Find(side + "Leg");
        if (!leg) throw new InvalidOperationException("Expected the existing separate-mesh " + side + "Leg.");
        Transform thigh = Pivot(leg, side + "HipPivot", Vector3.up * (.93f * size));
        Transform shin = Pivot(leg, side + "ShinPivot", Vector3.up * (.50f * size));
        Transform knee = Pivot(leg, side + "KneePivot", Vector3.up * (.50f * size));
        Transform boot = Pivot(leg, side + "AnklePivot", Vector3.up * (.15f * size));
        foreach (string part in new[] { "ThighUndersuit", "QuadricepsArmor", "OuterThighArmor", "HamstringArmor" }) Reparent(leg, side + part, thigh);
        foreach (string part in new[] { "CalfUndersuit", "ShinArmor", "CalfGuard", "ShinViolet" }) Reparent(leg, side + part, shin);
        foreach (string part in new[] { "KneeFlexSeal", "Patella" }) Reparent(leg, side + part, knee);
        foreach (string part in new[] { "Boot", "ToeAndInstep", "HeelCounter", "AnkleCuff", "Outsole" }) Reparent(leg, side + part, boot);
        return new ScoutProceduralLegs.Leg { thigh = thigh, shin = shin, knee = knee, boot = boot, hipLocal = leg.localPosition + Vector3.up * (.93f * size) };
    }
    static Transform Pivot(Transform parent, string name, Vector3 local)
    {
        Transform existing = parent.Find(name);
        if (existing) return existing;
        Transform pivot = new GameObject(name).transform;
        pivot.SetParent(parent, false); pivot.localPosition = local;
        return pivot;
    }
    static void Reparent(Transform parent, string name, Transform pivot)
    {
        Transform part = parent.Find(name);
        if (!part)
        {
            if (pivot.Find(name)) return;
            throw new InvalidOperationException("Missing avatar mesh part " + name);
        }
        part.SetParent(pivot, true);
    }
}
