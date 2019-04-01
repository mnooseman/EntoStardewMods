using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Xna.Framework;

namespace SundropCity.Hotel
{
    class FurnitureInfo
    {
        public Rectangle ImageRect;
        public Point TileSize;
        public bool NeedsWall;
        public bool IsFrame;
        public bool IsCarpet;
        public bool IsLamp;
        public bool HasSlots;
        public bool Flipped;
        public Point[] Slots;
    }
}
