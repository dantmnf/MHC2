using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace LittleCms
{

    public enum cmsTagSignature
    {
        cmsSigAToB0Tag = 0x41324230,  // 'A2B0'
        cmsSigAToB1Tag = 0x41324231,  // 'A2B1'
        cmsSigAToB2Tag = 0x41324232,  // 'A2B2'
        cmsSigBlueColorantTag = 0x6258595A,  // 'bXYZ'
        cmsSigBlueMatrixColumnTag = 0x6258595A,  // 'bXYZ'
        cmsSigBlueTRCTag = 0x62545243,  // 'bTRC'
        cmsSigBToA0Tag = 0x42324130,  // 'B2A0'
        cmsSigBToA1Tag = 0x42324131,  // 'B2A1'
        cmsSigBToA2Tag = 0x42324132,  // 'B2A2'
        cmsSigCalibrationDateTimeTag = 0x63616C74,  // 'calt'
        cmsSigCharTargetTag = 0x74617267,  // 'targ'
        cmsSigChromaticAdaptationTag = 0x63686164,  // 'chad'
        cmsSigChromaticityTag = 0x6368726D,  // 'chrm'
        cmsSigColorantOrderTag = 0x636C726F,  // 'clro'
        cmsSigColorantTableTag = 0x636C7274,  // 'clrt'
        cmsSigColorantTableOutTag = 0x636C6F74,  // 'clot'
        cmsSigColorimetricIntentImageStateTag = 0x63696973,  // 'ciis'
        cmsSigCopyrightTag = 0x63707274,  // 'cprt'
        cmsSigCrdInfoTag = 0x63726469,  // 'crdi'
        cmsSigDataTag = 0x64617461,  // 'data'
        cmsSigDateTimeTag = 0x6474696D,  // 'dtim'
        cmsSigDeviceMfgDescTag = 0x646D6E64,  // 'dmnd'
        cmsSigDeviceModelDescTag = 0x646D6464,  // 'dmdd'
        cmsSigDeviceSettingsTag = 0x64657673,  // 'devs'
        cmsSigDToB0Tag = 0x44324230,  // 'D2B0'
        cmsSigDToB1Tag = 0x44324231,  // 'D2B1'
        cmsSigDToB2Tag = 0x44324232,  // 'D2B2'
        cmsSigDToB3Tag = 0x44324233,  // 'D2B3'
        cmsSigBToD0Tag = 0x42324430,  // 'B2D0'
        cmsSigBToD1Tag = 0x42324431,  // 'B2D1'
        cmsSigBToD2Tag = 0x42324432,  // 'B2D2'
        cmsSigBToD3Tag = 0x42324433,  // 'B2D3'
        cmsSigGamutTag = 0x67616D74,  // 'gamt'
        cmsSigGrayTRCTag = 0x6b545243,  // 'kTRC'
        cmsSigGreenColorantTag = 0x6758595A,  // 'gXYZ'
        cmsSigGreenMatrixColumnTag = 0x6758595A,  // 'gXYZ'
        cmsSigGreenTRCTag = 0x67545243,  // 'gTRC'
        cmsSigLuminanceTag = 0x6C756d69,  // 'lumi'
        cmsSigMeasurementTag = 0x6D656173,  // 'meas'
        cmsSigMediaBlackPointTag = 0x626B7074,  // 'bkpt'
        cmsSigMediaWhitePointTag = 0x77747074,  // 'wtpt'
        cmsSigNamedColorTag = 0x6E636f6C,  // 'ncol' // Deprecated by the ICC
        cmsSigNamedColor2Tag = 0x6E636C32,  // 'ncl2'
        cmsSigOutputResponseTag = 0x72657370,  // 'resp'
        cmsSigPerceptualRenderingIntentGamutTag = 0x72696730,  // 'rig0'
        cmsSigPreview0Tag = 0x70726530,  // 'pre0'
        cmsSigPreview1Tag = 0x70726531,  // 'pre1'
        cmsSigPreview2Tag = 0x70726532,  // 'pre2'
        cmsSigProfileDescriptionTag = 0x64657363,  // 'desc'
        cmsSigProfileDescriptionMLTag = 0x6473636d,  // 'dscm'
        cmsSigProfileSequenceDescTag = 0x70736571,  // 'pseq'
        cmsSigProfileSequenceIdTag = 0x70736964,  // 'psid'
        cmsSigPs2CRD0Tag = 0x70736430,  // 'psd0'
        cmsSigPs2CRD1Tag = 0x70736431,  // 'psd1'
        cmsSigPs2CRD2Tag = 0x70736432,  // 'psd2'
        cmsSigPs2CRD3Tag = 0x70736433,  // 'psd3'
        cmsSigPs2CSATag = 0x70733273,  // 'ps2s'
        cmsSigPs2RenderingIntentTag = 0x70733269,  // 'ps2i'
        cmsSigRedColorantTag = 0x7258595A,  // 'rXYZ'
        cmsSigRedMatrixColumnTag = 0x7258595A,  // 'rXYZ'
        cmsSigRedTRCTag = 0x72545243,  // 'rTRC'
        cmsSigSaturationRenderingIntentGamutTag = 0x72696732,  // 'rig2'
        cmsSigScreeningDescTag = 0x73637264,  // 'scrd'
        cmsSigScreeningTag = 0x7363726E,  // 'scrn'
        cmsSigTechnologyTag = 0x74656368,  // 'tech'
        cmsSigUcrBgTag = 0x62666420,  // 'bfd '
        cmsSigViewingCondDescTag = 0x76756564,  // 'vued'
        cmsSigViewingConditionsTag = 0x76696577,  // 'view'
        cmsSigVcgtTag = 0x76636774,  // 'vcgt'
        cmsSigMetaTag = 0x6D657461,  // 'meta'
        cmsSigArgyllArtsTag = 0x61727473   // 'arts'
    }

    /// <summary>
    /// ICC Color spaces
    /// </summary>
    enum cmsColorSpaceSignature
    {
        cmsSigXYZData = 0x58595A20,  // 'XYZ '
        cmsSigLabData = 0x4C616220,  // 'Lab '
        cmsSigLuvData = 0x4C757620,  // 'Luv '
        cmsSigYCbCrData = 0x59436272,  // 'YCbr'
        cmsSigYxyData = 0x59787920,  // 'Yxy '
        cmsSigRgbData = 0x52474220,  // 'RGB '
        cmsSigGrayData = 0x47524159,  // 'GRAY'
        cmsSigHsvData = 0x48535620,  // 'HSV '
        cmsSigHlsData = 0x484C5320,  // 'HLS '
        cmsSigCmykData = 0x434D594B,  // 'CMYK'
        cmsSigCmyData = 0x434D5920,  // 'CMY '
        cmsSigMCH1Data = 0x4D434831,  // 'MCH1'
        cmsSigMCH2Data = 0x4D434832,  // 'MCH2'
        cmsSigMCH3Data = 0x4D434833,  // 'MCH3'
        cmsSigMCH4Data = 0x4D434834,  // 'MCH4'
        cmsSigMCH5Data = 0x4D434835,  // 'MCH5'
        cmsSigMCH6Data = 0x4D434836,  // 'MCH6'
        cmsSigMCH7Data = 0x4D434837,  // 'MCH7'
        cmsSigMCH8Data = 0x4D434838,  // 'MCH8'
        cmsSigMCH9Data = 0x4D434839,  // 'MCH9'
        cmsSigMCHAData = 0x4D434841,  // 'MCHA'
        cmsSigMCHBData = 0x4D434842,  // 'MCHB'
        cmsSigMCHCData = 0x4D434843,  // 'MCHC'
        cmsSigMCHDData = 0x4D434844,  // 'MCHD'
        cmsSigMCHEData = 0x4D434845,  // 'MCHE'
        cmsSigMCHFData = 0x4D434846,  // 'MCHF'
        cmsSigNamedData = 0x6e6d636c,  // 'nmcl'
        cmsSig1colorData = 0x31434C52,  // '1CLR'
        cmsSig2colorData = 0x32434C52,  // '2CLR'
        cmsSig3colorData = 0x33434C52,  // '3CLR'
        cmsSig4colorData = 0x34434C52,  // '4CLR'
        cmsSig5colorData = 0x35434C52,  // '5CLR'
        cmsSig6colorData = 0x36434C52,  // '6CLR'
        cmsSig7colorData = 0x37434C52,  // '7CLR'
        cmsSig8colorData = 0x38434C52,  // '8CLR'
        cmsSig9colorData = 0x39434C52,  // '9CLR'
        cmsSig10colorData = 0x41434C52,  // 'ACLR'
        cmsSig11colorData = 0x42434C52,  // 'BCLR'
        cmsSig12colorData = 0x43434C52,  // 'CCLR'
        cmsSig13colorData = 0x44434C52,  // 'DCLR'
        cmsSig14colorData = 0x45434C52,  // 'ECLR'
        cmsSig15colorData = 0x46434C52,  // 'FCLR'
        cmsSigLuvKData = 0x4C75764B   // 'LuvK'

    }

    /// <summary>
    /// ICC Profile Class
    /// </summary>
    public enum cmsProfileClassSignature
    {
        cmsSigInputClass = 0x73636E72,  // 'scnr'
        cmsSigDisplayClass = 0x6D6E7472,  // 'mntr'
        cmsSigOutputClass = 0x70727472,  // 'prtr'
        cmsSigLinkClass = 0x6C696E6B,  // 'link'
        cmsSigAbstractClass = 0x61627374,  // 'abst'
        cmsSigColorSpaceClass = 0x73706163,  // 'spac'
        cmsSigNamedColorClass = 0x6e6d636c   // 'nmcl'

    }
    struct cmsCIEXYZ
    {
        public double X;
        public double Y;
        public double Z;

        public cmsCIExyY ToCIExyY()
        {
            return new cmsCIExyY { x = X / (X + Y + Z), y = Y / (X + Y + Z), Y = Y };
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void _MakeRoslynHappy()
        {
            X = 0;
            Y = 0;
            Z = 0;
        }
    }

    struct cmsCIExyY
    {
        public double x;
        public double y;
        public double Y;
    }
    public enum cmsInfoType
    {
        cmsInfoDescription = 0,
        cmsInfoManufacturer = 1,
        cmsInfoModel = 2,
        cmsInfoCopyright = 3
    }

    struct cmsCIExyYTRIPLE
    {
        public cmsCIExyY Red;
        public cmsCIExyY Green;
        public cmsCIExyY Blue;
    }
    internal class CmsNative
    {
        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern IntPtr cmsOpenProfileFromMem(IntPtr MemPtr, uint size);

        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern IntPtr cmsReadTag(IntPtr hProfile, cmsTagSignature sig);

        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int cmsGetToneCurveParametricType(IntPtr cmsToneCurve);

        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern unsafe double* cmsGetToneCurveParams(IntPtr cmsToneCurve);

        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern IntPtr cmsReverseToneCurve(IntPtr inGamma);

        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern ushort cmsEvalToneCurve16(IntPtr cmsToneCurve, ushort v);
        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern float cmsEvalToneCurveFloat(IntPtr cmsToneCurve, float v);
        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void cmsFreeToneCurve(IntPtr cmsToneCurve);
        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int cmsGetToneCurveEstimatedTableEntries(IntPtr cmsToneCurve);
        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern unsafe ushort* cmsGetToneCurveEstimatedTable(IntPtr cmsToneCurve);

        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int cmsCloseProfile(IntPtr hProfile);
        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern cmsColorSpaceSignature cmsGetPCS(IntPtr hProfile);
        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern cmsColorSpaceSignature cmsGetColorSpace(IntPtr hProfile);
        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int cmsAdaptToIlluminant(out cmsCIEXYZ result, in cmsCIEXYZ SourceWhitePt, in cmsCIEXYZ Illuminant, in cmsCIEXYZ Value);
        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern unsafe int cmsAdaptToIlluminant(out cmsCIEXYZ result, cmsCIEXYZ* SourceWhitePt, cmsCIEXYZ* Illuminant, cmsCIEXYZ* Value);
        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern unsafe cmsCIEXYZ* cmsD50_XYZ();
        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern unsafe int cmsWriteRawTag(IntPtr hProfile, cmsTagSignature sig, void* data, uint size);
        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern unsafe int cmsWriteTag(IntPtr hProfile, cmsTagSignature sig, void* data);
        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int cmsWriteTag(IntPtr hProfile, cmsTagSignature sig, IntPtr data);

        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern unsafe int cmsSaveProfileToMem(IntPtr hProfile, void* ptr, ref uint size);
        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern unsafe IntPtr cmsMLUalloc(IntPtr ContextID, uint nItems);
        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern unsafe void cmsMLUfree(IntPtr mlu);


        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, CharSet = CharSet.Unicode)]
        public static extern unsafe uint cmsMLUgetWide(IntPtr mlu, [MarshalAs(UnmanagedType.LPStr)] string LanguageCode, [MarshalAs(UnmanagedType.LPStr)] string CountryCode, char* buffer, uint bufferSize);
        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true, CharSet = CharSet.Unicode)]
        public static extern unsafe int cmsMLUsetWide(IntPtr mlu, [MarshalAs(UnmanagedType.LPStr)] string LanguageCode, [MarshalAs(UnmanagedType.LPStr)] string CountryCode, [MarshalAs(UnmanagedType.LPWStr)] string WideString);



        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern unsafe uint cmsGetProfileInfo(IntPtr hProfile, cmsInfoType Info, [MarshalAs(UnmanagedType.LPStr)] string LanguageCode, [MarshalAs(UnmanagedType.LPStr)] string CountryCode, char* buffer, uint bufferSize);

        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern unsafe uint cmsGetHeaderManufacturer(IntPtr hProfile);
        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern unsafe void cmsSetHeaderManufacturer(IntPtr hProfile, uint manufacturer);
        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern unsafe uint cmsGetHeaderModel(IntPtr hProfile);
        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern unsafe void cmsSetHeaderModel(IntPtr hProfile, uint manufacturer);

        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern unsafe IntPtr cmsCreateRGBProfile(in cmsCIExyY WhitePoint, in cmsCIExyYTRIPLE Primaries, IntPtr[] TransferFunction);
        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern unsafe void cmsMD5computeID(IntPtr hProfile);
        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern unsafe void cmsGetHeaderAttributes(IntPtr hProfile, out ulong flags);
        [DllImport("lcms2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern unsafe void cmsSetHeaderAttributes(IntPtr hProfile, ulong flags);
    }
}
