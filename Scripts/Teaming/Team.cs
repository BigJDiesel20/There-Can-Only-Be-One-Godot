using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// A team's membership + leader/follower bookkeeping. Ported from Unity
/// (Debug.Log → GD.Print; player.name → playerName). Pure logic.
/// </summary>
public class Team
{
    /// <summary>Fired whenever any member is added or removed (HUD/list sync).</summary>
    public Action OnMembershipChanged;

    private List<LocalPlayerManager> Members = new();

    public LocalPlayerManager GetLeader()
    {
        for (int i = 0; i < Members.Count; i++)
            if (Members[i].CurrentTeamStatus == TeamController.Status.Leader)
                return Members[i];
        return null;
    }

    public List<LocalPlayerManager> GetFollowers()
    {
        var followers = new List<LocalPlayerManager>();
        for (int i = 0; i < Members.Count; i++)
            if (Members[i].CurrentTeamStatus != TeamController.Status.Leader)
                followers.Add(Members[i]);
        return followers;
    }

    public List<LocalPlayerManager> GetAllMembers() => Members;

    public LocalPlayerManager GetMembersByName(string name)
    {
        LocalPlayerManager member = null;
        for (int i = 0; i < Members.Count; i++)
            if (Members[i].playerName == name) member = Members[i];
        return member;
    }

    public bool IsCurrentMember(LocalPlayerManager player)
    {
        for (int i = 0; i < Members.Count; i++)
            if (Members[i].playerName == player.playerName) return true;
        return false;
    }

    public bool AddMember(LocalPlayerManager member)
    {
        if (IsCurrentMember(member)) return false;

        if (TeamRules.WouldExceedMaxTeamSize(this))
        {
            GD.Print($"[Team] Cannot add {member.playerName}: team full ({Members.Count}/{TeamRules.GetMaxTeamSize()}).");
            return false;
        }

        // Wire the team reference BEFORE setting status (the status setter resolves the
        // leader's symbol via teamController.team).
        member.teamController.team = this;

        member.CurrentTeamStatus = Members.Count == 0
            ? TeamController.Status.Leader
            : TeamController.Status.Follower;

        Members.Add(member);
        OnMembershipChanged?.Invoke();
        return true;
    }

    public void RemoveMember(LocalPlayerManager member)
    {
        if (!IsCurrentMember(member)) return;

        bool wasLeader = member.CurrentTeamStatus == TeamController.Status.Leader;

        member.CurrentTeamStatus   = TeamController.Status.Solo;
        member.teamController.team  = null;
        Members.Remove(member);

        if (Members.Count == 1)
        {
            Members[0].CurrentTeamStatus   = TeamController.Status.Solo;
            Members[0].teamController.team  = null;
            Members.Clear();
        }
        else if (wasLeader && Members.Count >= 1)
        {
            Members[0].CurrentTeamStatus = TeamController.Status.Leader;
        }

        OnMembershipChanged?.Invoke();
    }

    public void RemoveAllMembers()
    {
        var memberList = new List<LocalPlayerManager>(Members);

        for (int i = 0; i < Members.Count; i++)
            Members[i].CurrentTeamStatus = TeamController.Status.Solo;

        Members.Clear();

        for (int i = 0; i < memberList.Count; i++)
            memberList[i].teamController.team = null;

        OnMembershipChanged?.Invoke();
    }
}
