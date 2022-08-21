using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace MHC2Gen
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

    internal class Util
    {
        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr GetCommandLineW();
        public static string GetArgv0()
        {
            var cmdline = Marshal.PtrToStringUni(GetCommandLineW())!.TrimStart();
            string argv0;
            if (cmdline.StartsWith("\""))
            {
                var end = cmdline.IndexOf('"', 1);
                argv0 = cmdline.Substring(0, end + 1);
            }
            else
            {
                var end = cmdline.IndexOf(' ');
                argv0 = end == -1 ? cmdline : cmdline.Substring(0, end);
            }
            return argv0;
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
