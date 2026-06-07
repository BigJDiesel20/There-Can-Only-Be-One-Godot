/// <summary>
/// Defines which input layer is active for a player at any given moment.
/// PlayerInput gates its methods against this value so the same physical button
/// can serve different purposes depending on game context, with no caller needing
/// to check state itself. Ported verbatim from Unity (pure enum, no engine deps).
///
///   Disabled  — all input blocked (between states, dead, not yet spawned)
///   Battle    — full combat input live; UI input blocked
///   Prone     — combat input live at a reduced set (e.g. get-up); UI blocked
///   Comboing  — attack buttons live (combo buffering); movement/rotation axes zeroed
///   Dialog    — combat input blocked; UI button input live via GetUIButtonDown
///   Spectate  — no combat input; camera navigation handled separately
///   Menu      — Player Menu open; only menu-navigation queries pass through
/// </summary>
public enum PlayerInputContext
{
    Disabled,
    Battle,
    Prone,
    Comboing,
    Dialog,
    Spectate,
    Menu
}
