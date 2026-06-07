using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Owns and drives a player's stats (health, stamina, aura, regen modifiers, armor,
/// tough hide) and the prone/aura-drain win loop. Ported from Unity.
///
/// GODOT ADAPTATIONS
/// ─────────────────
/// • Plain runtime class (built with `new`), so Unity's [SerializeField] is dropped.
/// • MonoBehaviour owner → Godot Node (passed to each Stat for its debuff timer).
/// • Time.deltaTime → GameTime.Delta.
/// • Collider → Area3D; tag check → Godot group ("Player"); GetComponentInParent → NodeUtil.
/// • Debug Space-to-damage hook reimplemented with an edge-tracked physical key read.
/// </summary>
public class StatController
{
    Stat Health;
    Stat Stamina;
    Stat Aura;
    Stat HealthRegeneration;
    Stat StaminaRevovery;
    Stat Armor;
    Stat ToughHide;

    List<LocalPlayerManager> targets = new();

    Node _owner;
    PlayerEvents playerEvents;
    bool _isInitialized;
    bool isHitConfirmPause;
    bool isInvulnerabilityActive = false;
    public bool isProne = false;
    bool isHealthReset = true;
    float threshold = 0;
    float proneTimelimit = 0;

    PlayerInput gamePad;
    public bool debugLogs = false;

    bool _debugSpacePrev = false;

    public bool IsInitialized => _isInitialized;

    // ── Aura max scaling ──────────────────────────────────────────────────────
    public float GetAuraMax() => Aura.Max;

    public void BroadcastCurrentValues()
    {
        Health.Refresh();
        Stamina.Refresh();
        Aura.Refresh();
    }

    public void ResetStats()
    {
        Health.Reset();
        Stamina.Reset();
        Aura.Reset();
    }

    public void AdjustAuraMaximum(float newMax) => Aura.AdjustMaximum(newMax);

    public void OnUpdate()
    {
        // Debug: Space deals damage (edge-detected to fire once per press).
        bool spaceNow = Input.IsPhysicalKeyPressed(Key.Space);
        if (spaceNow && !_debugSpacePrev)
            playerEvents.OnDamageReceived?.Invoke(new Damage(20, Damage.AttackType.Slash));
        _debugSpacePrev = spaceNow;

        if (Aura.Value != 0)
        {
            if (!isProne & isHealthReset)
            {
                Recover(Health, HealthRegeneration, 1, .1f, true);
                Recover(Stamina, StaminaRevovery, 10, 1, true);
            }
            else if (isProne & !isHealthReset)
            {
                proneTimelimit = Mathf.Clamp((proneTimelimit += GameTime.Delta), 0, 20);
                if (proneTimelimit >= 20)
                {
                    proneTimelimit = 0;
                    playerEvents.OnProneActive?.Invoke(false);
                }
            }
            else if (!isProne & !isHealthReset)
            {
                Recover(Health, HealthRegeneration, Health.Max / 3, 0, false);
                if (debugLogs) GD.Print($"isProne:{isProne} isHealthReset:{isHealthReset} Running");
                if (Health.Value == Health.Max)
                {
                    isHealthReset = true;
                    playerEvents.OnInvulnerabilityActive?.Invoke(false);
                }
            }

            if (gamePad.GetButton("B"))
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i].statManager.isProne)
                    {
                        targets[i].playerEvents.OnAuraDrain?.Invoke();
                        playerEvents.OnAuraReplenish?.Invoke();
                    }
                }
            }
            else
            {
                threshold = 0;
            }
        }
        else
        {
            if (debugLogs) GD.Print("Player Dead");
        }
    }

    public void Initialize(Stat Health, Stat HealthRegeneration, Stat Stamina, Stat StaminaRevovery,
                           Stat Aura, Stat Armor, Stat ToughHide, Node owner, PlayerEvents playerEvents)
    {
        if (Health.IsInitialized & HealthRegeneration.IsInitialized & Stamina.IsInitialized &
            StaminaRevovery.IsInitialized & Aura.IsInitialized & Armor.IsInitialized & ToughHide.IsInitialized)
        {
            this.Health = Health;
            this.HealthRegeneration = HealthRegeneration;
            this.Stamina = Stamina;
            this.StaminaRevovery = StaminaRevovery;
            this.Aura = Aura;
            this.Armor = Armor;
            this.ToughHide = ToughHide;
        }
        else
        {
            throw new Exception("Not all stats are initialized");
        }
    }

    public void Initialize((float starting, float max) health, (float starting, float max) healthRegeneration,
                           (float starting, float max) stamina, (float starting, float max) staminaRevovery,
                           (float starting, float max) aura, (float starting, float max) armor,
                           (float starting, float max) toughHide, Node owner, PlayerEvents playerEvents,
                           PlayerInput gamePad)
    {
        _owner = owner;

        Health = new Stat();
        Health.Initialize(health.starting, 0, health.max, owner, playerEvents.statEventsCoclection[StatEvents.Type.Health]);
        HealthRegeneration = new Stat();
        HealthRegeneration.Initialize(healthRegeneration.starting, 0, healthRegeneration.max, owner, playerEvents.statEventsCoclection[StatEvents.Type.HealthRegeneration]);
        Stamina = new Stat();
        Stamina.Initialize(stamina.starting, 0, stamina.max, owner, playerEvents.statEventsCoclection[StatEvents.Type.Stamina]);
        StaminaRevovery = new Stat();
        StaminaRevovery.Initialize(staminaRevovery.starting, 0, staminaRevovery.max, owner, playerEvents.statEventsCoclection[StatEvents.Type.StaminaRecovery]);
        Aura = new Stat();
        Aura.Initialize(aura.starting, 0, aura.max, owner, playerEvents.statEventsCoclection[StatEvents.Type.Aura]);
        Armor = new Stat();
        Armor.Initialize(armor.starting, 0, armor.max, owner, playerEvents.statEventsCoclection[StatEvents.Type.Armor]);
        ToughHide = new Stat();
        ToughHide.Initialize(toughHide.starting, 0, toughHide.max, owner, playerEvents.statEventsCoclection[StatEvents.Type.ToughHide]);

        this.playerEvents = playerEvents;
        this.playerEvents.OnUpdate += OnUpdate;
        this.playerEvents.OnHitConfirm += OnHitConfirm;
        this.playerEvents.OnHitConfirmPauseEnd += OnHitConfirmPauseEnd;
        this.playerEvents.OnDamageReceived += OnDamageReceived;
        this.playerEvents.OnAuraDrain += OnAuraDrain;
        this.playerEvents.OnAuraReplenish += OnAuraReplenish;
        this.playerEvents.OnInvulnerabilityActive += OnInvulnerabilityActive;
        this.playerEvents.OnProneActive += OnProneActive;
        this.playerEvents.statEventsCoclection[StatEvents.Type.Health].OnValueZero += OnHealthValueZero;
        this.playerEvents.objectTriggerEventCollection[ObjectTriggerEvents.Type.AuraField].BroadCastOnTriggerEnter += BroadCastOnTriggerEnter;
        this.playerEvents.objectTriggerEventCollection[ObjectTriggerEvents.Type.AuraField].BroadCastOnTriggerExit += BroadCastOnTriggerExit;

        this.gamePad = gamePad;
        _isInitialized = true;
    }

    public void OnHitConfirm((Node3D hitbox, Node3D hurtbox) arg0)      => isHitConfirmPause = true;
    public void OnHitConfirmPauseEnd((Node3D hitbox, Node3D hurtbox) arg0) => isHitConfirmPause = false;

    public void Deactivate()
    {
        Health.Deactivate();            Health = null;
        HealthRegeneration.Deactivate(); HealthRegeneration = null;
        Stamina.Deactivate();           Stamina = null;
        StaminaRevovery.Deactivate();    StaminaRevovery = null;
        Aura.Deactivate();              Aura = null;
        Armor.Deactivate();             Armor = null;
        ToughHide.Deactivate();         ToughHide = null;

        playerEvents.OnUpdate -= OnUpdate;
        playerEvents.OnHitConfirm -= OnHitConfirm;
        playerEvents.OnHitConfirmPauseEnd -= OnHitConfirmPauseEnd;
        playerEvents.OnAuraDrain -= OnAuraDrain;
        playerEvents.OnAuraReplenish -= OnAuraReplenish;
        playerEvents.OnInvulnerabilityActive -= OnInvulnerabilityActive;
        playerEvents.OnProneActive -= OnProneActive;
        playerEvents.statEventsCoclection[StatEvents.Type.Health].OnValueZero -= OnHealthValueZero;
        playerEvents.objectTriggerEventCollection[ObjectTriggerEvents.Type.AuraField].BroadCastOnTriggerEnter -= BroadCastOnTriggerEnter;
        playerEvents.objectTriggerEventCollection[ObjectTriggerEvents.Type.AuraField].BroadCastOnTriggerExit -= BroadCastOnTriggerExit;

        playerEvents = null;
        _isInitialized = false;
    }

    public void OnDamageReceived(Damage Damage)
    {
        if (!isInvulnerabilityActive)
        {
            float value = Damage.Value;
            if (Damage.Type == Damage.AttackType.Smash)
            {
                float percentage = ToughHide.Value / 100;
                value = value - (percentage * value);
            }
            Health.Subtract(Damage.Value);
        }
        Damage.Reset();
    }

    // Aura transferred per second while draining. Matches Unity's 5/s (the earlier "drain doesn't
    // work" report was a body→LPM resolution bug, since fixed — not the rate).
    const float AuraTransferPerSec = 5f;

    public void OnAuraDrain()
    {
        Aura.Subtract(AuraTransferPerSec * GameTime.Delta);
        if (debugLogs) GD.Print("Being Drained");
    }

    public void OnAuraReplenish() => Aura.Add(AuraTransferPerSec * GameTime.Delta);

    /// <summary>Recovers a stat over time (see param docs in the Unity original).</summary>
    bool Recover(Stat stat, Stat statModifier, float baseRatePerSecond, float baseRateBonusPerModifierValue, bool minMaxPauseActive)
    {
        if ((stat.Value <= stat.Min | stat.Value >= stat.Max) & minMaxPauseActive == true)
        {
            if (debugLogs) GD.Print($"Min or Max is true:{(stat.Value <= stat.Min | stat.Value >= stat.Max)} & PauseActive: {minMaxPauseActive}");
            return false;
        }
        float rateBonusPerSecond = statModifier.Value * baseRateBonusPerModifierValue;
        float ratePerSecond = baseRatePerSecond + rateBonusPerSecond;
        float ratePerDletaTime = ratePerSecond * GameTime.Delta;
        stat.Add(ratePerDletaTime);
        return true;
    }

    public void Heal() { }

    // ── Debug helpers ──────────────────────────────────────────────────────────
    public void DebugForceKnockOut()
    {
        Health.SetValue(0);
        Aura.SetValue(0);
    }

    public void DebugForceWin() => Aura.SetValue(Aura.Max);
    public void DebugDrainAura() => Aura.SetValue(0);

    private readonly (string name, float value, float max)[] _debugStats = new (string, float, float)[3];

    public (string name, float value, float max)[] GetDebugStats()
    {
        _debugStats[0] = ("Health",  Health.Value,  Health.Max);
        _debugStats[1] = ("Stamina", Stamina.Value, Stamina.Max);
        _debugStats[2] = ("Aura",    Aura.Value,    Aura.Max);
        return _debugStats;
    }

    public void BroadCastOnTriggerEnter(Node3D otherPlayer)
    {
        if (otherPlayer.IsInGroup("Player"))
        {
            // Resolve the owning player via PlayerDetection (a CHILD of the body). The character
            // is a sibling of its LocalPlayerManager, so walking UP the tree would never find it.
            LocalPlayerManager lpm = NodeUtil.GetComponentInChildren<PlayerDetection>(otherPlayer)?.Player;
            if (lpm != null && lpm != _owner && !targets.Contains(lpm)) // skip own aura field
                targets.Add(lpm);
        }
    }

    public void BroadCastOnTriggerExit(Node3D otherPlayer)
    {
        if (otherPlayer.IsInGroup("Player"))
        {
            LocalPlayerManager lpm = NodeUtil.GetComponentInChildren<PlayerDetection>(otherPlayer)?.Player;
            if (lpm != null && targets.Contains(lpm))
                targets.Remove(lpm);
        }
    }

    public void OnHealthValueZero()
    {
        if (debugLogs) GD.Print("Health Zero");
        playerEvents.OnProneActive?.Invoke(true);
        isHealthReset = false;
        playerEvents.OnInvulnerabilityActive?.Invoke(true);
    }

    void OnInvulnerabilityActive(bool isActive) => isInvulnerabilityActive = isActive;
    void OnProneActive(bool isActive)           => isProne = isActive;
}
