using Mirror;
using UnityEngine;

namespace Episeanter.Content.Shared.Maps
{
    public struct GridSnapshotRequest : NetworkMessage
    {
        public uint RegistryHash;
    }

    public struct PlaceTileRequest : NetworkMessage
    {
        public Vector3Int Position;
        public uint TileId;
    }

    public struct RemoveTileRequest : NetworkMessage
    {
        public Vector3Int Position;
    }

    public struct DamageTileRequest : NetworkMessage
    {
        public Vector3Int Position;
        public float Damage;
    }

    public struct ChunkDataMessage : NetworkMessage
    {
        public Vector2Int ChunkPosition;
        public uint[] Ids;
        public Vector3Int[] DamagedPositions;
        public float[] Healths;
    }

    public struct TileHealthMessage : NetworkMessage
    {
        public Vector3Int[] Positions;
        public float[] Healths;
    }

    public struct TilesChangedMessage : NetworkMessage
    {
        public Vector3Int[] Positions;
        public uint[] Ids;
    }
}
