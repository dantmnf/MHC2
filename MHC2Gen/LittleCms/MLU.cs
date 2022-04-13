using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace LittleCms
{
    public class MLU : IDisposable {

        public const string NoLanguage = "\0\0";
        public const string NoCountry = "\0\0";

        private IntPtr handle;
        private bool ownHandle;
        
        public IntPtr Handle => handle;
        public MLU(IntPtr handle, bool ownHandle = false) {
            this.handle = handle;
            this.ownHandle = ownHandle;
        }

        public void Dispose() {
            if (ownHandle && handle != IntPtr.Zero) {
                CmsNative.cmsMLUfree(handle);
                handle = IntPtr.Zero;
            }
        }

        public static MLU FromUnlocalizedString(string str) {
            IntPtr handle = CmsNative.cmsMLUalloc(IntPtr.Zero, 1);
            if (handle == IntPtr.Zero) {
                throw new OutOfMemoryException();
            }
            if (CmsNative.cmsMLUsetWide(handle, NoLanguage, NoCountry, str) == 0) {
                throw new Exception("cmsMLUsetWide failed");
            }
            return new MLU(handle, true);
        }

        public unsafe string? GetLocalizedString(string languageCode, string countryCode) {
            var len = CmsNative.cmsMLUgetWide(handle, languageCode, countryCode, null, 0);
            if (len == 0) {
                return null;
            }
            var buf = stackalloc char[(int)len];
            if (CmsNative.cmsMLUgetWide(handle, languageCode, countryCode, buf, len) == 0) {
                throw new Exception("cmsMLUgetWide failed");
            }
            return new string(buf);
        }

        public string? GetUnlocalizedString() => GetLocalizedString(NoLanguage, NoCountry);
    }
}