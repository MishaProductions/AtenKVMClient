using System.Collections.Generic;

namespace KVMClient
{
    public class CleanDirty
    {
        public BoxInfo Clean = new BoxInfo();
        public List<BoxInfo> Dirty = new List<BoxInfo>();
    }

    public struct RectInfo
    {
        public int x1, x2, y1, y2;

        public RectInfo(int x1, int x2, int y1, int y2) : this()
        {
            this.x1 = x1;
            this.x2 = x2;
            this.y1 = y1;
            this.y2 = y2;
        }
    }
    public struct BoxInfo
    {
        public int x, y, w, h;

        public BoxInfo(int x, int y, int w, int h) : this()
        {
            this.x = x;
            this.y = y;
            this.w = w;
            this.h = h;
        }
    }
}