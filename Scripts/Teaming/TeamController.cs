using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Per-player team controller: status (Solo/Leader/Follower), invite/join/kick/quit
/// flows, and the mutiny vote. Ported from Unity.
///
/// GODOT ADAPTATIONS
/// • UnityAction → System.Action; Collider → Node3D.
/// • Mutiny vote Coroutine → async/await on the SceneTree ProcessFrame signal.
/// • MonoBehaviour reference dropped (async uses the player Node directly).
///
/// ⚠ The interactive flows call player.playerEvents.RequestMessage(...), which is currently a STUB on
/// LocalPlayerManager (logs only) until the UI dialog system is ported — so invites/
/// mutiny won't actually run in-game yet, but status/rules/data are fully live.
/// </summary>
public class TeamController
{
    private PlayerInput gamePad;
    private LocalPlayerManager player;
    private double messageDuration = 10;

    public enum Status { Solo, Leader, Follower }

    private Status _currentStatus;
    public Status CurrentStatus
    {
        get => _currentStatus;
        set { _currentStatus = value; onStatusChange?.Invoke(_currentStatus); }
    }

    private PlayerEvents playerEvents;
    public Action<Status> onStatusChange;

    public Team team;

    private int  voteTally = 0;
    private int  hasVoted  = 0;
    private bool _mutinyActive = false;
    public  int  testvar = 5000;

    private bool IsVoteCompleted() => hasVoted == team.GetFollowers().Count;

    private bool isInitialized = false;
    private bool _isHitConfirmPause;
    private LocalPlayerManager _orbitTarget = null;

    public bool IsInitialized => isInitialized;

    public void OnUpdate()
    {
        LocalPlayerManager targetPlayer = _orbitTarget;
        if (targetPlayer == null) return;

        if (gamePad.GetButtonDown("Right Stick Button"))
        {
            if (team != null && team.IsCurrentMember(targetPlayer))
                player.playerEvents.RequestMessage("Choose Action", () => { QuitTeam(); }, () => { Mutiny(targetPlayer); }, () => { KickMember(targetPlayer); }, () => { }, ("QuitTeam", "Mutiny", "KickMember", "Exit"), 10);
            else
                player.playerEvents.RequestMessage("Choose Action", () => { Invite(targetPlayer); }, () => { JoinRequest(targetPlayer); }, () => { }, ("Invite", "JoinRequest", "Exit"), 10);
        }

        if (gamePad.GetButtonDown("Right Trigger"))
        {
            if (team != null && team.IsCurrentMember(targetPlayer))
                KickMember(targetPlayer);
        }

        if (gamePad.GetButtonDown("Left Trigger"))
        {
            if (team != null) QuitTeam();
        }
    }

    // ── TeamRules feedback messages ───────────────────────────────────────────
    private string MaxTeamsMsg() =>
        $"Cannot form more teams — max {TeamRules.GetMaxTeams()} allowed (largest team has {TeamRules.GetLargestTeamSize()} members).";

    private string MaxTeamSizeMsg() =>
        $"Cannot join — team is full. Max {TeamRules.GetMaxTeamSize()} members per team with {TeamRules.GetActiveTeamCount()} teams active.";

    // ── JoinRequest ───────────────────────────────────────────────────────────
    private void JoinRequest(LocalPlayerManager otherPlayer)
    {
        Action OnOtherPlayerConfirm;
        GD.Print($"{player.playerName}: {player.CurrentTeamStatus} {otherPlayer.playerName}: {otherPlayer.CurrentTeamStatus}");
        switch (player.CurrentTeamStatus, otherPlayer.CurrentTeamStatus)
        {
            case (Status.Solo, Status.Solo):
                OnOtherPlayerConfirm = () =>
                {
                    if (TeamRules.WouldExceedMaxTeams()) { player.playerEvents.RequestMessage(MaxTeamsMsg(), () => { }, "OK", messageDuration); return; }
                    team = new Team();
                    team.AddMember(player);
                    team.AddMember(otherPlayer);
                    otherPlayer.teamController.team = this.team;
                };
                otherPlayer.playerEvents.RequestMessage("Can we team up!", OnOtherPlayerConfirm, "Accept", messageDuration);
                break;

            case (Status.Solo, Status.Leader):
                OnOtherPlayerConfirm = () =>
                {
                    otherPlayer.teamController.team.RemoveAllMembers();
                    if (TeamRules.WouldExceedMaxTeams()) { player.playerEvents.RequestMessage(MaxTeamsMsg(), () => { }, "OK", messageDuration); return; }
                    team = new Team();
                    team.AddMember(player);
                    team.AddMember(otherPlayer);
                    otherPlayer.teamController.team = this.team;
                };
                otherPlayer.playerEvents.RequestMessage("Allow me to join your Team!", OnOtherPlayerConfirm, "Accept", messageDuration);
                break;

            case (Status.Solo, Status.Follower):
                OnOtherPlayerConfirm = () =>
                {
                    Action LearderConfirm = () =>
                    {
                        Team leaderTeam = otherPlayer.teamController.team.GetLeader().teamController.team;
                        if (!leaderTeam.AddMember(player)) { player.playerEvents.RequestMessage(MaxTeamSizeMsg(), () => { }, "OK", messageDuration); return; }
                        player.teamController.team = leaderTeam;
                    };
                    otherPlayer.teamController.team.GetLeader().playerEvents.RequestMessage($"Can {player.playerName} join our team", LearderConfirm, "Accept", messageDuration);
                };
                otherPlayer.playerEvents.RequestMessage("Can I join join your team", OnOtherPlayerConfirm, "Ask", messageDuration);
                break;

            case (Status.Leader, Status.Solo):
                OnOtherPlayerConfirm = () =>
                {
                    team.RemoveAllMembers();
                    if (TeamRules.WouldExceedMaxTeams()) { player.playerEvents.RequestMessage(MaxTeamsMsg(), () => { }, "OK", messageDuration); return; }
                    Team newTeam = new Team();
                    newTeam.AddMember(otherPlayer);
                    newTeam.AddMember(player);
                    this.team = newTeam;
                    otherPlayer.teamController.team = newTeam;
                };
                otherPlayer.playerEvents.RequestMessage("I'll abandon my team if I can join you!", OnOtherPlayerConfirm, "Accept", messageDuration);
                break;

            case (Status.Leader, Status.Leader):
                OnOtherPlayerConfirm = () =>
                {
                    team.RemoveAllMembers();
                    if (!otherPlayer.teamController.team.AddMember(player)) { player.playerEvents.RequestMessage(MaxTeamSizeMsg(), () => { }, "OK", messageDuration); return; }
                    this.team = otherPlayer.teamController.team;
                };
                otherPlayer.playerEvents.RequestMessage("I'll Abandon my team if I can join Yours!", OnOtherPlayerConfirm, "Accept", messageDuration);
                break;

            case (Status.Leader, Status.Follower):
                OnOtherPlayerConfirm = () =>
                {
                    Action OnLeaderConfirm = () =>
                    {
                        Team leaderTeam = otherPlayer.teamController.team.GetLeader().teamController.team;
                        if (!leaderTeam.AddMember(player)) { player.playerEvents.RequestMessage(MaxTeamSizeMsg(), () => { }, "OK", messageDuration); return; }
                        this.team = leaderTeam;
                    };
                    otherPlayer.teamController.team.GetLeader().playerEvents.RequestMessage($"Can {player.playerName} Join our team", OnLeaderConfirm, "Accept", messageDuration);
                };
                otherPlayer.playerEvents.RequestMessage("Leave your Team and Join My Team!", OnOtherPlayerConfirm, "Ask Leader", messageDuration);
                break;

            case (Status.Follower, Status.Solo):
                OnOtherPlayerConfirm = () =>
                {
                    team.RemoveMember(player);
                    if (TeamRules.WouldExceedMaxTeams()) { player.playerEvents.RequestMessage(MaxTeamsMsg(), () => { }, "OK", messageDuration); return; }
                    Team newTeam = new Team();
                    newTeam.AddMember(otherPlayer);
                    newTeam.AddMember(player);
                    this.team = newTeam;
                    otherPlayer.teamController.team = newTeam;
                };
                team.GetLeader().playerEvents.RequestMessage("Can I join you", OnOtherPlayerConfirm, () => { }, ("Accept", "Reject"), messageDuration);
                break;

            case (Status.Follower, Status.Leader):
                OnOtherPlayerConfirm = () =>
                {
                    team.RemoveMember(player);
                    if (!otherPlayer.teamController.team.AddMember(player)) { player.playerEvents.RequestMessage(MaxTeamSizeMsg(), () => { }, "OK", messageDuration); return; }
                    this.team = otherPlayer.teamController.team;
                };
                team.GetLeader().playerEvents.RequestMessage("Can I join your team?", OnOtherPlayerConfirm, () => { }, ("Yes", "No"), messageDuration);
                break;

            case (Status.Follower, Status.Follower):
                Action OnLeaderConfirmFF = () =>
                {
                    OnOtherPlayerConfirm = () =>
                    {
                        team.RemoveMember(player);
                        Team leaderTeam = otherPlayer.teamController.team.GetLeader().teamController.team;
                        if (!leaderTeam.AddMember(player)) { player.playerEvents.RequestMessage(MaxTeamSizeMsg(), () => { }, "OK", messageDuration); return; }
                        this.team = otherPlayer.teamController.team;
                    };
                    otherPlayer.playerEvents.RequestMessage($"Can {player.playerName} join our team", OnOtherPlayerConfirm, "Confirm", messageDuration);
                };
                team.GetLeader().playerEvents.RequestMessage("Can I join your team?", OnLeaderConfirmFF, () => { }, ("Yes", "No"), messageDuration);
                break;
        }
    }

    // ── Invite ────────────────────────────────────────────────────────────────
    public void Invite(LocalPlayerManager otherPlayer)
    {
        Action OnOtherPlayerConfirm;
        Action OnLeaderConfirm;
        GD.Print($"{player.playerName}: {player.CurrentTeamStatus} {otherPlayer.playerName}: {otherPlayer.CurrentTeamStatus}");
        switch (player.CurrentTeamStatus, otherPlayer.CurrentTeamStatus)
        {
            case (Status.Solo, Status.Solo):
                OnOtherPlayerConfirm = () =>
                {
                    if (TeamRules.WouldExceedMaxTeams()) { player.playerEvents.RequestMessage(MaxTeamsMsg(), () => { }, "OK", messageDuration); return; }
                    team = new Team();
                    team.AddMember(player);
                    team.AddMember(otherPlayer);
                    otherPlayer.teamController.team = this.team;
                };
                otherPlayer.playerEvents.RequestMessage("Lets Team up!", OnOtherPlayerConfirm, () => { }, ("Team Up", "Decline"), messageDuration);
                break;

            case (Status.Solo, Status.Leader):
                OnOtherPlayerConfirm = () =>
                {
                    otherPlayer.teamController.team.RemoveAllMembers();
                    if (TeamRules.WouldExceedMaxTeams()) { player.playerEvents.RequestMessage(MaxTeamsMsg(), () => { }, "OK", messageDuration); return; }
                    team = new Team();
                    team.AddMember(player);
                    team.AddMember(otherPlayer);
                    otherPlayer.teamController.team = this.team;
                };
                otherPlayer.playerEvents.RequestMessage("Abandon your Team and Lets Team up!", OnOtherPlayerConfirm, () => { }, ("Team Up", "Decline"), messageDuration);
                break;

            case (Status.Solo, Status.Follower):
                OnOtherPlayerConfirm = () =>
                {
                    otherPlayer.teamController.team.RemoveMember(otherPlayer);
                    if (TeamRules.WouldExceedMaxTeams()) { player.playerEvents.RequestMessage(MaxTeamsMsg(), () => { }, "OK", messageDuration); return; }
                    team = new Team();
                    team.AddMember(player);
                    team.AddMember(otherPlayer);
                    otherPlayer.teamController.team = this.team;
                };
                otherPlayer.playerEvents.RequestMessage("Leave your Team and Lets Team up!", OnOtherPlayerConfirm, () => { }, ("Team Up", "Decline"), messageDuration);
                break;

            case (Status.Leader, Status.Solo):
                OnOtherPlayerConfirm = () =>
                {
                    if (!team.AddMember(otherPlayer)) { player.playerEvents.RequestMessage(MaxTeamSizeMsg(), () => { }, "OK", messageDuration); return; }
                    otherPlayer.teamController.team = this.team;
                };
                otherPlayer.playerEvents.RequestMessage("Join my Team!", OnOtherPlayerConfirm, () => { }, ("Follow", "Decline"), messageDuration);
                break;

            case (Status.Leader, Status.Leader):
                OnOtherPlayerConfirm = () =>
                {
                    otherPlayer.teamController.team.RemoveAllMembers();
                    if (!team.AddMember(otherPlayer)) { player.playerEvents.RequestMessage(MaxTeamSizeMsg(), () => { }, "OK", messageDuration); return; }
                    otherPlayer.teamController.team = this.team;
                };
                otherPlayer.playerEvents.RequestMessage("Abandon your team and Join my Team!", OnOtherPlayerConfirm, () => { }, ("Follow", "Decline"), messageDuration);
                break;

            case (Status.Leader, Status.Follower):
                OnOtherPlayerConfirm = () =>
                {
                    otherPlayer.teamController.team.RemoveMember(otherPlayer);
                    if (!team.AddMember(otherPlayer)) { player.playerEvents.RequestMessage(MaxTeamSizeMsg(), () => { }, "OK", messageDuration); return; }
                    otherPlayer.teamController.team = this.team;
                };
                otherPlayer.playerEvents.RequestMessage("Leave your Team and Join My Team!", OnOtherPlayerConfirm, () => { }, ("Follow", "Decline"), messageDuration);
                break;

            case (Status.Follower, Status.Solo):
                OnLeaderConfirm = () =>
                {
                    Action onConfirm = () =>
                    {
                        team.GetLeader().teamController.Invite(otherPlayer);
                        otherPlayer.teamController.team = team.GetLeader().teamController.team;
                    };
                    otherPlayer.playerEvents.RequestMessage("Our Leader says you can join our team", onConfirm, () => { }, ("Follow", "Decline"), messageDuration);
                };
                team.GetLeader().playerEvents.RequestMessage($"Can I invite {otherPlayer.playerName} to join our team?", OnLeaderConfirm, () => { }, ("Yes", "No"), messageDuration);
                break;

            case (Status.Follower, Status.Leader):
                OnLeaderConfirm = () =>
                {
                    Action onConfirm = () =>
                    {
                        otherPlayer.teamController.team.RemoveAllMembers();
                        team.GetLeader().teamController.Invite(otherPlayer);
                        otherPlayer.teamController.team = team.GetLeader().teamController.team;
                    };
                    otherPlayer.playerEvents.RequestMessage("Abandon your team and follow our Leader", onConfirm, () => { }, ("Follow", "Decline"), messageDuration);
                };
                team.GetLeader().playerEvents.RequestMessage($"Can I invite {otherPlayer.playerName} to join our team?", OnLeaderConfirm, () => { }, ("Yes", "No"), messageDuration);
                break;

            case (Status.Follower, Status.Follower):
                OnLeaderConfirm = () =>
                {
                    Action onConfirm = () =>
                    {
                        otherPlayer.teamController.team.RemoveMember(otherPlayer);
                        team.GetLeader().teamController.Invite(otherPlayer);
                        otherPlayer.teamController.team = team.GetLeader().teamController.team;
                    };
                    otherPlayer.playerEvents.RequestMessage("Our Leader says you can join our team", onConfirm, () => { }, ("Follow", "Decline"), messageDuration);
                };
                team.GetLeader().playerEvents.RequestMessage($"Can I to invite {otherPlayer.playerName} to join our team?", OnLeaderConfirm, () => { }, ("Yes", "No"), messageDuration);
                break;
        }
    }

    void KickMember(LocalPlayerManager otherPlayer)
    {
        switch (player.CurrentTeamStatus)
        {
            case Status.Leader:
                team.RemoveMember(otherPlayer);
                break;
            case Status.Follower:
                Action onLeaderConfirm = () => { team.RemoveMember(otherPlayer); };
                team.GetLeader().playerEvents.RequestMessage($"Please Kick {otherPlayer.playerName} from the team", onLeaderConfirm, "Kick", messageDuration);
                break;
            case Status.Solo:
                break;
        }
    }

    void QuitTeam()
    {
        if (team == null) return;
        switch (player.CurrentTeamStatus)
        {
            case Status.Leader:
                player.playerEvents.RequestMessage("Do you want to quit your team?", () => { team.RemoveAllMembers(); }, () => { }, ("Disband", "No"), messageDuration);
                break;
            case Status.Follower:
                player.playerEvents.RequestMessage("Do you want to quit your team?", () => { team.RemoveMember(player); }, () => { }, ("Leave", "No"), messageDuration);
                break;
            case Status.Solo:
                break;
        }
    }

    void Mutiny(LocalPlayerManager leader)
    {
        if (team == null) return;
        if (player.CurrentTeamStatus != Status.Follower) return;

        if (team.IsCurrentMember(leader) & leader.CurrentTeamStatus == Status.Leader)
        {
            if (!_mutinyActive)
            {
                _mutinyActive = true;
                VoteQue(leader);
                Action onConfirm = () => { };
                leader.playerEvents.RequestMessage($"{player.playerName} seeks to depose you!", onConfirm, () => { }, ("Revenge", "Forgive"), 10);
                List<LocalPlayerManager> followers = team.GetFollowers();
                for (int i = 0; i < followers.Count; i++)
                {
                    Action onMemberConfirm = () => { VoteYes(); };
                    Action onMemberReject  = () => { VoteNo(); };
                    followers[i].playerEvents.RequestMessage("Depose the Leader Yay or Nay", onMemberConfirm, onMemberReject, ("Yay", "Nay"), 10);
                }
            }
        }
    }

    /// <summary>Mutiny vote tally — Godot port of the Unity coroutine (await ProcessFrame until done).</summary>
    async void VoteQue(LocalPlayerManager leader)
    {
        GD.Print("VoteQue Activated");
        while (!IsVoteCompleted())
        {
            if (player == null || !GodotObject.IsInstanceValid(player)) { _mutinyActive = false; return; }
            await player.ToSignal(player.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        GD.Print("Vote Completed");

        if (voteTally >= team.GetFollowers().Count)
        {
            List<LocalPlayerManager> followers = leader.teamController.team.GetFollowers();
            followers.Remove(player);
            leader.teamController.team.RemoveAllMembers();

            if (followers.Count >= 1)
            {
                team = new Team();
                team.AddMember(player);
                for (int i = 0; i < followers.Count; i++)
                    team.AddMember(followers[i]);
            }
        }

        hasVoted      = 0;
        voteTally     = 0;
        _mutinyActive = false;
    }

    void VoteYes() { voteTally++; hasVoted++; }
    void VoteNo()  { hasVoted++; }

    public void Initialize(PlayerInput gamePad, LocalPlayerManager player, PlayerEvents playerEvents)
    {
        this.gamePad      = gamePad;
        this.player       = player;
        this.CurrentStatus = Status.Solo;
        this.playerEvents = playerEvents;
        this.playerEvents.OnUpdate             += OnUpdate;
        this.playerEvents.OnOrbitTargetChanged += OnOrbitTargetChanged;
        this.playerEvents.OnHitConfirm         += OnHitConfirm;
        this.playerEvents.OnHitConfirmPauseEnd += OnHitConfirmPauseEnd;
        this.onStatusChange                    += OnStatusChanged;
        isInitialized = true;
    }

    public void Deactivate()
    {
        this.onStatusChange                    -= OnStatusChanged;
        this.playerEvents.OnUpdate             -= OnUpdate;
        this.playerEvents.OnOrbitTargetChanged -= OnOrbitTargetChanged;
        this.playerEvents.OnHitConfirm         -= OnHitConfirm;
        this.playerEvents.OnHitConfirmPauseEnd -= OnHitConfirmPauseEnd;
        this.playerEvents = null;
        isInitialized = false;
    }

    public void OnHitConfirm((Node3D hitbox, Node3D hurtbox) hitInfo)         => _isHitConfirmPause = true;
    public void OnHitConfirmPauseEnd((Node3D hitbox, Node3D hurtbox) hitInfo) => _isHitConfirmPause = false;

    void OnOrbitTargetChanged(LocalPlayerManager target, bool isTargeting) => _orbitTarget = target;

    void OnStatusChanged(Status newStatus) => playerEvents.OnTeamChanged?.Invoke();
}
