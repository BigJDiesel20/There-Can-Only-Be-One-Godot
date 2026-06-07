using Godot;
using System.Collections.Generic;

/// <summary>
/// Holds the full input state of one emulated gamepad. Godot port of Unity's
/// VirtualControllerState. Axes are stored in Godot's RAW convention (stick-Y is +down) so
/// PlayerInput's existing per-axis sign flips apply unchanged.
/// </summary>
public class VirtualControllerState
{
    public bool A, B, X, Y, Start, L1, R1, L3, R3, DpadUp, DpadDown, DpadLeft, DpadRight;
    public float LeftX, LeftY, RightX, RightY;

    public void ResetButtons()
    {
        A = B = X = Y = Start = L1 = R1 = L3 = R3 = false;
        DpadUp = DpadDown = DpadLeft = DpadRight = false;
    }

    public void Reset()
    {
        ResetButtons();
        LeftX = LeftY = RightX = RightY = 0f;
    }

    public bool GetButton(JoyButton b) => b switch
    {
        JoyButton.A             => A,
        JoyButton.B             => B,
        JoyButton.X             => X,
        JoyButton.Y             => Y,
        JoyButton.Start         => Start,
        JoyButton.RightShoulder => R1,
        JoyButton.LeftShoulder  => L1,
        JoyButton.RightStick    => R3,
        JoyButton.LeftStick     => L3,
        JoyButton.DpadUp        => DpadUp,
        JoyButton.DpadDown      => DpadDown,
        JoyButton.DpadLeft      => DpadLeft,
        JoyButton.DpadRight     => DpadRight,
        _                       => false,
    };

    public float GetAxis(JoyAxis a) => a switch
    {
        JoyAxis.LeftX  => LeftX,
        JoyAxis.LeftY  => LeftY,
        JoyAxis.RightX => RightX,
        JoyAxis.RightY => RightY,
        _              => 0f,
    };
}

/// <summary>
/// Registry of all active virtual controllers, keyed by a synthetic device id (>= Base so it
/// can never collide with a real Godot joypad id, which start at 0). MenuInput and PlayerInput
/// consult this when a device id is virtual; the VirtualControllerManager drives the states.
/// Godot port of Unity's VirtualControllerRegistry.
/// </summary>
public static class VirtualControllers
{
    public const int Base = 1000;
    public const int Max  = 16;

    static readonly Dictionary<int, VirtualControllerState> _states = new();

    public static bool IsVirtual(int device) => device >= Base;

    public static VirtualControllerState Get(int device)
        => _states.TryGetValue(device, out var s) ? s : null;

    public static IReadOnlyDictionary<int, VirtualControllerState> States => _states;
    public static int Count => _states.Count;

    /// <summary>Allocates the lowest free virtual device id, or -1 if full.</summary>
    public static int Add()
    {
        for (int i = 0; i < Max; i++)
        {
            int id = Base + i;
            if (!_states.ContainsKey(id)) { _states[id] = new VirtualControllerState(); return id; }
        }
        return -1;
    }

    public static void Remove(int device) => _states.Remove(device);

    /// <summary>Highest currently-allocated virtual device id, or -1 if none.</summary>
    public static int Last()
    {
        int last = -1;
        foreach (int id in _states.Keys) if (id > last) last = id;
        return last;
    }

    public static void Clear() => _states.Clear();
}
