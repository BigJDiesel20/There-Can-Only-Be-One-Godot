using Godot;

/// <summary>
/// PostGame — results screen entered when Battle detects a winner. Shows the winner and four
/// navigable options (Up/Down + A/Enter), ported from Unity:
///   Replay            — reset stats, same players/characters → Battle
///   Choose Characters — keep players, drop characters       → CharacterSelect
///   Leave             — tear everything down                → Lobby
///   Quit              — quit the application
/// </summary>
public class PostGame : IGameState
{
    public GameManager gameManager { get; set; }

    const int RowReplay = 0, RowNewChars = 1, RowLeave = 2, RowQuit = 3, RowCount = 4;
    int _selectedRow;
    PostGameUI _ui;

    public PostGame(GameManager gameManager) { this.gameManager = gameManager; }

    public void OnLoad()
    {
        _selectedRow = 0;
        string winner = gameManager.lastWinnerName.Length > 0 ? gameManager.lastWinnerName : "Unknown";
        GD.Print($"[PostGame] Winner: {winner}.");

        _ui = new PostGameUI();
        _ui.Initialize(gameManager, winner);
        _ui.Refresh(_selectedRow);
    }

    public void OnExit()
    {
        gameManager.lastWinnerName = string.Empty;
        _ui?.Destroy();
        _ui = null;
    }

    public void OnUpdate()
    {
        bool up      = MenuInput.KeyJustPressed(Key.Up)   || AnyPad(JoyButton.DpadUp);
        bool down    = MenuInput.KeyJustPressed(Key.Down) || AnyPad(JoyButton.DpadDown);
        bool confirm = MenuInput.KeyJustPressed(Key.Enter) || MenuInput.KeyJustPressed(Key.Space) || AnyPad(JoyButton.A);

        if (up)   { _selectedRow = (_selectedRow - 1 + RowCount) % RowCount; _ui?.Refresh(_selectedRow); }
        if (down) { _selectedRow = (_selectedRow + 1) % RowCount;           _ui?.Refresh(_selectedRow); }
        if (confirm) Confirm();
    }

    static bool AnyPad(JoyButton b)
    {
        foreach (int d in Input.GetConnectedJoypads())
            if (MenuInput.PadJustPressed(d, b)) return true;
        return false;
    }

    void Confirm()
    {
        switch (_selectedRow)
        {
            case RowReplay:   Replay();           break;
            case RowNewChars: ChooseCharacters(); break;
            case RowLeave:    Leave();            break;
            case RowQuit:     gameManager.GetTree().Quit(); break;
        }
    }

    // Same players + characters: reset stats (aura value back to start, combined max kept), then
    // re-enter Battle which repositions everyone.
    void Replay()
    {
        foreach (LocalPlayerManager p in gameManager.playerSlot)
            p.statManager?.ResetStats();
        gameManager.ChangeState("Battle");
    }

    // Keep the player slots but drop their characters so they can pick new ones.
    void ChooseCharacters()
    {
        foreach (LocalPlayerManager p in gameManager.playerSlot)
        {
            p.DeactivatePlayerCharacter();
            p.characterConfirmed = false;
        }
        FreeRig();
        gameManager.ChangeState("CharacterSelect");
    }

    // Full teardown back to the Lobby.
    void Leave()
    {
        foreach (LocalPlayerManager p in gameManager.playerSlot)
        {
            p.DeactivatePlayerCharacter();
            p.QueueFree();
        }
        gameManager.playerSlot.Clear();
        FreeRig();
        gameManager.ChangeState("Lobby");
    }

    void FreeRig()
    {
        gameManager.splitScreen?.QueueFree();
        gameManager.splitScreen = null;
    }
}
