using Godot;

/// <summary>
/// The 16 selectable characters. Names match the Unity color-named character prefabs;
/// until the real models are exported from Unity, each is represented by a tinted capsule
/// (the color drives the character's material). Index = selection slot.
/// </summary>
public static class CharacterRoster
{
    public static readonly (string name, Color color)[] Characters =
    {
        ("Blue",    new Color(0.20f, 0.40f, 0.95f)),
        ("Coral",   new Color(1.00f, 0.50f, 0.40f)),
        ("Crimson", new Color(0.86f, 0.08f, 0.24f)),
        ("Cyan",    new Color(0.20f, 0.85f, 0.90f)),
        ("Green",   new Color(0.20f, 0.75f, 0.25f)),
        ("Indigo",  new Color(0.35f, 0.10f, 0.65f)),
        ("Jade",    new Color(0.00f, 0.66f, 0.42f)),
        ("Lime",    new Color(0.65f, 0.95f, 0.20f)),
        ("Navy",    new Color(0.12f, 0.18f, 0.50f)),
        ("Orange",  new Color(1.00f, 0.55f, 0.10f)),
        ("Purple",  new Color(0.60f, 0.20f, 0.80f)),
        ("Red",     new Color(0.90f, 0.15f, 0.15f)),
        ("Teal",    new Color(0.00f, 0.55f, 0.55f)),
        ("Violet",  new Color(0.56f, 0.30f, 0.85f)),
        ("White",   new Color(0.95f, 0.95f, 0.95f)),
        ("Yellow",  new Color(0.98f, 0.85f, 0.10f)),
    };

    public static int Count => Characters.Length;
}
