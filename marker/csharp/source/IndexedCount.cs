//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* How many vertices and indices an indexed triangle list call wrote. Both are 0 when the call failed.
//*
//* (c) 2026 Mana Battery
//****************************************************************************************************************************************************

namespace MB.FrameMarker
{
  public readonly struct IndexedCount
  {
    public IndexedCount(int vertexCount, int indexCount)
    {
      VertexCount = vertexCount;
      IndexCount = indexCount;
    }

    public int VertexCount { get; }

    public int IndexCount { get; }
  }
}
