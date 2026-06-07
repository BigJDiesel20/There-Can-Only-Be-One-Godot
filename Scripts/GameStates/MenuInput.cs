using Godot;
using System.Collections.Generic;

/// <summary>
/// Raw edge-detected input for the menu states (which run before per-player PlayerInput is
/// gameplay-active). Polls gamepad buttons per device and keyboard keys directly — bypassing
/// the context-gated PlayerInput — mirroring how Unity's menus read Rewired directly.
///
/// Call the JustPressed methods every frame for the buttons/keys you care about; edges are
/// tracked from the previous poll.
/// </summary>
public static class MenuInput
{
    // key = (device, code): device >= 0 → gamepad (code = JoyButton); device -1 → keyboard (code = Key)
    static readonly Dictionary<(int device, int code), bool> _prev = new();

    public static bool PadJustPressed(int device, JoyButton btn)
    {
        var k   = (device, (int)btn);
        bool now = VirtualControllers.IsVirtual(device)
            ? (VirtualControllers.Get(device)?.GetButton(btn) ?? false)
            : Input.IsJoyButtonPressed(device, btn);
        bool was = _prev.GetValueOrDefault(k);
        _prev[k] = now;
        return now && !was;
    }

    public static bool KeyJustPressed(Key key)
    {
        var k   = (-1, (int)key);
        bool now = Input.IsPhysicalKeyPressed(key);
        bool was = _prev.GetValueOrDefault(k);
        _prev[k] = now;
        return now && !was;
    }
}
