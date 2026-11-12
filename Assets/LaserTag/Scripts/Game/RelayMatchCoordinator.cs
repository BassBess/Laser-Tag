using System.Collections;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

// Scene-local match startup only. The menu retains connection/scene/player ownership.
public sealed class RelayMatchCoordinator : NetworkBehaviour
{
    public bool startAfterMenuLoading = true;
    public override void OnNetworkSpawn(){if(IsServer&&startAfterMenuLoading)StartCoroutine(BeginAfterPlayers());}
    IEnumerator BeginAfterPlayers()
    {
        var game=GetComponent<LaserTagGameManager>();
        while(IsSpawned)
        {
            if(game.Phase.Value!=GamePhase.Lobby)yield break;
            var menu=LaserTag.Menu.MenuSessionController.Instance;
            if(menu&&menu.Lobby&&menu.Lobby.Phase.Value==LaserTag.Menu.SessionPhase.Playing&&game.IsSpawned&&
               NetworkManager.ConnectedClients.Values.All(c=>c.PlayerObject&&c.PlayerObject.GetComponent<RelayCombatPlayer>())&&
               RelayCombatPlayer.All.Count>=game.minPlayersToStart)
            {game.ServerStartRound();yield break;}
            yield return null;
        }
    }
}
