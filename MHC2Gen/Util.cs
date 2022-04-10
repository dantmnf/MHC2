using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MHC2Gen.Util
{
    internal ref struct Defer
    {
        private Action? _deferred;
        public Defer(Action deferred)
        {
            _deferred = deferred;
        }

        public void Dispose()
        {
            if (_deferred != null)
            {
                _deferred();
                _deferred = null;
            }
        }
    }
    internal static class ArrayHelper
    {
        public static bool AllEqual(int[] coll)
        {
            if (coll == null || coll.Length == 0) return false;
            var first = coll[0];
            foreach (var item in coll.Skip(1))
            {
                if (item != first) return false;
            }
            return true;
        }

        public static void Broadcast<T>(T[] array, T value)
        {
            for (var i = 0; i < array.Length; i++)
            {
                array[i] = value;
            }
        }
    }
}
