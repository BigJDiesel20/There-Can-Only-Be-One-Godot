/// <summary>
/// Contract for every global game-flow state (Lobby, PreGame, Battle, PostGame, …).
/// Ported from Unity's IGameState. The GameManager owns one instance of each and
/// drives OnLoad/OnUpdate/OnExit through ChangeState.
/// </summary>
public interface IGameState
{
    GameManager gameManager { get; set; }

    void OnLoad();
    void OnUpdate();
    void OnExit();
}
