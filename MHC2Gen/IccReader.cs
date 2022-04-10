using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using MHC2Gen.Util;
using LittleCms;
using System.Buffers.Binary;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace MHC2Gen
{
    internal class IccReader
    {
        private static IntPtr MustReverseToneCurve(IntPtr trc)
        {
            if (trc != IntPtr.Zero)
            {
                var revtrc = CmsNative.cmsReverseToneCurve(trc);
                if (revtrc == IntPtr.Zero)
                {
                    throw new FileFormatException("cmsReverseToneCurveEx failed");
                }
                return revtrc;
            }
            throw new ArgumentNullException(nameof(trc));
        }


        public static Matrix<double> RgbToXYZ(RgbPrimaries primaries)
        {
            var rXYZ = primaries.Red.ToXYZ();
            var gXYZ = primaries.Green.ToXYZ();
            var bXYZ = primaries.Blue.ToXYZ();
            var wXYZ = primaries.White.ToXYZ();

            var S = DenseMatrix.OfArray(new[,] {
                {rXYZ.X, gXYZ.X, bXYZ.X},
                {rXYZ.Y, gXYZ.Y, bXYZ.Y},
                {rXYZ.Z, gXYZ.Z, bXYZ.Z},
            }).Inverse().Multiply(DenseMatrix.OfArray(new[,] { { wXYZ.X }, { wXYZ.Y }, { wXYZ.Z } }));

            var M = DenseMatrix.OfArray(new[,] {
                {S[0,0] * rXYZ.X, S[1,0]*gXYZ.X, S[2,0]*bXYZ.X },
                {S[0,0] * rXYZ.Y, S[1,0]*gXYZ.Y, S[2,0]*bXYZ.Y },
                {S[0,0] * rXYZ.Z, S[1,0]*gXYZ.Z, S[2,0]*bXYZ.Z },
            });
            return M;
        }

        public static Matrix<double> XYZToRgb(RgbPrimaries primaries) => RgbToXYZ(primaries).Inverse();

        public static Matrix<double> RgbToRgb(RgbPrimaries from, RgbPrimaries to)
        {
            var M1 = RgbToXYZ(from);
            var M2 = XYZToRgb(to);
            return M2 * M1;
        }

        private static unsafe IntPtr MustReadTag(IntPtr profile_, cmsTagSignature tag)
        {
            var result = CmsNative.cmsReadTag(profile_, tag);
            if (result == IntPtr.Zero)
            {
                var gat = BinaryPrimitives.ReverseEndianness((uint)tag);
                var tagName = Encoding.ASCII.GetString((byte*)&gat, 4);
                throw new FileFormatException($"tag {tagName} not found");
            }
            return result;
        }

        public static int EncodeS15F16(double value)
        {
            var x = (int)Math.Round(value * 65536);
            return x;
        }


        public static unsafe IntPtr MustOpenProfileFromMem(ReadOnlySpan<byte> profile)
        {
            fixed (byte* ptr = profile)
            {
                var handle = CmsNative.cmsOpenProfileFromMem((IntPtr)ptr, (uint)profile.Length);
                if (handle == IntPtr.Zero)
                {
                    throw new FileFormatException("cmsOpenProfileFromMem failed");
                }
                return handle;
            }
        }

        public static unsafe RgbPrimaries GetPrimariesFromProfile(IntPtr profile)
        {
            var wtpt = (cmsCIEXYZ*)MustReadTag(profile, cmsTagSignature.cmsSigMediaWhitePointTag);

            var chrm = (cmsCIExyY*)CmsNative.cmsReadTag(profile, cmsTagSignature.cmsSigChromaticityTag);
            cmsCIExyY rxyY, gxyY, bxyY;
            if (chrm != null)
            {
                rxyY = chrm[0];
                gxyY = chrm[1];
                bxyY = chrm[2];
            }
            else
            {
                // [rgb]XYZ is PCS-relative (D50), adapt to white point
                var rXYZ_pcs = (cmsCIEXYZ*)MustReadTag(profile, cmsTagSignature.cmsSigRedColorantTag);
                var gXYZ_pcs = (cmsCIEXYZ*)MustReadTag(profile, cmsTagSignature.cmsSigGreenColorantTag);
                var bXYZ_pcs = (cmsCIEXYZ*)MustReadTag(profile, cmsTagSignature.cmsSigBlueColorantTag);

                var d50 = CmsNative.cmsD50_XYZ();

                var result = CmsNative.cmsAdaptToIlluminant(out var rXYZ, d50, wtpt, rXYZ_pcs) != 0;
                result &= CmsNative.cmsAdaptToIlluminant(out var gXYZ, d50, wtpt, gXYZ_pcs) != 0;
                result &= CmsNative.cmsAdaptToIlluminant(out var bXYZ, d50, wtpt, bXYZ_pcs) != 0;
                if (!result)
                {
                    throw new FileFormatException("cmsAdaptToIlluminant failed");
                }
                rxyY = rXYZ.ToCIExyY();
                gxyY = gXYZ.ToCIExyY();
                bxyY = bXYZ.ToCIExyY();
            }

            var wxyY = wtpt->ToCIExyY();
            return new(rxyY.ToXY(), gxyY.ToXY(), bxyY.ToXY(), wxyY.ToXY());
        }

        public static unsafe byte[] ProcessICC(ReadOnlySpan<byte> deviceIccProfile, ReadOnlySpan<byte> targetIccProfile)
        {
            var deviceProfile = MustOpenProfileFromMem(deviceIccProfile);
            using var defer1 = new Defer(() => CmsNative.cmsCloseProfile(deviceProfile));

            var pcs = CmsNative.cmsGetPCS(deviceProfile);
            var targetSpace = CmsNative.cmsGetColorSpace(deviceProfile);

            if (pcs != cmsColorSpaceSignature.cmsSigXYZData || targetSpace != cmsColorSpaceSignature.cmsSigRgbData)
            {
                throw new FileFormatException("ICC profile is not XYZ->RGB");
            }




            //var trcs = new IntPtr[] { MustReverseToneCurve(rTRC), MustReverseToneCurve(gTRC), MustReverseToneCurve(bTRC) };
            //using var defer3 = new Defer(() =>
            //{
            //    foreach (var t in trcs)
            //        CmsNative.cmsFreeToneCurve(t);
            //});
            var wtpt = (cmsCIEXYZ*)MustReadTag(deviceProfile, cmsTagSignature.cmsSigMediaWhitePointTag);

            var max_nits = ((cmsCIEXYZ*)MustReadTag(deviceProfile, cmsTagSignature.cmsSigLuminanceTag))->Y;
            var min_nits = 0.005;
            var bkpt = (cmsCIEXYZ*)CmsNative.cmsReadTag(deviceProfile, cmsTagSignature.cmsSigMediaBlackPointTag);
            if (bkpt != null && bkpt->Y != 0)
            {
                var bkpt_scale = bkpt->Y / wtpt->Y;
                min_nits = max_nits * bkpt_scale;
            }
            var vcgt = (IntPtr*)CmsNative.cmsReadTag(deviceProfile, cmsTagSignature.cmsSigVcgtTag);
            var luts = new ushort[][] { new ushort[256], new ushort[256], new ushort[256] };

            var devicePrimaries = GetPrimariesFromProfile(deviceProfile);
            var targetProfile = MustOpenProfileFromMem(targetIccProfile);
            using var defer2 = new Defer(() => CmsNative.cmsCloseProfile(targetProfile));

            var rTRC = MustReadTag(deviceProfile, cmsTagSignature.cmsSigRedTRCTag);
            var gTRC = MustReadTag(deviceProfile, cmsTagSignature.cmsSigGreenTRCTag);
            var bTRC = MustReadTag(deviceProfile, cmsTagSignature.cmsSigBlueTRCTag);
            CmsNative.cmsWriteTag(targetProfile, cmsTagSignature.cmsSigRedTRCTag, rTRC);
            CmsNative.cmsWriteTag(targetProfile, cmsTagSignature.cmsSigGreenTRCTag, gTRC);
            CmsNative.cmsWriteTag(targetProfile, cmsTagSignature.cmsSigBlueTRCTag, bTRC);


            var targetPrimaries = GetPrimariesFromProfile(targetProfile);

            var M = RgbToRgb(targetPrimaries, devicePrimaries);

            var mhc2_matrix = new double[] {
               M[0,0], M[0,1], M[0,2], 0,
               M[1,0], M[1,1], M[1,2], 0,
               M[2,0], M[2,1], M[2,2], 0,
            };

            var lut_size = 2;
            var mhc2_lut = (int[][])Array.CreateInstance(typeof(int[]), 3);
            if (vcgt != null)
            {
                lut_size = 256;
                for (int ch = 0; ch < 3; ch++)
                {
                    mhc2_lut[ch] = new int[lut_size];
                    for (int x = 0; x < lut_size; x++)
                    {
                        mhc2_lut[ch][x] = EncodeS15F16(CmsNative.cmsEvalToneCurveFloat(vcgt[ch], (float)x / lut_size));
                    }
                }
            }
            else
            {
                lut_size = 2;
                var linear_lut = new int[] { EncodeS15F16(0), EncodeS15F16(1) };
                mhc2_lut[0] = linear_lut;
                mhc2_lut[1] = linear_lut;
                mhc2_lut[2] = linear_lut;
            }
            const cmsTagSignature mhc2_sig = (cmsTagSignature)0x4D484332;

            var ms0 = new MemoryStream();
            var writer = new BinaryWriter(ms0);
            // type
            writer.Write(BinaryPrimitives.ReverseEndianness((int)mhc2_sig));
            writer.Write(BinaryPrimitives.ReverseEndianness(0));

            writer.Write(BinaryPrimitives.ReverseEndianness(lut_size));
            writer.Write(BinaryPrimitives.ReverseEndianness(EncodeS15F16(min_nits)));
            writer.Write(BinaryPrimitives.ReverseEndianness(EncodeS15F16(max_nits)));
            // matrix offset
            writer.Write(BinaryPrimitives.ReverseEndianness(36));
            // lut0 offset
            writer.Write(BinaryPrimitives.ReverseEndianness(84));
            // lut1 offset
            var lut1_offset = 84 + 8 + lut_size * 4;
            writer.Write(BinaryPrimitives.ReverseEndianness(lut1_offset));
            // lut2 offset
            var lut2_offset = lut1_offset + 8 + lut_size * 4;
            writer.Write(BinaryPrimitives.ReverseEndianness(lut2_offset));

            foreach (var e in mhc2_matrix)
            {
                writer.Write(BinaryPrimitives.ReverseEndianness(EncodeS15F16(e)));
            }

            for (int ch = 0; ch < 3; ch++)
            {
                writer.Write(new ReadOnlySpan<byte>(new byte[] { (byte)'s', (byte)'f', (byte)'3', (byte)'2', 0, 0, 0, 0 }));
                for (int i = 0; i < lut_size; i++)
                {
                    writer.Write(BinaryPrimitives.ReverseEndianness(mhc2_lut[ch][i]));
                }
            }
            writer.Flush();
            var mhc2 = ms0.ToArray();
            
            bool br;
            fixed (byte* ptr = mhc2)
                br = CmsNative.cmsWriteRawTag(targetProfile, mhc2_sig, ptr, (uint)mhc2.Length) != 0;
            if (!br)
            {
                throw new IOException("cmsWriteRawTag failed");
            }

            br = CmsNative.cmsWriteTag(targetProfile, cmsTagSignature.cmsSigVcgtTag, null) != 0;
            if (!br)
            {
               throw new IOException("cmsWriteRawTag failed");
            }

            uint newlen = 0;
            br = CmsNative.cmsSaveProfileToMem(targetProfile, null, ref newlen) != 0;
            if (!br)
            {
                throw new IOException("cmsSaveProfileToMem failed");
            }
            var newicc = new byte[newlen];
            fixed (byte* ptr = newicc)
                br = CmsNative.cmsSaveProfileToMem(targetProfile, ptr, ref newlen) != 0;
            if (!br)
            {
                throw new IOException("cmsSaveProfileToMem failed");
            }
            return newicc;
        }
    }
}
