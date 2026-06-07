// Per-player states + their contract. Ported verbatim from Unity (pure logic — each
// state just sets the PlayerInput context; Menu also raises OnMenuStateChanged). No
// engine dependencies.

/// <summary>Contract for every per-player state.</summary>
public interface IPlayerState
{
    void OnEnter(LocalPlayerManager player);
    void OnExit(LocalPlayerManager player);
}

/// <summary>Normal gameplay — full combat input live.</summary>
public class PlayerState_Battle : IPlayerState
{
    public void OnEnter(LocalPlayerManager player) => player.playerInput.Context = PlayerInputContext.Battle;
    public void OnExit(LocalPlayerManager player) { }
}

/// <summary>Knocked down — combat routed through Prone context (controllers gate actions).</summary>
public class PlayerState_Prone : IPlayerState
{
    public void OnEnter(LocalPlayerManager player) => player.playerInput.Context = PlayerInputContext.Prone;
    public void OnExit(LocalPlayerManager player) { }
}

/// <summary>Mid-combo — attack buttons live for buffering; movement/rotation axes zeroed.</summary>
public class PlayerState_Comboing : IPlayerState
{
    public void OnEnter(LocalPlayerManager player) => player.playerInput.Context = PlayerInputContext.Comboing;
    public void OnExit(LocalPlayerManager player) { }
}

/// <summary>Dialog open — combat blocked; only UI face-button input passes (GetUIButtonDown).</summary>
public class PlayerState_Dialog : IPlayerState
{
    public void OnEnter(LocalPlayerManager player) => player.playerInput.Context = PlayerInputContext.Dialog;
    public void OnExit(LocalPlayerManager player) { }
}

/// <summary>Eliminated — spectating; all combat input blocked.</summary>
public class PlayerState_Spectate : IPlayerState
{
    public void OnEnter(LocalPlayerManager player) => player.playerInput.Context = PlayerInputContext.Spectate;
    public void OnExit(LocalPlayerManager player) { }
}

/// <summary>Player Menu open — all combat/movement blocked; raises OnMenuStateChanged(true).</summary>
public class PlayerState_Menu : IPlayerState
{
    public void OnEnter(LocalPlayerManager player)
    {
        player.playerInput.Context = PlayerInputContext.Menu;
        player.playerEvents.OnMenuStateChanged?.Invoke(true);
    }
    public void OnExit(LocalPlayerManager player) { }
}
