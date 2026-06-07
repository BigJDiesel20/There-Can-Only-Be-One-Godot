using Godot;
using System.Collections.Generic;

/// <summary>
/// Lobby — players join with A (gamepad) or Space (keyboard → device 0), leave with B/Backspace,
/// and Start/Enter begins CharacterSelect once at least one has joined. Each join creates a
/// LocalPlayerManager bound to that device; the character itself is chosen in CharacterSelect.
/// </summary>
public class Lobby : IGameState
{
    public GameManager gameManager { get; set; }

    CanvasLayer _ui;
    Label       _info;
    readonly HashSet<int> _joinedDevices = new();
    bool _keyboardJoined;

    public Lobby(GameManager gameManager) { this.gameManager = gameManager; }

    public void OnLoad()
    {
        _joinedDevices.Clear();
        _keyboardJoined = false;
        gameManager.playerSlot.Clear();   // PostGame already tore players down

        (_ui, _, _info) = MenuUI.Create(gameManager, "LOBBY");
        Refresh();
    }

    public void OnUpdate()
    {
        foreach (int dev in AllDevices())
        {
            if (!_joinedDevices.Contains(dev) && MenuInput.PadJustPressed(dev, JoyButton.A))
                Join(dev);
            else if (_joinedDevices.Contains(dev) && MenuInput.PadJustPressed(dev, JoyButton.B))
                Leave(dev);

            if (_joinedDevices.Contains(dev) && MenuInput.PadJustPressed(dev, JoyButton.Start) && gameManager.playerSlot.Count > 0)
            { gameManager.ChangeState("CharacterSelect"); return; }
        }

        // Keyboard fallback (device 0 pseudo-player) so the flow is testable without a pad.
        if (!_keyboardJoined && MenuInput.KeyJustPressed(Key.Space)) { _keyboardJoined = true; Join(0); }
        if (MenuInput.KeyJustPressed(Key.Enter) && gameManager.playerSlot.Count > 0)
        { gameManager.ChangeState("CharacterSelect"); return; }
    }

    public void OnExit() { _ui?.QueueFree(); _ui = null; }

    /// <summary>Real connected gamepads plus any active virtual (debug) controllers.</summary>
    static IEnumerable<int> AllDevices()
    {
        foreach (int dev in Input.GetConnectedJoypads()) yield return dev;
        foreach (int dev in VirtualControllers.States.Keys) yield return dev;
    }

    void Join(int device)
    {
        if (_joinedDevices.Contains(device)) return;
        _joinedDevices.Add(device);

        var lpm = new LocalPlayerManager();
        gameManager.AddChild(lpm);
        lpm.InitializePlayer(device);
        lpm.attackConfig = gameManager.DefaultConfig;
        lpm.colorIndex   = gameManager.playerSlot.Count % CharacterRoster.Count;
        gameManager.playerSlot.Add(lpm);
        gameManager.SetPlayerNames();
        Refresh();
    }

    void Leave(int device)
    {
        _joinedDevices.Remove(device);
        LocalPlayerManager lpm = gameManager.playerSlot.Find(p => p.deviceId == device);
        if (lpm != null)
        {
            gameManager.playerSlot.Remove(lpm);
            lpm.QueueFree();
        }
        gameManager.SetPlayerNames();
        Refresh();
    }

    void Refresh()
    {
        string list = "";
        for (int i = 0; i < gameManager.playerSlot.Count; i++)
            list += $"●  Player {i + 1}   [device {gameManager.playerSlot[i].deviceId}]\n";
        if (gameManager.playerSlot.Count == 0) list = "(waiting for players…)\n";

        _info.Text = list + "\nA / Space — Join     B / Backspace — Leave\nStart / Enter — Begin";
    }
}
