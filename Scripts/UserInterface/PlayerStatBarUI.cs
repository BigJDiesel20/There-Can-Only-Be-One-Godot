using Godot;
using System.Collections.Generic;

/// <summary>
/// Per-player HUD, laid out bottom-left like Unity: the aura ring (with the player symbol in its
/// hole) in the corner, and to its right a stack of — bottom to top — the animated aura-flame
/// row, the stamina bar and the health bar. RIGHT = the locked-on target (mirrored, shown only
/// while a target is acquired).
///
/// All sizes are scaled by the player's SubViewport size relative to the window, so the HUD keeps
/// the same on-screen proportion whether it's a single full view or one cell of a 16-way split
/// (Godot has no Unity-style CanvasScaler, so we scale the pixel constants ourselves).
/// </summary>
public class PlayerStatBarUI
{
    PlayerEvents _playerEvents;

    ColorRect _healthFill, _staminaFill, _tHealthFill, _tStaminaFill;
    AuraPie   _auraPie, _tAuraPie;
    Control   _targetRoot;
    CanvasLayer _layer;
    TextureRect _auraSymbol, _tAuraSymbol;
    HudNameChip _selfName, _tName;
    readonly List<HudNameChip> _teamRows = new();
    Team        _hookedTeam;
    AnimatedFlame[] _auraFlames;
    LocalPlayerManager _owner, _currentTarget;

    const int FlameCount = 10; // a rolling band of 100 aura, 10 per flame (Unity parity)

    static readonly Color BgCol      = new Color(0.10f, 0.10f, 0.12f, 0.85f);
    static readonly Color HealthCol  = new Color(0.10f, 0.70f, 0.12f);
    static readonly Color StaminaCol = new Color(0.10f, 0.35f, 0.85f);
    static readonly Color AuraCol    = new Color(0.53f, 0.81f, 0.98f);

    // Scaled layout metrics (computed in Initialize from the viewport-cell size).
    float _pad, _barH, _gap, _flameH, _barW, _roleSize, _pieSize, _barX;
    float _flameStart, _flameTotalW;
    float _labelH, _labelFont, _nameBottom; // name box height / font / bottom offset (above health bar)

    public void Initialize(CanvasLayer layer, PlayerEvents playerEvents, LocalPlayerManager owner)
    {
        _playerEvents = playerEvents;
        _owner        = owner;
        _layer        = layer;

        // ── Size everything as a fraction of THIS player's viewport cell ─────────────
        // (a fixed fraction of the cell, so the HUD looks the same whether it's a full view or
        // one cell of a 16-way split, and is independent of window/monitor resolution).
        Vector2 cell = GetCellSize(layer);
        float ch = cell.Y, cw = cell.X;
        _pad      = 0.03f  * ch;
        _pieSize  = 0.16f  * ch;          // bottom-left aura ring
        _barH     = 0.045f * ch;
        _gap      = 0.012f * ch;
        _roleSize = 0.075f * ch;
        _labelH    = _barH * 2f;                       // name box height (Unity: barH * 2)
        _labelFont = _barH * 1.5f;                     // name font size  (Unity: barH * 1.5)
        float barEnd = 0.45f * cw;                    // bars' right edge = 45% across the cell
        _barX     = _pad;                             // bars' left edge lines up with the pie's left edge
        _barW     = Mathf.Max(10f, barEnd - _barX);
        _flameStart  = _pad + _pieSize;               // flames: from the pie's right edge ...
        _flameTotalW = Mathf.Max(10f, barEnd - _flameStart - _gap); // ... stopping just before the bar end
        _flameH = _pieSize; // full pie height, like Unity (flames are stretched to fill the cell)

        // Stacked above the pie: pie → stamina (bottom on pie's top) → health (bottom on stamina's top).
        float flamesBot  = _pad;
        float staminaBot = _pad + _pieSize;
        float healthBot  = _pad + _pieSize + _barH;

        // ── Self (bottom-left) ───────────────────────────────────────────────────────
        _auraPie     = MakePie(layer, rightSide: false);
        _auraSymbol  = MakeSymbol(_auraPie);
        _staminaFill = MakeBar(layer, staminaBot, StaminaCol, rightSide: false);
        _healthFill  = MakeBar(layer, healthBot,  HealthCol,  rightSide: false);
        MakeFlames(layer, flamesBot, rightSide: false);

        if (_owner?.personalSymbol?.sprite != null) _auraSymbol.Texture = _owner.personalSymbol.sprite;

        // ── Name box (Unity parity) ──────────────────────────────────────────────────
        // A dark label above the health bar: [figure(+crown) | name], left-aligned. Teammate
        // rows stack upward above it. The figure shows only while teamed; the crown only for a
        // leader — exactly like Unity's name label / team list.
        float healthTop = _pad + _pieSize + 2f * _barH; // top edge of the health bar
        _nameBottom = healthTop + _gap;                 // name box bottom = just above the bar

        _selfName = new HudNameChip(_labelH, _labelFont, showIcon: true, new Color(1f, 1f, 1f), bold: true);
        PlaceChip(_selfName, _nameBottom, rightSide: false);
        layer.AddChild(_selfName);
        _selfName.SetText(_owner?.playerName);

        playerEvents.OnTeamChanged += UpdateRole;
        UpdateRole();

        // ── Target (mirrored bottom-right), hidden until lock-on ─────────────────────
        _targetRoot = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _targetRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_targetRoot);
        _tAuraPie     = MakePie(_targetRoot, rightSide: true);
        _tAuraSymbol  = MakeSymbol(_tAuraPie);
        _tStaminaFill = MakeBar(_targetRoot, staminaBot, StaminaCol, rightSide: true);
        _tHealthFill  = MakeBar(_targetRoot, healthBot,  HealthCol,  rightSide: true);

        // Target name box — right-aligned, no role icon (mirrors Unity's target name label).
        _tName = new HudNameChip(_labelH, _labelFont, showIcon: false, new Color(1f, 1f, 1f), bold: true);
        PlaceChip(_tName, _nameBottom, rightSide: true);
        _targetRoot.AddChild(_tName);

        var health  = playerEvents.statEventsCoclection[StatEvents.Type.Health];
        var stamina = playerEvents.statEventsCoclection[StatEvents.Type.Stamina];
        var aura    = playerEvents.statEventsCoclection[StatEvents.Type.Aura];
        health.OnPercentageChange  += OnHealth;
        stamina.OnPercentageChange += OnStamina;
        aura.OnPercentageChange    += OnAura;
        aura.OnValueChange         += OnAuraValue;

        playerEvents.OnOrbitTargetChanged += OnTargetChanged;

        // Seed every bar/pie/flame with the current stat values (the stats' own init broadcast
        // fired before this HUD subscribed, and the later aura-max scaling only emits a
        // percentage change — so without this the flame row would never light at battle start).
        _owner?.statManager?.BroadcastCurrentValues();
    }

    /// <summary>This player's SubViewport cell size in pixels (HUD metrics are fractions of it).</summary>
    static Vector2 GetCellSize(CanvasLayer layer)
    {
        var vp = layer.GetViewport();
        Vector2 sz = vp != null ? vp.GetVisibleRect().Size : Vector2.Zero;
        if (sz.X <= 0f || sz.Y <= 0f)
        {
            var root = layer.GetTree()?.Root;
            sz = root != null ? root.GetVisibleRect().Size : new Vector2(1152f, 648f);
        }
        return sz;
    }

    public void Deactivate()
    {
        if (_playerEvents == null) return;
        _playerEvents.statEventsCoclection[StatEvents.Type.Health].OnPercentageChange  -= OnHealth;
        _playerEvents.statEventsCoclection[StatEvents.Type.Stamina].OnPercentageChange -= OnStamina;
        _playerEvents.statEventsCoclection[StatEvents.Type.Aura].OnPercentageChange    -= OnAura;
        _playerEvents.statEventsCoclection[StatEvents.Type.Aura].OnValueChange         -= OnAuraValue;
        _playerEvents.OnOrbitTargetChanged -= OnTargetChanged;
        _playerEvents.OnTeamChanged        -= UpdateRole;
        if (_hookedTeam != null) { _hookedTeam.OnMembershipChanged -= UpdateRole; _hookedTeam = null; }
        UnsubscribeTarget();
        _playerEvents = null;
    }

    void UpdateRole()
    {
        if (_selfName == null || _owner?.teamController == null) return;

        _selfName.SetText(_owner.playerName);
        _selfName.SetRole(_owner.CurrentTeamStatus);

        // Re-hook membership so the roster refreshes when OTHER players join/leave this team
        // (status events only fire on the member whose status actually changed).
        Team team = _owner.teamController.team;
        if (!ReferenceEquals(team, _hookedTeam))
        {
            if (_hookedTeam != null) _hookedTeam.OnMembershipChanged -= UpdateRole;
            _hookedTeam = team;
            if (_hookedTeam != null) _hookedTeam.OnMembershipChanged += UpdateRole;
        }

        RebuildRoster(team);
    }

    // One teammate name row per OTHER member, stacked upward above the local name box (Unity parity).
    void RebuildRoster(Team team)
    {
        foreach (var r in _teamRows) r.QueueFree();
        _teamRows.Clear();

        if (team == null) return;

        int row = 1; // own name is row 0 (the _selfName box); teammates stack above it
        foreach (var member in team.GetAllMembers())
        {
            if (member == _owner) continue;
            var chip = new HudNameChip(_labelH, _labelFont, showIcon: true,
                                       new Color(0.90f, 0.90f, 0.90f), bold: false);
            PlaceChip(chip, _nameBottom + row * (_labelH + 1f), rightSide: false);
            _layer.AddChild(chip);
            chip.SetText(member.playerName);
            chip.SetRole(member.CurrentTeamStatus);
            _teamRows.Add(chip);
            row++;
        }
    }

    // Position a content-sized name chip by its bottom edge, growing right (left side) or left
    // (right side) and always upward.
    void PlaceChip(HudNameChip chip, float bottomOffset, bool rightSide)
    {
        chip.AnchorTop = chip.AnchorBottom = 1f;
        chip.GrowVertical = Control.GrowDirection.Begin; // grow upward
        if (rightSide)
        {
            chip.AnchorLeft = chip.AnchorRight = 1f;
            chip.GrowHorizontal = Control.GrowDirection.Begin; // grow left from the right edge
            chip.OffsetRight = -_pad;
        }
        else
        {
            chip.AnchorLeft = chip.AnchorRight = 0f;
            chip.GrowHorizontal = Control.GrowDirection.End;   // grow right from the left edge
            chip.OffsetLeft = _pad;
        }
        chip.OffsetBottom = -bottomOffset;
    }

    // Light the aura-flame row from the current rolling 100-band: each flame = 10 aura,
    // the trailing flame fades by its partial fill, multiples of 100 light the whole band.
    void OnAuraValue(float value)
    {
        if (_auraFlames == null) return;
        float band    = value % 100f;
        bool  fullBand = value > 0f && band < 0.0001f; // exact multiple of 100 = full row
        for (int i = 0; i < _auraFlames.Length; i++)
        {
            float fill = fullBand ? 1f : Mathf.Clamp((band - i * 10f) / 10f, 0f, 1f);
            _auraFlames[i].Visible  = fill > 0.001f;
            _auraFlames[i].Modulate = new Color(AuraCol.R, AuraCol.G, AuraCol.B, fill);
            _auraFlames[i].Draining = fill > 0.02f && fill < 0.98f; // the partial segment flickers faster
        }
    }

    // ── Construction ──────────────────────────────────────────────────────────────
    AuraPie MakePie(Node parent, bool rightSide)
    {
        var pie = new AuraPie { FillColor = AuraCol, MouseFilter = Control.MouseFilterEnum.Ignore };
        pie.AnchorTop = 1f; pie.AnchorBottom = 1f;
        if (rightSide) { pie.AnchorLeft = 1f; pie.AnchorRight = 1f; pie.OffsetRight = -_pad; pie.OffsetLeft = -_pad - _pieSize; }
        else           { pie.AnchorLeft = 0f; pie.AnchorRight = 0f; pie.OffsetLeft = _pad;  pie.OffsetRight = _pad + _pieSize; }
        pie.OffsetBottom = -_pad; pie.OffsetTop = -(_pad + _pieSize);
        parent.AddChild(pie);
        return pie;
    }

    // Shared circular-clip material for the symbol. Fades alpha to 0 outside a centered circle of
    // UV-radius 0.47 — which, for the 0.17-inset rect, matches the pie's 0.62·pieSize black hole.
    static ShaderMaterial _circleMask;
    static ShaderMaterial CircleMask()
    {
        if (_circleMask != null) return _circleMask;
        _circleMask = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = "shader_type canvas_item;\n" +
                       "void fragment() {\n" +
                       "    float d = distance(UV, vec2(0.5));\n" +
                       "    COLOR.a *= 1.0 - smoothstep(0.44, 0.47, d);\n" +
                       "}\n",
            },
        };
        return _circleMask;
    }

    // The player symbol sits in the pie's center hole.
    TextureRect MakeSymbol(AuraPie pie)
    {
        var sym = new TextureRect
        {
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            // IgnoreSize so the texture's native size doesn't become a minimum that forces the
            // control to grow past its anchored rect (which pushed the symbol below the hole on
            // small split-screen pies). Now it stays centered + scales down at every cell size.
            ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            // Circular mask: clips the (square) glyph to the pie's black hole so it can fill the
            // circle without its corners spilling onto the colored ring.
            Material    = CircleMask(),
        };
        sym.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        // Rect a little larger than the hole (hole = 0.62·pieSize); the mask clips it back to the
        // hole, so the glyph fills the black circle right up to its edge.
        float inset = _pieSize * 0.17f;
        sym.OffsetLeft = inset; sym.OffsetTop = inset; sym.OffsetRight = -inset; sym.OffsetBottom = -inset;
        pie.AddChild(sym);
        return sym;
    }

    ColorRect MakeBar(Node parent, float bottomOffset, Color col, bool rightSide)
    {
        // Flat (square) dark track — a plain ColorRect so nothing bleeds onto the pie/flames it
        // sits above (the previous rounded StyleBox's anti-aliased corners spilled 1px below).
        var bg = new ColorRect { Color = new Color(0.05f, 0.05f, 0.07f, 0.92f), MouseFilter = Control.MouseFilterEnum.Ignore };
        bg.AnchorTop = 1f; bg.AnchorBottom = 1f;
        if (rightSide) { bg.AnchorLeft = 1f; bg.AnchorRight = 1f; bg.OffsetRight = -_barX; bg.OffsetLeft = -_barX - _barW; }
        else           { bg.AnchorLeft = 0f; bg.AnchorRight = 0f; bg.OffsetLeft = _barX;   bg.OffsetRight = _barX + _barW; }
        bg.OffsetBottom = -bottomOffset; bg.OffsetTop = -(bottomOffset + _barH);
        parent.AddChild(bg);

        // Coloured fill, inset inside the track by a thin, size-consistent outline. (No gloss
        // gradient: a gradient is a fraction of the bar, so on the big bars used by 2/4/9-player
        // splits it became visible light-top/dark-bottom bands. A flat fill + hairline outline
        // looks identical at every player count.)
        float border = Mathf.Clamp(_barH * 0.05f, 1f, 1.5f);
        var fill = new ColorRect { Color = col, MouseFilter = Control.MouseFilterEnum.Ignore };
        fill.AnchorTop = 0f; fill.AnchorBottom = 1f;
        fill.OffsetTop = border; fill.OffsetBottom = -border;
        fill.AnchorLeft = 0f; fill.AnchorRight = 1f;
        fill.OffsetLeft = border; fill.OffsetRight = -border;
        bg.AddChild(fill);
        return fill;
    }

    void MakeFlames(Node parent, float bottomOffset, bool rightSide)
    {
        _auraFlames = new AnimatedFlame[FlameCount];
        float flameW = _flameTotalW / FlameCount;
        for (int i = 0; i < FlameCount; i++)
        {
            var f = new AnimatedFlame { Visible = false, Modulate = AuraCol };
            f.AnchorTop = 1f; f.AnchorBottom = 1f;
            float xl = _flameStart + i * flameW;       // no gap — the frame's transparent padding
            float xr = _flameStart + (i + 1) * flameW;  // spaces the flames apart (Unity-style)
            if (rightSide) { f.AnchorLeft = 1f; f.AnchorRight = 1f; f.OffsetRight = -xl; f.OffsetLeft = -xr; }
            else           { f.AnchorLeft = 0f; f.AnchorRight = 0f; f.OffsetLeft = xl; f.OffsetRight = xr; }
            f.OffsetBottom = -bottomOffset; f.OffsetTop = -(bottomOffset + _flameH);
            parent.AddChild(f);
            _auraFlames[i] = f;
        }
    }

    // ── Self ────────────────────────────────────────────────────────────────────
    void OnHealth(float pct)  => SetFillLeft(_healthFill, pct);
    void OnStamina(float pct) => SetFillLeft(_staminaFill, pct);
    void OnAura(float pct)    => _auraPie?.SetPercent(pct);

    static void SetFillLeft(ColorRect fill, float pct)
    {
        if (fill == null) return;
        fill.AnchorRight = Mathf.Clamp(pct, 0f, 1f);
        fill.OffsetRight = -Mathf.Max(1f, fill.OffsetLeft);
    }

    // ── Target ──────────────────────────────────────────────────────────────────
    void OnTargetChanged(LocalPlayerManager target, bool acquired)
    {
        UnsubscribeTarget();

        if (acquired && target != null && target.statManager != null)
        {
            _currentTarget = target;
            var th = target.playerEvents.statEventsCoclection[StatEvents.Type.Health];
            var ts = target.playerEvents.statEventsCoclection[StatEvents.Type.Stamina];
            var ta = target.playerEvents.statEventsCoclection[StatEvents.Type.Aura];
            th.OnPercentageChange += OnTargetHealth;
            ts.OnPercentageChange += OnTargetStamina;
            ta.OnPercentageChange += OnTargetAura;

            if (_targetRoot != null) _targetRoot.Visible = true;
            if (_tAuraSymbol != null) _tAuraSymbol.Texture = target.ActiveSymbol?.sprite;
            _tName?.SetText(target.playerName);
            target.statManager.BroadcastCurrentValues();
        }
        else if (_targetRoot != null)
        {
            _targetRoot.Visible = false;
        }
    }

    void UnsubscribeTarget()
    {
        if (_currentTarget == null) return;
        var th = _currentTarget.playerEvents.statEventsCoclection[StatEvents.Type.Health];
        var ts = _currentTarget.playerEvents.statEventsCoclection[StatEvents.Type.Stamina];
        var ta = _currentTarget.playerEvents.statEventsCoclection[StatEvents.Type.Aura];
        th.OnPercentageChange -= OnTargetHealth;
        ts.OnPercentageChange -= OnTargetStamina;
        ta.OnPercentageChange -= OnTargetAura;
        _currentTarget = null;
    }

    void OnTargetHealth(float pct)  => SetFillRight(_tHealthFill, pct);
    void OnTargetStamina(float pct) => SetFillRight(_tStaminaFill, pct);
    void OnTargetAura(float pct)    => _tAuraPie?.SetPercent(pct);

    static void SetFillRight(ColorRect fill, float pct)
    {
        if (fill == null) return;
        fill.AnchorLeft = 1f - Mathf.Clamp(pct, 0f, 1f);
        fill.OffsetLeft = Mathf.Max(1f, fill.OffsetRight * -1f);
    }
}
