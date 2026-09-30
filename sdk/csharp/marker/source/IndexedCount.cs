//****************************************************************************************************************************************************
//* File Description
//* ----------------
//* How many vertices and indices an indexed triangle list call wrote. Both are 0 when the call failed.
//*
//* SPDX-FileCopyrightText: Copyright (C) 2026 Mana Battery ApS
//* SPDX-License-Identifier: BSD-3-Clause
//****************************************************************************************************************************************************

namespace MB.FramePacing.Marker
{
  public readonly struct IndexedCount
  {
    public IndexedCount(int vertexCount, int indexCount)
    {
      VertexCount = vertexCount;
      IndexCount = indexCount;
    }

    public readonly int VertexCount;

    public readonly int IndexCount;
  }
}
