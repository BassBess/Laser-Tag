using UnityEngine;

public class PlayerIdentity : MonoBehaviour
{
    [Header("Player Information")]
    [SerializeField] private int playerId;
    [SerializeField] private string displayName;

    public int PlayerId => playerId;
    public string DisplayName => displayName;

    public void Initialize(int id, string name)
    {
        playerId = id;
        displayName = name;

        gameObject.name = $"Player_{id}_{name}";
    }
}
