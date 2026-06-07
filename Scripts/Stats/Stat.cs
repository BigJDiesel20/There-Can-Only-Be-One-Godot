using Godot;

/// <summary>
/// Generic clamped stat value with min/max, debuff override, value-lock, and an
/// event hub. Ported from Unity.
///
/// GODOT ADAPTATIONS
/// ─────────────────
/// • UnityAction → System.Action (via StatEvents).
/// • MonoBehaviour.StartCoroutine(Timer) → async/await on a SceneTreeTimer.
///   A Godot Node is supplied at Initialize so the debuff can await the engine's
///   timer and per-frame signal exactly like the original coroutine
///   (wait debuffLength, then wait until unlocked, then clear the debuff).
/// • Mathf.Infinity → Mathf.Inf.
/// </summary>
public class Stat
{
    float _value;
    float _min;
    float _max;

    float _startingValue;   // cached once in Initialize — never changes
    bool  _isInitialized;

    bool  isDebuffed;
    float _debuffValue;
    bool  _isLocked = false;

    Node       _owner;       // used to access the SceneTree for timed debuffs
    StatEvents statEvents;

    public void Initialize(float value, float min, float max, Node owner, StatEvents statEvents)
    {
        _value         = value;
        _startingValue = value;
        _min           = min;
        _max           = max;

        _owner          = owner;
        this.statEvents = statEvents;

        statEvents.OnValueChange?.Invoke(value);
        statEvents.OnMinimumChange?.Invoke(min);
        statEvents.OnMaximumChange?.Invoke(max);
        statEvents.OnPercentageChange?.Invoke(value / max);
        statEvents.OnValueLockChange += SetValueLock;

        _isInitialized = true;
    }

    public void Deactivate()
    {
        _value = 0;
        _min   = 0;
        _max   = 0;
        _debuffValue = 0;

        statEvents.OnValueLockChange -= SetValueLock;

        _owner     = null;
        statEvents = null;

        _isInitialized = false;
    }

    public float Value      => !isDebuffed ? _value : _debuffValue;
    public float Min        => _min;
    public float Max        => _max;
    public float Percentage => _value / _max;
    public bool  IsInitialized => _isInitialized;
    public bool  IsLocked      => _isLocked;

    /// <summary>Restores the current value to the value passed into Initialize. Max untouched.</summary>
    public void Reset() => SetValue(_startingValue);

    public void Add(float value)
    {
        if (_isLocked) return;
        _value = Mathf.Clamp(_value + Mathf.Abs(value), _min, _max);
        statEvents.OnValueChange?.Invoke(_value);
        statEvents.OnPercentageChange?.Invoke(_value / _max);
        if (_value >= _max) statEvents.OnValueMaximum?.Invoke();
    }

    public void Subtract(float value)
    {
        if (_isLocked) return;
        _value = Mathf.Clamp(_value - Mathf.Abs(value), _min, _max);
        statEvents.OnValueChange?.Invoke(_value);
        statEvents.OnPercentageChange?.Invoke(_value / _max);
        if (_value <= 0)    statEvents.OnValueZero?.Invoke();
        if (_value <= _min) statEvents.OnValueMinimum?.Invoke();
    }

    public void SetValue(float value)
    {
        if (_isLocked) return;
        _value = Mathf.Clamp(Mathf.Abs(value), _min, _max);
        statEvents.OnValueChange?.Invoke(_value);
        statEvents.OnPercentageChange?.Invoke(_value / _max);
        if (_value <= 0)    statEvents.OnValueZero?.Invoke();
        if (_value <= _min) statEvents.OnValueMinimum?.Invoke();
        if (_value >= _max) statEvents.OnValueMaximum?.Invoke();
    }

    public void AdjustMinimum(float value)
    {
        if (_isLocked) return;
        _min   = Mathf.Clamp(Mathf.Abs(value), 0, _max);
        _value = Mathf.Clamp(_value, _min, _max);
        statEvents.OnMinimumChange?.Invoke(_min);
        statEvents.OnPercentageChange?.Invoke(_value / _max);
        if (_value <= 0) statEvents.OnValueZero?.Invoke();
    }

    public void AdjustMaximum(float value)
    {
        if (_isLocked) return;
        _max   = Mathf.Clamp(Mathf.Abs(value), _min, Mathf.Inf);
        _value = Mathf.Clamp(_value, _min, _max);
        statEvents.OnMaximumChange?.Invoke(_max);
        statEvents.OnPercentageChange?.Invoke(_value / _max);
        if (_value <= 0)    statEvents.OnValueZero?.Invoke();
        if (_value <= _min) statEvents.OnValueMinimum?.Invoke();
        if (_value >= _max) statEvents.OnValueMaximum?.Invoke();
    }

    /// <summary>
    /// Temporarily overrides the reported value for <paramref name="debuffLength"/>
    /// seconds, then restores it once the stat is no longer locked. Godot port of
    /// the original Unity coroutine using async/await on engine signals.
    /// </summary>
    public async void SetDebuff(float debuffValue, float debuffLength)
    {
        if (_isLocked || _owner == null) return;

        _debuffValue = Mathf.Abs(debuffValue);
        isDebuffed   = true;
        statEvents.OnValueChange?.Invoke(_debuffValue);
        statEvents.OnPercentageChange?.Invoke(_value / _max);

        // Wait the debuff duration.
        await _owner.ToSignal(_owner.GetTree().CreateTimer(debuffLength), SceneTreeTimer.SignalName.Timeout);

        // Then wait until the stat is unlocked (mirrors the original WaitUntil).
        while (_isLocked && _owner != null)
            await _owner.ToSignal(_owner.GetTree(), SceneTree.SignalName.ProcessFrame);

        isDebuffed   = false;
        _debuffValue = _value;
        statEvents?.OnValueChange?.Invoke(_value);
    }

    public void SetValueLock(bool isLocked) => _isLocked = isLocked;

    /// <summary>Re-fires current value/percentage to seed newly-subscribed listeners.</summary>
    public void Refresh()
    {
        statEvents.OnValueChange?.Invoke(_value);
        statEvents.OnPercentageChange?.Invoke(_value / _max);
    }
}
