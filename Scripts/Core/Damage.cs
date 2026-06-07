/// <summary>
/// A single damage instance passed through OnDamageReceived. Ported from Unity.
/// The unused `damageDealer` reference from the Unity version was dropped to keep
/// this dependency-free; reintroduce a LocalPlayerManager reference later if
/// attribution (who dealt the damage) is needed.
/// </summary>
public class Damage
{
    public enum AttackType { None, Smash, Slash }

    AttackType _type;
    float _value;
    bool _isReset = true;

    public Damage(float value, AttackType type)
    {
        _value   = value;
        _type    = type;
        _isReset = false;
    }

    public float Value => _value;
    public AttackType Type => _type;

    public void Reset()
    {
        _value   = 0;
        _type    = AttackType.None;
        _isReset = true;
    }
}
