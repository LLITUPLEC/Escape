using System;
using UnityEngine;

namespace Project.Networking
{
    public static class OpCodes
    {
        public const long Transform = 1;
        public const long PlayerLeft = 2;
        public const long DoorOpened = 3;
        public const long MatchWon = 4;
    }

    [Serializable]
    public sealed class NetTransformState
    {
        public float px;
        public float py;
        public float pz;
        public float ry;

        public static NetTransformState From(Transform t)
        {
            var p = t.position;
            return new NetTransformState
            {
                px = p.x,
                py = p.y,
                pz = p.z,
                ry = t.eulerAngles.y,
            };
        }

        public void ApplyTo(Transform t)
        {
            t.position = new Vector3(px, py, pz);
            t.rotation = Quaternion.Euler(0f, ry, 0f);
        }
    }

    [Serializable]
    public sealed class NetDoorOpenedState
    {
        public int doorId;
        public int attemptsUsed;
    }

    [Serializable]
    public sealed class NetMatchWonState
    {
        public string winnerUserId;
    }
}

