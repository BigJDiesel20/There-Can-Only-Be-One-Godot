using Godot;
using System.Collections.Generic;

/// <summary>
/// Node-tree helpers replacing Unity's GetComponentInParent / GetComponentInChildren /
/// GetComponentsInChildren. In the port, "components" are typically C# scripts attached
/// as Nodes (or the controller objects), so these walk the Godot scene tree by type.
/// </summary>
public static class NodeUtil
{
    /// <summary>Nearest ancestor (or self) that is assignable to T, else null.</summary>
    public static T GetComponentInParent<T>(Node node) where T : class
    {
        Node n = node;
        while (n != null)
        {
            if (n is T t) return t;
            n = n.GetParent();
        }
        return null;
    }

    /// <summary>First descendant (depth-first, including self) assignable to T, else null.</summary>
    public static T GetComponentInChildren<T>(Node node) where T : class
    {
        if (node is T self) return self;
        foreach (Node child in node.GetChildren())
        {
            T found = GetComponentInChildren<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>All descendants (including self) assignable to T.</summary>
    public static List<T> GetComponentsInChildren<T>(Node node, List<T> results = null) where T : class
    {
        results ??= new List<T>();
        if (node is T self) results.Add(self);
        foreach (Node child in node.GetChildren())
            GetComponentsInChildren(child, results);
        return results;
    }
}
