using System;

/// <summary>
/// Per-stat event hub. Ported from Unity (UnityAction → System.Action).
/// One instance exists per stat type (Health, Stamina, Aura, …) and is shared
/// between the owning Stat and any UI/logic listeners.
/// </summary>
public class StatEvents
{
    public enum Type { Health, HealthRegeneration, Stamina, StaminaRecovery, Aura, Armor, ToughHide }

    public Action<float> OnValueChange;
    public Action<float> OnMinimumChange;
    public Action<float> OnMaximumChange;
    public Action<float> OnPercentageChange;
    public Action<bool>  OnValueLockChange;

    // Detect death / caps
    public Action OnValueZero;
    public Action OnValueMinimum;
    public Action OnValueMaximum;
}
