using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LittleCms
{
    internal struct cmsCIExy
    {
        public double x;
        public double y;
        public cmsCIEXYZ ToXYZ(double Y = 1.0)
        {
            return new() { X = x * Y / y, Y = Y, Z = (1 - x - y) * Y / y };
        }
    }
    internal class RgbPrimaries
    {
        public cmsCIExy Red { get; set; }
        public cmsCIExy Green { get; set; }
        public cmsCIExy Blue { get; set; }
        public cmsCIExy White { get; set; }

        private RgbPrimaries() { }
        public RgbPrimaries(cmsCIExy red, cmsCIExy green, cmsCIExy blue, cmsCIExy white)
        {
            Red = red;
            Green = green;
            Blue = blue;
            White = white;
        }

        public static RgbPrimaries sRGB { get; } = new RgbPrimaries(new() { x = 0.64, y = 0.33 }, new() { x = 0.30, y = 0.60 }, new() { x = 0.15, y = 0.06 }, new() { x = 0.3127, y = 0.3290 });

        public static RgbPrimaries Rec2020 { get; } = new RgbPrimaries(new() { x = 0.708, y = 0.292 }, new() { x = 0.170, y = 0.797 }, new() { x = 0.131, y = 0.046 }, new() { x = 0.3127, y = 0.3290 });

    }
}
