using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using static LittleCms.CmsNative;

namespace LittleCms
{
    public class IccProfile : CmsObject
    {
        public IccProfile(IntPtr handle, bool moveOwnership) : base(handle, moveOwnership) { }

        public static IccProfile Create_sRGB()
        {
            return new IccProfile(CheckError(cmsCreate_sRGBProfile()), true);
        }

        public static IccProfile CreateXYZ()
        {
            return new IccProfile(CheckError(cmsCreateXYZProfile()), true);
        }

        public static unsafe IccProfile CreateRGB(CIExyY whitepoint, CIExyYTRIPLE primaries, ToneCurveTriple transfer)
        {
            Span<IntPtr> transferhandle = stackalloc IntPtr[] { transfer.Red.Handle, transfer.Green.Handle, transfer.Blue.Handle };
            var handle = CheckError(cmsCreateRGBProfile(whitepoint, primaries, ref transferhandle[0]));
            return new(handle, true);
        }
        public static unsafe IccProfile Open(ReadOnlySpan<byte> bytes)
        {
            fixed (byte* ptr = bytes)
            {
                var handle = CheckError(cmsOpenProfileFromMem(ptr, (uint)bytes.Length));
                return new IccProfile(handle, true);
            }
        }

        public unsafe byte[] GetBytes()
        {
            uint newlen = 0;
            CheckError(cmsSaveProfileToMem(Handle, null, ref newlen));
            var newicc = new byte[newlen];
            fixed (byte* ptr = newicc)
                CheckError(cmsSaveProfileToMem(Handle, ptr, ref newlen));
            return newicc;
        }

        protected override void FreeObject()
        {
            cmsCloseProfile(Handle);
        }

        public IntPtr ReadTag(TagSignature sig)
        {
            return cmsReadTag(Handle, sig);
        }

        public bool ContainsTag(TagSignature sig) => ReadTag(sig) != IntPtr.Zero;

        public unsafe T? ReadTag<T>(TagSignature sig)
        {
            var ptr = cmsReadTag(Handle, sig);
            if (ptr == IntPtr.Zero) return default;
            switch (sig)
            {
                case TagSignature.Chromaticity:
                    return (T?)(object)(*(CIExyYTRIPLE*)ptr);
                case TagSignature.MediaWhitePoint:
                case TagSignature.BlueColorant:
                case TagSignature.GreenColorant:
                case TagSignature.Luminance:
                case TagSignature.MediaBlackPoint:
                case TagSignature.RedColorant:
                    return (T?)(object)(*(CIEXYZ*)ptr);
                case TagSignature.ChromaticAdaptation:
                    {
                        var result = new double[3, 3];
                        new ReadOnlySpan<double>((double*)ptr, 9).CopyTo(MemoryMarshal.CreateSpan(ref result[0, 0], 9));
                        return (T?)(object)result;
                    }
                case TagSignature.Data:
                case TagSignature.Ps2CRD0:
                case TagSignature.Ps2CRD1:
                case TagSignature.Ps2CRD2:
                case TagSignature.Ps2CRD3:
                case TagSignature.Ps2CSA:
                case TagSignature.Ps2RenderingIntent:
                    return (T?)(object)_ICCData.ToManaged((_ICCData*)ptr);
                case TagSignature.BlueTRC:
                case TagSignature.GrayTRC:
                case TagSignature.GreenTRC:
                case TagSignature.RedTRC:
                    return (T?)(object)ToneCurve.CopyFromObject(ptr);
                case TagSignature.Vcgt:
                    {
                        var curves = (IntPtr*)ptr;
                        return (T?)(object)new ToneCurveTriple(ToneCurve.CopyFromObject(curves[0]), ToneCurve.CopyFromObject(curves[1]), ToneCurve.CopyFromObject(curves[2]));
                    }
                case TagSignature.CharTarget:
                case TagSignature.Copyright:
                case TagSignature.DeviceMfgDesc:
                case TagSignature.DeviceModelDesc:
                case TagSignature.ProfileDescription:
                case TagSignature.ScreeningDesc:
                case TagSignature.ViewingCondDesc:
                    return (T?)(object)MLU.CopyFromObject(ptr);
                default:
                    throw new NotImplementedException();
            }
        }

        public void WriteTag(TagSignature sig, IntPtr handle)
        {
            CheckError(cmsWriteTag(Handle, sig, handle));
        }

        public void WriteTag(TagSignature sig, CmsObject obj)
        {
            WriteTag(sig, obj.Handle);
        }

        public unsafe void WriteTag(TagSignature sig, double[,] chad)
        {
            if (sig != TagSignature.ChromaticAdaptation) throw new ArgumentException();
            fixed (double* ptr = &chad[0, 0])
                WriteTag(sig, (IntPtr)ptr);
        }

        public unsafe void WriteTag(TagSignature sig, in CIEXYZ xyz)
        {
            switch (sig)
            {
                case TagSignature.MediaWhitePoint:
                case TagSignature.BlueColorant:
                case TagSignature.GreenColorant:
                case TagSignature.Luminance:
                case TagSignature.MediaBlackPoint:
                case TagSignature.RedColorant:
                    break;
                default:
                    throw new ArgumentException();
            }
            fixed (void* ptr = &xyz)
            {
                WriteTag(sig, (IntPtr)ptr);
            }
        }

        public unsafe void WriteTag(TagSignature sig, in CIExyYTRIPLE xyy3)
        {
            if (sig != TagSignature.Chromaticity) throw new ArgumentException();
            fixed (void* ptr = &xyy3)
            {
                WriteTag(sig, (IntPtr)ptr);
            }
        }

        public unsafe void WriteTag(TagSignature sig, in ICCData obj)
        {
            switch (sig)
            {
                case TagSignature.Data:
                case TagSignature.Ps2CRD0:
                case TagSignature.Ps2CRD1:
                case TagSignature.Ps2CRD2:
                case TagSignature.Ps2CRD3:
                case TagSignature.Ps2CSA:
                case TagSignature.Ps2RenderingIntent:
                    break;
                default:
                    throw new ArgumentException();
            }
            var data = new byte[sizeof(_ICCDataHeader) + obj.Data.Length];
            obj.Data.Span.CopyTo(data.AsSpan().Slice(sizeof(_ICCDataHeader)));
            fixed (byte* ptr = data)
            {
                var header = (_ICCDataHeader*)ptr;
                header->flag = obj.Flag;
                header->len = (uint)obj.Data.Length;

                WriteTag(sig, (IntPtr)ptr);
            }
        }

        public unsafe void WriteTag(TagSignature sig, in ToneCurveTriple obj)
        {
            if (sig != TagSignature.Vcgt) throw new ArgumentException();
            var handles = stackalloc IntPtr[] { obj.Red.Handle, obj.Green.Handle, obj.Blue.Handle };
            WriteTag(sig, (IntPtr)handles);
        }

        public unsafe byte[]? ReadRawTag(TagSignature sig)
        {
            if (!cmsIsTag(Handle, sig)) return null;
            var len = cmsReadRawTag(Handle, sig, null, 0);
            var buf = new byte[len];
            fixed (byte* ptr = buf)
                len = cmsReadRawTag(Handle, sig, ptr, len);
            return buf;
        }

        public void WriteRawTag(TagSignature sig, ReadOnlySpan<byte> bytes)
        {
            CheckError(cmsWriteRawTag(Handle, sig, in bytes[0], (uint)bytes.Length));
        }

        public void ComputeProfileId()
        {
            CheckError(cmsMD5computeID(Handle));
        }

        public HeaderFlags HeaderFlags
        {
            get => cmsGetHeaderFlags(Handle);
            set => cmsSetHeaderFlags(Handle, value);
        }

        public unsafe string GetInfo(InfoType info, string languageCode = MLU.NoLanguage, string countryCode = MLU.NoCountry)
        {
            var (lang, cont) = MLU.EncodeLanguageCountryCode(languageCode, countryCode);
            var len = cmsGetProfileInfo(Handle, info, in lang, in cont, null, 0);
            if (len == 0) return "";
            var buf = new byte[len];
            fixed (byte* ptr = buf)
                len = cmsGetProfileInfo(Handle, info, in lang, in cont, ptr, len);
            var s = WcharEncoding.GetString(buf)[..^1];
            return s;
        }

        public ulong HeaderAttributes
        {
            get {
                cmsGetHeaderAttributes(Handle, out var attr);
                return attr;
            }
            set => cmsSetHeaderAttributes(Handle, value);
        }

        public Guid HeaderProfileId
        {
            get
            {
                cmsGetHeaderProfileID(Handle, out var attr);
                return attr;
            }
            set => cmsSetHeaderProfileID(Handle, value);
        }

        public DateTime HeaderCreationDateTime
        {
            get
            {
                CheckError(cmsGetHeaderCreationDateTime(Handle, out var tm));
                return tm.ToDateTime();
            }
        }

        public RenderingIntent HeaderRenderingIntent
        {
            get => (RenderingIntent)cmsGetHeaderRenderingIntent(Handle);
            set => cmsSetHeaderRenderingIntent(Handle, (uint)value);
        }

        public uint HeaderManufacturer
        {
            get => cmsGetHeaderManufacturer(Handle);
            set => cmsSetHeaderManufacturer(Handle, value);
        }

        public uint HeaderCreator
        {
            get => cmsGetHeaderCreator(Handle);
        }

        public uint HeaderModel
        {
            get => cmsGetHeaderModel(Handle);
            set => cmsSetHeaderModel(Handle, value);
        }

        public ColorSpaceSignature PCS
        {
            get => cmsGetPCS(Handle);
            set => cmsSetPCS(Handle, value);
        }


        public ColorSpaceSignature ColorSpace
        {
            get => cmsGetColorSpace(Handle);
            set => cmsSetColorSpace(Handle, value);
        }

        public ProfileClassSignature DeviceClass
        {
            get => cmsGetDeviceClass(Handle);
            set => cmsSetDeviceClass(Handle, value);
        }

        public Version ProfileVersion
        {
            get {
                var encver = cmsGetEncodedICCversion(Handle);
                return new((int)(encver >> 24), (int)((encver & 0x00F00000) >> 20), (int)((encver & 0x000F0000) >> 16));
            }
            set
            {
                var encver = (((uint)value.Major & 0xFF) << 24) | (((uint)value.Minor & 0xF) << 20) | (((uint)value.Build & 0xF) << 16);
                cmsSetEncodedICCversion(Handle, encver);
            }
        }
    }
}
