using System;

namespace Project.Friends
{
    public enum FriendsTab
    {
        Friends = 0,
        Online = 1,
    }

    /// <summary>Состояние дружбы Nakama: 0 mutual, 1 исходящая, 2 входящая, 3 блок.</summary>
    public enum FriendRelationState
    {
        Mutual = 0,
        InviteSent = 1,
        InviteReceived = 2,
        Blocked = 3,
    }

    public sealed class FriendListEntry
    {
        public string UserId;
        public string Username;
        public bool Online;
        public FriendRelationState State;
    }

    public sealed class OnlinePlayerEntry
    {
        public string UserId;
        public string Username;
        public int Level;
    }

    [Serializable]
    public sealed class OnlineListRpcResponse
    {
        public bool ok;
        public string err;
        public int total;
        public int shown;
        public OnlinePlayerDto[] players;
        public string[] online_ids;
    }

    [Serializable]
    public sealed class OnlinePlayerDto
    {
        public string user_id;
        public string username;
        public int level;
    }

    public sealed class OnlineListResult
    {
        public bool Ok;
        public string Err;
        public int Total;
        public int Shown;
        public OnlinePlayerEntry[] Players = Array.Empty<OnlinePlayerEntry>();
        public string[] OnlineIds = Array.Empty<string>();
    }

    public sealed class FriendsListResult
    {
        public bool Ok;
        public string Err;
        public FriendListEntry[] Friends = Array.Empty<FriendListEntry>();
    }

    public sealed class FriendsMutationResult
    {
        public bool Ok;
        public string Err;
    }

    [Serializable]
    public sealed class ResolveUsernameRpcResponse
    {
        public bool ok;
        public string err;
        public string user_id;
        public string username;
    }

    public sealed class FriendsRaceInviteResult
    {
        public bool Ok;
        public string Err;
        public string InviteId;
        public string TargetUsername;
    }

    public sealed class FriendsRaceRespondResult
    {
        public bool Ok;
        public string Err;
        public string Status;
        public string MatchId;
        public int PrepSeconds;
        public string OpponentUserId;
        public string OpponentUsername;
    }

    [Serializable]
    public sealed class FriendsRaceInviteRpcResponse
    {
        public bool ok;
        public string err;
        public string invite_id;
        public string target_user_id;
        public string target_username;
        public long expires_at;
        public int required;
        public string resource;
    }

    [Serializable]
    public sealed class FriendsRaceRespondRpcResponse
    {
        public bool ok;
        public string err;
        public string status;
        public string match_id;
        public int prep_seconds;
        public string opponent_user_id;
        public string opponent_username;
        public string invite_id;
    }

    [Serializable]
    public sealed class FriendsRaceClearRpcResponse
    {
        public bool ok;
        public string err;
        public int cleared;
        public string reason;
    }
}
