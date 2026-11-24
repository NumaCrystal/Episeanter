using Mirror;
using UnityEngine;

namespace Episeanter.Content.Shared.Maps.Network
{
    public struct GridSnapshotRequest : NetworkMessage
    {
        public uint RegistryHash;
        public uint GridNetworkId;
    }

    public struct CreateGridRequest : NetworkMessage
    {
        public Vector3 Position;
        public uint TileId;
    }

    public struct PlaceTileRequest : NetworkMessage
    {
        public Vector3Int Position;
        public uint TileId;
        public uint GridNetworkId;
    }

    public struct RemoveTileRequest : NetworkMessage
    {
        public Vector3Int Position;
        public uint GridNetworkId;
    }

    public struct DamageTileRequest : NetworkMessage
    {
        public Vector3Int Position;
        public float Damage;
        public uint GridNetworkId;
    }

    public struct ChunkDataMessage : NetworkMessage
    {
        public Vector2Int ChunkPosition;
        public uint[] Ids;
        public Vector3Int[] DamagedPositions;
        public float[] Healths;
        public uint GridNetworkId;
    }

    public struct TileHealthMessage : NetworkMessage
    {
        public Vector3Int[] Positions;
        public float[] Healths;
        public uint GridNetworkId;
    }

    public struct TilesChangedMessage : NetworkMessage
    {
        public Vector3Int[] Positions;
        public uint[] Ids;
        public uint GridNetworkId;
    }
}
