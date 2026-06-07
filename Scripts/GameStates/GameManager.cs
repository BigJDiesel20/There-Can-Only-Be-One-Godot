using Godot;
using System.Collections.Generic;

/// <summary>
/// Global game-flow controller. Ported from Unity's GameManager (MonoBehaviour → Node).
///
/// SCOPE (first backbone pass): drives Lobby → PreGame → Battle → PostGame. The Unity
/// SplashScreen / Menu / CharacterSelect screens (heavy uGUI + multi-device join) are a
/// later UI pass; this auto-joins connected gamepads in a simplified Lobby instead.
///
/// _Process pumps the active state's OnUpdate (Unity Update). States are pre-built once.
/// </summary>
public partial class GameManager : Node
{
    public enum GameMode { Classic }
    public GameMode currentGameMode = GameMode.Classic;

    /// <summary>Set by Battle when a win condition is met; read by PostGame.</summary>
    public string lastWinnerName = string.Empty;

    [Export] public PackedScene PlayerScene;            // the CharacterBody3D character to spawn
    [Export] public CharacterAttackConfig DefaultConfig; // optional moveset (null = no attacks yet)

    public List<LocalPlayerManager> playerSlot = new();

    /// <summary>The split-screen rig built by PreGame (one SubViewport per player).</summary>
    public SplitScreenRig splitScreen;

    public Dictionary<string, IGameState> states = new();
    public IGameState currentState;

    public override void _Ready()
    {
        // Ensure a moveset exists so combat fires. Built in code each run — ResourceSaver
        // round-trips scripted sub-resources unreliably, so we don't persist a .tres for now.
        if (DefaultConfig == null)
            DefaultConfig = CharacterAttackConfig.BuildDefault();

        states["SplashScreen"]    = new SplashScreen(this);
        states["Menu"]            = new Menu(this);
        states["Lobby"]           = new Lobby(this);
        states["CharacterSelect"] = new CharacterSelect(this);
        states["PreGame"]         = new PreGame(this);
        states["Battle"]          = new Battle(this);
        states["PostGame"]        = new PostGame(this);

        ChangeState("SplashScreen");

        // Debug-only: virtual controllers for testing local multiplayer (F1 add … see overlay).
        AddChild(new VirtualControllerManager(this));
    }

    public override void _Process(double delta)
    {
        currentState?.OnUpdate();
    }

    public void ChangeState(string state)
    {
        currentState?.OnExit();
        if (states.TryGetValue(state, out IGameState result))
        {
            // Assign BEFORE OnLoad: a state's OnLoad may itself call ChangeState (e.g. PreGame →
            // Battle). If we set currentState after OnLoad, that nested transition gets overwritten
            // and the game ends up stuck in the outer state (PreGame) with Battle.OnUpdate never running.
            currentState = result;
            GD.Print($"[GameManager] State → {state}");
            result.OnLoad();
        }
    }

    /// <summary>Renames every player slot sequentially (_player 1, _player 2, …).</summary>
    public void SetPlayerNames()
    {
        for (int i = 0; i < playerSlot.Count; i++)
            playerSlot[i].InitializePlayerName($"_player {i + 1}");
    }
}
