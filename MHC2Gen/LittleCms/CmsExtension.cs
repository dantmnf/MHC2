using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LittleCms
{
    internal static class CmsExtension
    {

        public static cmsCIExyY ToXY(this cmsCIEXYZ xyz)
        {
            var sum = xyz.X + xyz.Y + xyz.Z;
            return new() { x = xyz.X / sum, y = xyz.Y / sum , Y = 1};
        }

        public static cmsCIExy ToXY(this cmsCIExyY xy)
        {
            return new() { x = xy.x, y = xy.y };
        }

        public static cmsCIEXYZ ToXYZ(this cmsCIExyY xyY)
        {
            return new() { X = xyY.x * xyY.Y / xyY.y, Y = xyY.Y, Z = (1 - xyY.x - xyY.y) * xyY.Y / xyY.y };
        }
    }
}
