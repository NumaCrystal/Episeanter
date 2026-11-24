using Episeanter.Content.Shared.Maps.Grids;
using Episeanter.Content.Shared.Events;
using UnityEngine;

namespace Episeanter.Content.Shared.Maps.Events
{
    public struct ChunkLoadedEvent : IEvent
    {
        public uint GridNetId;
        public Vector2Int ChunkPosition;
    }

    public struct PlaceTileEvent : IEvent
    {
        public uint GridNetId;
        public Vector3Int Position;
        public uint TileId;
    }

    public struct RemoveTileEvent : IEvent
    {
        public uint GridNetId;
        public Vector3Int Position;
    }

    public struct TileHealthChangedEvent : IEvent
    {
        public uint GridNetId;
        public Vector3Int Position;
        public float Health;
    }
    
    public struct GridCreatedEvent : IEvent
    {
        public uint GridNetId;
        public GridSystem Grid;
    }
    public struct GridDeletedEvent : IEvent
    {
        public uint GridNetId;
        public GridSystem Grid;
    }
}