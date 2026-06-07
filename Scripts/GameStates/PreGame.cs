using Godot;

/// <summary>
/// PreGame — one-time setup before Battle. Builds every staged character (completing the
/// deferred controller init), scales the aura maximum by player count, then → Battle.
///
/// Unity's split-screen camera viewport assignment (SetViewPort) is deferred to the
/// SubViewport pass; for now each player's camera renders full-screen (last one wins with
/// multiple players until split-screen lands).
/// </summary>
public class PreGame : IGameState
{
    public GameManager gameManager { get; set; }

    public PreGame(GameManager gameManager) { this.gameManager = gameManager; }

    public void OnLoad()
    {
        int count = gameManager.playerSlot.Count;

        // Split-screen: one SubViewport per player, all sharing the main 3D world. Must be
        // built and assigned BEFORE BuildCharacter so each camera/HUD spawns in its viewport.
        gameManager.splitScreen?.QueueFree();
        var rig = new SplitScreenRig { Name = "SplitScreen" };
        gameManager.AddChild(rig);
        rig.Build(count, gameManager.GetViewport().FindWorld3D());
        gameManager.splitScreen = rig;
        for (int i = 0; i < count; i++)
            gameManager.playerSlot[i].viewport = rig.Viewports[i];

        foreach (LocalPlayerManager p in gameManager.playerSlot)
            p.BuildCharacter();   // initialises controllers + subscribes stat events

        SetAuraMaximum();         // fires OnPercentageChange with combined max — HUD already subscribed
        GD.Print($"[PreGame] Built {count} character(s) across {count} viewport(s) → Battle.");
        gameManager.ChangeState("Battle");
    }

    public void OnUpdate() { }
    public void OnExit() { }

    /// <summary>Aura max = playerCount × each player's base aura max (win = first to combined max).</summary>
    void SetAuraMaximum()
    {
        int count = gameManager.playerSlot.Count;
        if (count == 0) return;

        float baseAuraMax = gameManager.playerSlot[0].statManager.GetAuraMax();
        float combinedMax = count * baseAuraMax;

        foreach (LocalPlayerManager p in gameManager.playerSlot)
            p.statManager.AdjustAuraMaximum(combinedMax);
    }
}
