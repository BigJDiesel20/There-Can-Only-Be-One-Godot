using Godot;

/// <summary>
/// Title screen. Any Start/A (gamepad) or Enter/Space (keyboard) advances to the Lobby.
/// </summary>
public class SplashScreen : IGameState
{
    public GameManager gameManager { get; set; }
    CanvasLayer _ui;

    public SplashScreen(GameManager gameManager) { this.gameManager = gameManager; }

    public void OnLoad()
    {
        (_ui, _, var info) = MenuUI.Create(gameManager, "THERE CAN ONLY BE ONE");
        info.Text = "Press Start  (or Enter)";
    }

    public void OnUpdate()
    {
        bool advance = MenuInput.KeyJustPressed(Key.Enter) || MenuInput.KeyJustPressed(Key.Space);
        foreach (int dev in Input.GetConnectedJoypads())
            if (MenuInput.PadJustPressed(dev, JoyButton.Start) || MenuInput.PadJustPressed(dev, JoyButton.A))
                advance = true;

        if (advance) gameManager.ChangeState("Menu");
    }

    public void OnExit() { _ui?.QueueFree(); _ui = null; }
}
