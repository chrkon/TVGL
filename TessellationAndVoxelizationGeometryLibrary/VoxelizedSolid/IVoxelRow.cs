﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace TVGL.Voxelization
{
    public interface IVoxelRow
    {
        bool this[int index] { get; set; }
        int Count { get; }
        void TurnOnRange(ushort lo, ushort hi);
        void TurnOffRange(ushort lo, ushort hi);
        void IntersectRange(ushort lo, ushort hi);
        
        (bool, bool) GetNeighbors(int index);
        void Union(IVoxelRow[] others, int offset = 0);
        void Intersect(IVoxelRow[] others, int offset = 0);
        void Subtract(IVoxelRow[] subtrahends, int offset = 0);
    }
}
