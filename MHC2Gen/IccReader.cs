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

        private static unsafe void MustWriteTag(IntPtr profile_, cmsTagSignature tag, void* data)
        {
            var result = CmsNative.cmsWriteTag(profile_, tag, data);
            if (result == 0)
            {
                var gat = BinaryPrimitives.ReverseEndianness((uint)tag);
                var tagName = Encoding.ASCII.GetString((byte*)&gat, 4);
                throw new FileFormatException($"write tag {tagName} failed");
            }
        }

        public static int EncodeS15F16(double value)
        {
            var x = (int)Math.Round(value * 65536);
            return x;
        }

        public static IntPtr CheckPointer(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
            {
                throw new Exception("pointer is null");
            }
            return ptr;
        }

        public static void CheckCmsBool(int value)
        {
            if (value == 0)
            {
                throw new Exception("lcms faild");
            }
        }

        public static unsafe string? GetProfileInfo(IntPtr profile, cmsInfoType info)
        {
            var len = CmsNative.cmsGetProfileInfo(profile, cmsInfoType.cmsInfoModel, MLU.NoLanguage, MLU.NoCountry, null, 0);
            if (len != 0)
            {
                var buffer = stackalloc char[(int)len];
                CmsNative.cmsGetProfileInfo(profile, cmsInfoType.cmsInfoModel, MLU.NoLanguage, MLU.NoCountry, buffer, len);
                return new string(buffer);
            }
            return null;
        }

        public static string DescribeDeviceProfile(IntPtr profile)
        {
            var model = GetProfileInfo(profile, cmsInfoType.cmsInfoModel);
            if (model != null) return model;
            var desc = GetProfileInfo(profile, cmsInfoType.cmsInfoDescription);
            if (desc != null) return desc;
            return "<Unknown device>";
        }

        public static unsafe string DescribeSourceProfile(IntPtr profile)
        {
            var desc = CmsNative.cmsReadTag(profile, cmsTagSignature.cmsSigProfileDescriptionTag);
            if (desc != IntPtr.Zero)
            {
                var mlu = new MLU(desc);
                var str = mlu.GetUnlocalizedString();
                if (str != null) return str;
            }
            return "<unknown calibration target>";
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

        public static unsafe byte[] ProcessICC(ReadOnlySpan<byte> deviceIccProfile, ReadOnlySpan<byte> sourceIccProfile)
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
            var lumi = (cmsCIEXYZ*)MustReadTag(deviceProfile, cmsTagSignature.cmsSigLuminanceTag);
            var max_nits = lumi->Y;
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
            var sourceProfile = MustOpenProfileFromMem(sourceIccProfile);
            using var defer2 = new Defer(() => CmsNative.cmsCloseProfile(sourceProfile));

            var dev_rTRC = MustReadTag(deviceProfile, cmsTagSignature.cmsSigRedTRCTag);
            var dev_gTRC = MustReadTag(deviceProfile, cmsTagSignature.cmsSigGreenTRCTag);
            var dev_bTRC = MustReadTag(deviceProfile, cmsTagSignature.cmsSigBlueTRCTag);
            var deviceOetf = new IntPtr[] { MustReverseToneCurve(dev_rTRC), MustReverseToneCurve(dev_gTRC), MustReverseToneCurve(dev_bTRC) };
            using var defer4 = new Defer(() =>
            {
                foreach (var t in deviceOetf)
                    CmsNative.cmsFreeToneCurve(t);
            });
            var src_rTRC = MustReadTag(sourceProfile, cmsTagSignature.cmsSigRedTRCTag);
            var src_gTRC = MustReadTag(sourceProfile, cmsTagSignature.cmsSigGreenTRCTag);
            var src_bTRC = MustReadTag(sourceProfile, cmsTagSignature.cmsSigBlueTRCTag);
            var sourceEotf = new IntPtr[] { src_rTRC, src_gTRC, src_bTRC };
            var sourceVcgt = (IntPtr*)CmsNative.cmsReadTag(sourceProfile, cmsTagSignature.cmsSigVcgtTag);
            IntPtr[]? sourceRevVcgt = null;
            if (sourceVcgt != null)
            {
                sourceRevVcgt = new IntPtr[] { MustReverseToneCurve(sourceVcgt[0]), MustReverseToneCurve(sourceVcgt[1]), MustReverseToneCurve(sourceVcgt[2]) };
            }
            using var defer5 = new Defer(() =>
            {
                if (sourceRevVcgt != null)
                {
                    foreach (var t in sourceRevVcgt)
                        CmsNative.cmsFreeToneCurve(t);
                }
            });

            var sourcePrimaries = GetPrimariesFromProfile(sourceProfile);

            // var rgb_transform = RgbToRgb(sourcePrimaries, devicePrimaries);
            // rgb_transform = XYZToRgb(devicePrimaries) * RgbToXYZ(sourcePrimaries);
            // var xyz_transform = RgbToXYZ(sourcePrimaries) * rgb_transform * XYZToRgb(sourcePrimaries);
            var xyz_transform = RgbToXYZ(sourcePrimaries) * XYZToRgb(devicePrimaries);

            var mhc2_matrix = new double[] {
               xyz_transform[0,0], xyz_transform[0,1], xyz_transform[0,2], 0,
               xyz_transform[1,0], xyz_transform[1,1], xyz_transform[1,2], 0,
               xyz_transform[2,0], xyz_transform[2,1], xyz_transform[2,2], 0,
            };

            var lut_size = 256;
            var mhc2_lut = (int[][])Array.CreateInstance(typeof(int[]), 3);
            if (true || vcgt != null)
            {
                lut_size = 256;
                for (int ch = 0; ch < 3; ch++)
                {
                    mhc2_lut[ch] = new int[lut_size];
                    for (int iinput = 0; iinput < lut_size; iinput++)
                    {
                        var input = (float)iinput / (lut_size - 1);
                        var src_input = input;
                        if (sourceRevVcgt != null)
                        {
                            src_input = (int)CmsNative.cmsEvalToneCurveFloat(sourceRevVcgt[ch], input);
                        }
                        var linear = CmsNative.cmsEvalToneCurveFloat(sourceEotf[ch], input);
                        var dev_output = CmsNative.cmsEvalToneCurveFloat(deviceOetf[ch], linear);
                        if (vcgt != null)
                        {
                            dev_output = CmsNative.cmsEvalToneCurveFloat(vcgt[ch], dev_output);
                        }
                        mhc2_lut[ch][iinput] = EncodeS15F16(dev_output);
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

            var outputProfile = CmsNative.cmsCreateRGBProfile(sourcePrimaries.White.ToXYZ().ToCIExyY(), new cmsCIExyYTRIPLE
            {
                Red = sourcePrimaries.Red.ToXYZ().ToCIExyY(),
                Green = sourcePrimaries.Green.ToXYZ().ToCIExyY(),
                Blue = sourcePrimaries.Blue.ToXYZ().ToCIExyY()
            }, new IntPtr[] { src_rTRC, src_gTRC, src_bTRC });
            using var defer6 = new Defer(() => CmsNative.cmsCloseProfile(outputProfile));

            // copy characteristics from device profile
            var copy_tags = new cmsTagSignature[] { cmsTagSignature.cmsSigMediaBlackPointTag, cmsTagSignature.cmsSigLuminanceTag, cmsTagSignature.cmsSigDeviceMfgDescTag, cmsTagSignature.cmsSigDeviceModelDescTag };
            // var remove_tags = new cmsTagSignature[] { cmsTagSignature.cmsSigViewingConditionsTag, cmsTagSignature.cmsSigViewingCondDescTag, cmsTagSignature.cmsSigTechnologyTag, cmsTagSignature.cmsSigVcgtTag, cmsTagSignature.cmsSigMeasurementTag };

            foreach (var tag in copy_tags)
            {
                var tag_ptr = (void*)CmsNative.cmsReadTag(deviceProfile, tag);
                if (tag_ptr != null)
                {
                    MustWriteTag(outputProfile, tag, tag_ptr);
                }
            }
            // foreach (var tag in remove_tags)
            // {
            //     CmsNative.cmsWriteTag(sourceProfile, tag, null);
            // }

            // set output profile description

            CmsNative.cmsSetHeaderManufacturer(outputProfile, CmsNative.cmsGetHeaderManufacturer(deviceProfile));
            CmsNative.cmsSetHeaderModel(outputProfile, CmsNative.cmsGetHeaderModel(deviceProfile));
            CmsNative.cmsGetHeaderAttributes(deviceProfile, out var profileAttr);
            CmsNative.cmsSetHeaderAttributes(outputProfile, profileAttr);
            var new_desc = $"{DescribeDeviceProfile(deviceProfile)} calibrated to {DescribeSourceProfile(sourceProfile)} (MHC2)";
            Console.WriteLine("Output profile description: " + new_desc);
            using var new_desc_mlu = MLU.FromUnlocalizedString(new_desc);
            MustWriteTag(outputProfile, cmsTagSignature.cmsSigProfileDescriptionTag, (void*)new_desc_mlu.Handle);

            bool br;
            fixed (byte* ptr = mhc2)
                br = CmsNative.cmsWriteRawTag(outputProfile, mhc2_sig, ptr, (uint)mhc2.Length) != 0;
            if (!br)
            {
                throw new IOException("cmsWriteRawTag failed");
            }

            CmsNative.cmsMD5computeID(outputProfile);
            uint newlen = 0;
            br = CmsNative.cmsSaveProfileToMem(outputProfile, null, ref newlen) != 0;
            if (!br)
            {
                throw new IOException("cmsSaveProfileToMem failed");
            }
            var newicc = new byte[newlen];
            fixed (byte* ptr = newicc)
                br = CmsNative.cmsSaveProfileToMem(outputProfile, ptr, ref newlen) != 0;
            if (!br)
            {
                throw new IOException("cmsSaveProfileToMem failed");
            }
            return newicc;
        }

        public static unsafe byte[] CreatePQ10DecodeIcc(ReadOnlySpan<byte> deviceIccProfile)
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
            var lumi = (cmsCIEXYZ*)MustReadTag(deviceProfile, cmsTagSignature.cmsSigLuminanceTag);
            var max_nits = lumi->Y;
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

            var dev_rTRC = MustReadTag(deviceProfile, cmsTagSignature.cmsSigRedTRCTag);
            var dev_gTRC = MustReadTag(deviceProfile, cmsTagSignature.cmsSigGreenTRCTag);
            var dev_bTRC = MustReadTag(deviceProfile, cmsTagSignature.cmsSigBlueTRCTag);
            var deviceOetf = new IntPtr[] { MustReverseToneCurve(dev_rTRC), MustReverseToneCurve(dev_gTRC), MustReverseToneCurve(dev_bTRC) };
            using var defer4 = new Defer(() =>
            {
                foreach (var t in deviceOetf)
                    CmsNative.cmsFreeToneCurve(t);
            });


            var sourcePrimaries = RgbPrimaries.Rec2020;

            // var rgb_transform = RgbToRgb(sourcePrimaries, devicePrimaries);
            // rgb_transform = XYZToRgb(devicePrimaries) * RgbToXYZ(sourcePrimaries);
            // var xyz_transform = RgbToXYZ(sourcePrimaries) * rgb_transform * XYZToRgb(sourcePrimaries);
            var xyz_transform = RgbToXYZ(sourcePrimaries) * XYZToRgb(devicePrimaries);

            var mhc2_matrix = new double[] {
               xyz_transform[0,0], xyz_transform[0,1], xyz_transform[0,2], 0,
               xyz_transform[1,0], xyz_transform[1,1], xyz_transform[1,2], 0,
               xyz_transform[2,0], xyz_transform[2,1], xyz_transform[2,2], 0,
            };

            var lut_size = 1024;
            var mhc2_lut = (int[][])Array.CreateInstance(typeof(int[]), 3);
            for (int ch = 0; ch < 3; ch++)
            {
                mhc2_lut[ch] = new int[lut_size];
                for (int iinput = 0; iinput < lut_size; iinput++)
                {
                    var pqinput = (double)iinput / (lut_size - 1);
                    var nits = LittleCms.ST2084.SignalToNits(pqinput);
                    var linear = Math.Max(nits - min_nits, 0) / (max_nits - min_nits);
                    var dev_output = CmsNative.cmsEvalToneCurveFloat(deviceOetf[ch], (float)linear);
                    if (vcgt != null)
                    {
                        dev_output = CmsNative.cmsEvalToneCurveFloat(vcgt[ch], dev_output);
                    }
                    Console.WriteLine($"Channel {ch}: PQ {iinput} -> {nits} cd/m2 -> SDR {dev_output * 255}");
                    mhc2_lut[ch][iinput] = EncodeS15F16(dev_output);
                }
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

            var outputProfile = CmsNative.cmsCreateRGBProfile(devicePrimaries.White.ToXYZ().ToCIExyY(), new cmsCIExyYTRIPLE
            {
                Red = devicePrimaries.Red.ToXYZ().ToCIExyY(),
                Green = devicePrimaries.Green.ToXYZ().ToCIExyY(),
                Blue = devicePrimaries.Blue.ToXYZ().ToCIExyY()
            }, new IntPtr[] { dev_rTRC, dev_gTRC, dev_bTRC });
            using var defer6 = new Defer(() => CmsNative.cmsCloseProfile(outputProfile));

            // copy characteristics from device profile
            var copy_tags = new cmsTagSignature[] { cmsTagSignature.cmsSigMediaBlackPointTag, cmsTagSignature.cmsSigLuminanceTag, cmsTagSignature.cmsSigDeviceMfgDescTag, cmsTagSignature.cmsSigDeviceModelDescTag };
            // var remove_tags = new cmsTagSignature[] { cmsTagSignature.cmsSigViewingConditionsTag, cmsTagSignature.cmsSigViewingCondDescTag, cmsTagSignature.cmsSigTechnologyTag, cmsTagSignature.cmsSigVcgtTag, cmsTagSignature.cmsSigMeasurementTag };

            foreach (var tag in copy_tags)
            {
                var tag_ptr = (void*)CmsNative.cmsReadTag(deviceProfile, tag);
                if (tag_ptr != null)
                {
                    MustWriteTag(outputProfile, tag, tag_ptr);
                }
            }
            // foreach (var tag in remove_tags)
            // {
            //     CmsNative.cmsWriteTag(sourceProfile, tag, null);
            // }

            // set output profile description

            CmsNative.cmsSetHeaderManufacturer(outputProfile, CmsNative.cmsGetHeaderManufacturer(deviceProfile));
            CmsNative.cmsSetHeaderModel(outputProfile, CmsNative.cmsGetHeaderModel(deviceProfile));
            CmsNative.cmsGetHeaderAttributes(deviceProfile, out var profileAttr);
            CmsNative.cmsSetHeaderAttributes(outputProfile, profileAttr);
            var new_desc = $"{DescribeDeviceProfile(deviceProfile)} HDR10 (MHC2 PQ10 decode)";
            Console.WriteLine("Output profile description: " + new_desc);
            using var new_desc_mlu = MLU.FromUnlocalizedString(new_desc);
            MustWriteTag(outputProfile, cmsTagSignature.cmsSigProfileDescriptionTag, (void*)new_desc_mlu.Handle);

            bool br;
            fixed (byte* ptr = mhc2)
                br = CmsNative.cmsWriteRawTag(outputProfile, mhc2_sig, ptr, (uint)mhc2.Length) != 0;
            if (!br)
            {
                throw new IOException("cmsWriteRawTag failed");
            }

            CmsNative.cmsMD5computeID(outputProfile);
            uint newlen = 0;
            br = CmsNative.cmsSaveProfileToMem(outputProfile, null, ref newlen) != 0;
            if (!br)
            {
                throw new IOException("cmsSaveProfileToMem failed");
            }
            var newicc = new byte[newlen];
            fixed (byte* ptr = newicc)
                br = CmsNative.cmsSaveProfileToMem(outputProfile, ptr, ref newlen) != 0;
            if (!br)
            {
                throw new IOException("cmsSaveProfileToMem failed");
            }
            return newicc;
        }

    }
}
