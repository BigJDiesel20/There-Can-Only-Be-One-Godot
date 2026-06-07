using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Battle — the active match. Spawns characters at random non-overlapping positions, then
/// runs the event-driven win condition: first player whose aura reaches the combined max
/// wins → PostGame. Ported from Unity (Physics.OverlapBox ground check simplified to a flat
/// spawn; BattleDebugUI / CameraViewportBorder deferred).
/// </summary>
public class Battle : IGameState
{
    public GameManager gameManager { get; set; }

    bool _gameOver;
    readonly List<(StatEvents events, Action handler)> _auraHandlers = new();
    CanvasLayer _debugLayer;

    public Battle(GameManager gameManager) { this.gameManager = gameManager; }

    public void OnLoad()
    {
        _gameOver = false;
        SpawnCharacters();
        SubscribeWinCondition();

        // Make input live for everyone (sets Battle input context).
        foreach (LocalPlayerManager p in gameManager.playerSlot)
            p.stateMachine.EnterBattle();

        // Top-left collapsible Force-Win debug pane (Godot port of Unity's BattleDebugUI).
        _debugLayer = new CanvasLayer { Name = "BattleDebugUI", Layer = 40 };
        gameManager.AddChild(_debugLayer);
        _debugLayer.AddChild(new BattleDebugPanel(gameManager));
    }

    public void OnExit()
    {
        foreach (LocalPlayerManager p in gameManager.playerSlot)
        {
            p.stateMachine.ChangeState(null); // Disabled context
            if (p.movementController?.rb != null)
                p.movementController.rb.Velocity = Vector3.Zero;
        }
        UnsubscribeWinCondition();
        _debugLayer?.QueueFree();
        _debugLayer = null;
    }

    public void OnUpdate()
    {
        // win condition is event-driven; DEBUG: Enter forces player 1 to win so the
        // Battle → PostGame → Lobby loop is testable without an aura-gain mechanic.
        if (!_gameOver && gameManager.playerSlot.Count > 0 && Input.IsPhysicalKeyPressed(Key.Enter))
            gameManager.playerSlot[0].statManager.DebugForceWin();
    }

    // ── Spawn ───────────────────────────────────────────────────────────────────
    const float MinSpacing = 3f;
    const float SpawnRange = 12f;
    const int   MaxAttempts = 200;

    void SpawnCharacters()
    {
        var placed = new List<Vector3>();
        var rng    = new Random();
        int count  = gameManager.playerSlot.Count;

        for (int i = 0; i < count; i++)
        {
            Vector3 pos   = Vector3.Zero;
            bool    found = false;

            for (int a = 0; a < MaxAttempts; a++)
            {
                var c = new Vector3(
                    (float)(rng.NextDouble() * 2 - 1) * SpawnRange,
                    1f, // capsule centre sits on the y=0 ground
                    (float)(rng.NextDouble() * 2 - 1) * SpawnRange);

                bool tooClose = false;
                foreach (var q in placed) if (c.DistanceTo(q) < MinSpacing) { tooClose = true; break; }
                if (tooClose) continue;

                pos = c; found = true; break;
            }

            if (!found)
            {
                float ang = i * (Mathf.Tau / Mathf.Max(1, count));
                pos = new Vector3(Mathf.Cos(ang) * MinSpacing * 2f, 1f, Mathf.Sin(ang) * MinSpacing * 2f);
                GD.Print($"[Battle] Player {i + 1} used circle fallback spawn at {pos}.");
            }

            placed.Add(pos);
            if (gameManager.playerSlot[i].character != null)
                gameManager.playerSlot[i].character.GlobalPosition = pos;
        }
    }

    // ── Win condition ───────────────────────────────────────────────────────────
    void SubscribeWinCondition()
    {
        _auraHandlers.Clear();
        foreach (LocalPlayerManager p in gameManager.playerSlot)
        {
            StatEvents aura = p.playerEvents.statEventsCoclection[StatEvents.Type.Aura];
            LocalPlayerManager captured = p;
            Action handler = () => OnAuraMaxReached(captured);
            aura.OnValueMaximum += handler;
            _auraHandlers.Add((aura, handler));
        }
    }

    void UnsubscribeWinCondition()
    {
        foreach (var (events, handler) in _auraHandlers)
            events.OnValueMaximum -= handler;
        _auraHandlers.Clear();
    }

    void OnAuraMaxReached(LocalPlayerManager winner)
    {
        if (_gameOver) return;
        _gameOver = true;
        gameManager.lastWinnerName = winner.playerName;
        GD.Print($"[Battle] {winner.playerName} reached max aura — WINNER → PostGame.");
        gameManager.ChangeState("PostGame");
    }
}
