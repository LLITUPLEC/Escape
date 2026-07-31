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
}
