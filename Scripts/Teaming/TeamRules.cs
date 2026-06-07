using Godot;
using System.Collections.Generic;

/// <summary>
/// Dynamic team constraints — both limits recalculate live from
/// LocalPlayerManager.ActivePlayers on every query. Ported verbatim from Unity
/// (Mathf maps directly; pure logic, no engine objects).
///
///   MaxTeamSize = Ceil( PlayerCount / max(ActiveTeamCount, 2) )   [Team.AddMember]
///   MaxTeams    = Floor( PlayerCount / LargestCurrentTeamSize )   [TeamController]
/// </summary>
public static class TeamRules
{
    public static List<Team> GetActiveTeams()
    {
        var seen   = new HashSet<Team>();
        var result = new List<Team>();
        foreach (LocalPlayerManager p in LocalPlayerManager.ActivePlayers)
        {
            Team t = p.teamController?.team;
            if (t != null && t.GetAllMembers().Count >= 2 && !seen.Contains(t))
            {
                seen.Add(t);
                result.Add(t);
            }
        }
        return result;
    }

    public static int GetActiveTeamCount() => GetActiveTeams().Count;

    public static int GetLargestTeamSize()
    {
        int largest = 0;
        foreach (LocalPlayerManager p in LocalPlayerManager.ActivePlayers)
        {
            Team t = p.teamController?.team;
            if (t == null) continue;
            int size = t.GetAllMembers().Count;
            if (size > largest) largest = size;
        }
        return largest;
    }

    public static int GetMaxTeams()
    {
        int playerCount = LocalPlayerManager.ActivePlayers.Count;
        if (playerCount == 0) return 0;
        int largest = GetLargestTeamSize();
        if (largest < 2) return int.MaxValue;
        return Mathf.FloorToInt((float)playerCount / largest);
    }

    public static int GetMaxTeamSize()
    {
        int playerCount = LocalPlayerManager.ActivePlayers.Count;
        if (playerCount < 2) return int.MaxValue;
        int teamCount = GetActiveTeamCount();
        int effectiveTeamCount = Mathf.Max(teamCount, 2);
        return Mathf.CeilToInt((float)playerCount / effectiveTeamCount);
    }

    public static bool WouldExceedMaxTeams()
    {
        int max = GetMaxTeams();
        if (max == int.MaxValue) return false;
        return (GetActiveTeamCount() + 1) > max;
    }

    public static bool WouldExceedMaxTeamSize(Team team)
    {
        int max = GetMaxTeamSize();
        if (max == int.MaxValue) return false;
        return (team.GetAllMembers().Count + 1) > max;
    }
}
