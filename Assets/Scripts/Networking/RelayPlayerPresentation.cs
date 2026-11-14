using TMPro;
using Unity.Collections;
using UnityEngine;

// Presentation only. The existing network player owns the synchronized name and
// server-assigned color. No additional NetworkObject or tracking writer is used.
[DefaultExecutionOrder(150), DisallowMultipleComponent]
public sealed class RelayPlayerPresentation : MonoBehaviour
{
    static readonly Color[] Palette = {
        new Color(.10f,.85f,1f), new Color(1f,.28f,.12f),
        new Color(.65f,.30f,1f), new Color(.25f,1f,.30f),
        new Color(1f,.75f,.08f), new Color(1f,.25f,.65f),
        new Color(.20f,.45f,1f), new Color(.95f,.95f,.95f)
    };
    RelayCombatPlayer player;
    ScoutSuitAppearance remoteSuit, localSuit;
    Color originalLocalColor;
    bool originalLocalLights;
    TextMeshPro tag;
    byte appliedSlot = byte.MaxValue;
    FixedString32Bytes displayedName;
    bool hasName;

    public Color PlayerColor => Palette[player ? Mathf.Min(player.ColorSlot.Value, Palette.Length-1) : 0];

    public void Initialize(RelayCombatPlayer owner)
    {
        player=owner;
        remoteSuit=owner.avatar ? owner.avatar.GetComponentInChildren<ScoutSuitAppearance>(true) : null;
        if(!owner.IsOwner && !tag)
        {
            var label=new GameObject("Player Name Tag");
            label.transform.SetParent(transform,false);
            tag=label.AddComponent<TextMeshPro>();
            tag.rectTransform.sizeDelta=new Vector2(.8f,.14f);
            tag.fontSize=1.1f;
            tag.alignment=TextAlignmentOptions.Center;
            tag.richText=false;
            tag.textWrappingMode=TextWrappingModes.NoWrap;
            tag.raycastTarget=false;
            // Default world text uses depth testing: cover still hides the label.
        }
        RefreshColor();
    }

    void LateUpdate()
    {
        if(!player || !player.IsSpawned)return;
        if(player.IsOwner && !localSuit && LocalPlayerRig.Instance && LocalPlayerRig.Instance.avatar)
        {
            localSuit=LocalPlayerRig.Instance.avatar.GetComponentInChildren<ScoutSuitAppearance>(true);
            if(localSuit)
            {
                originalLocalColor=localSuit.accentColor;originalLocalLights=localSuit.showAccentLights;
                RefreshColor();
            }
        }
        if(appliedSlot!=player.ColorSlot.Value)RefreshColor();
        if(!tag)return;
        bool visible=player.IsPresentInWorld && player.avatar && player.avatar.head;
        tag.gameObject.SetActive(visible);
        if(!visible)return;
        var currentName=player.DisplayName.Value;
        if(!hasName || !displayedName.Equals(currentName))
        {displayedName=currentName;hasName=true;tag.text=currentName.ToString();}
        tag.transform.position=player.avatar.head.position+Vector3.up*.27f;
        var camera=LocalPlayerRig.Instance ? LocalPlayerRig.Instance.playerCamera : Camera.main;
        if(camera)tag.transform.rotation=camera.transform.rotation;
    }

    void RefreshColor()
    {
        appliedSlot=player.ColorSlot.Value;
        Apply(remoteSuit);Apply(localSuit);
        if(tag)tag.color=PlayerColor;
    }
    void Apply(ScoutSuitAppearance suit)
    {
        if(!suit)return;
        suit.showAccentLights=true;suit.accentColor=PlayerColor;suit.ApplyColors();
    }
    void OnDestroy()
    {
        if(tag)Destroy(tag.gameObject);
        if(localSuit)
        {
            localSuit.accentColor=originalLocalColor;localSuit.showAccentLights=originalLocalLights;
            localSuit.ApplyColors();
        }
    }
}
