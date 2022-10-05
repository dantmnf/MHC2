using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using WinRT;

namespace Windows.Graphics.Display.Interop
{
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("7449121C-382B-4705-8DA7-A795BA482013")]
    interface IDisplayInformationStaticsInterop
    {
        unsafe void GetIids(out uint size, out Guid* iids);
        IntPtr GetRuntimeClassName();
        TrustLevel GetTrustLevel();

        IntPtr GetForWindow(IntPtr hwnd, in Guid iid);
        IntPtr GetForMonitor(IntPtr hmon, in Guid iid);
    }
}
