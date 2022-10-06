using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using LittleCms;
using System.Buffers.Binary;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using System.Runtime.InteropServices;

namespace MHC2Gen
{



    class MHC2Tag
    {
        public const TagSignature Signature = (TagSignature)0x4D484332;

        public double MinCLL { get; set; }
        public double MaxCLL { get; set; }
        public double[,]? Matrix3x4 { get; set; }
        public double[,]? RegammaLUT { get; set; }

        private static int EncodeS15F16(double value)
        {
            var x = (int)Math.Round(value * 65536);
            return x;
        }

        public byte[] ToBytes()
        {
            if (RegammaLUT!.GetLength(0) != 3) throw new ArrayTypeMismatchException();
            var lut_size = RegammaLUT.GetLength(1);
            if (lut_size <= 1 || lut_size > 4096) throw new IndexOutOfRangeException();
            if (Matrix3x4!.Length != 12 || Matrix3x4.GetLength(0) != 3) throw new ArrayTypeMismatchException();
            var ms0 = new MemoryStream();
            var writer = new BinaryWriter(ms0);
            // type
            writer.Write(BinaryPrimitives.ReverseEndianness((int)Signature));
            writer.Write(BinaryPrimitives.ReverseEndianness(0));

            writer.Write(BinaryPrimitives.ReverseEndianness(lut_size));
            writer.Write(BinaryPrimitives.ReverseEndianness(EncodeS15F16(MinCLL)));
            writer.Write(BinaryPrimitives.ReverseEndianness(EncodeS15F16(MaxCLL)));
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

            foreach (var e in Matrix3x4)
            {
                writer.Write(BinaryPrimitives.ReverseEndianness(EncodeS15F16(e)));
            }

            for (int ch = 0; ch < 3; ch++)
            {
                writer.Write(new ReadOnlySpan<byte>(new byte[] { (byte)'s', (byte)'f', (byte)'3', (byte)'2', 0, 0, 0, 0 }));
                for (int i = 0; i < lut_size; i++)
                {
                    writer.Write(BinaryPrimitives.ReverseEndianness(EncodeS15F16(RegammaLUT[ch, i])));
                }
            }
            writer.Flush();
            return ms0.ToArray();
        }


    }

    internal class IccContext
    {
        protected IccProfile profile;
        public CIEXYZ IlluminantRelativeWhitePoint { get; }
        public Matrix<double>? ChromaticAdaptionMatrix { get; }
        public Matrix<double>? InverseChromaticAdaptionMatrix { get; }
        public RgbPrimaries ProfilePrimaries { get; }

        public IccContext(IccProfile profile)
        {
            if (profile.PCS != ColorSpaceSignature.XYZ || profile.ColorSpace != ColorSpaceSignature.Rgb)
            {
                throw new CmsException(CmsError.COLORSPACE_CHECK, "ICC profile is not XYZ->RGB");
            }
            this.profile = profile;
            var chad = profile.ReadTag<double[,]>(TagSignature.ChromaticAdaptation);
            if (chad != null)
            {
                ChromaticAdaptionMatrix = DenseMatrix.OfArray(chad);
                InverseChromaticAdaptionMatrix = ChromaticAdaptionMatrix.Inverse();
            }
            IlluminantRelativeWhitePoint = GetIlluminantReletiveWhitePoint();
            ProfilePrimaries = GetPrimaries();
        }


        private unsafe CIEXYZ GetIlluminantReletiveWhitePoint()
        {
            var icc_wtpt = profile.ReadTag<CIEXYZ?>(TagSignature.MediaWhitePoint);

            if (icc_wtpt.HasValue)
            {
                if (ChromaticAdaptionMatrix == null || profile.HeaderCreator == 0x6170706c /* 'aapl' */)
                {
                    // for profiels without 'chad' tag and Apple profiles, mediaWhitepointTag is illuminant-relative
                    return icc_wtpt.Value;
                }
                else
                {
                    // ... otherwise it is PCS-relative
                    var pcs_wtpt = icc_wtpt.Value;
                    if (ChromaticAdaptionMatrix != null)
                    {
                        return ApplyInverseChad(pcs_wtpt);
                    }
                }
            }
            if (ChromaticAdaptionMatrix != null)
            {
                // no wtpt in icc, sum RGB and reverse chad
                var pcs_rXYZ = profile.ReadTag<CIEXYZ>(TagSignature.RedColorant);
                var pcs_gXYZ = profile.ReadTag<CIEXYZ>(TagSignature.GreenColorant);
                var pcs_bXYZ = profile.ReadTag<CIEXYZ>(TagSignature.BlueColorant);
                var pcs_sumrgb = pcs_rXYZ + pcs_gXYZ + pcs_bXYZ;

                return ApplyInverseChad(pcs_sumrgb);
            }
            else
            {
                throw new Exception("malformed profile: missing wtpt and chad");
            }
        }

        protected CIEXYZ ApplyInverseChad(in CIEXYZ val)
        {
            var vec = InverseChromaticAdaptionMatrix!.Multiply(new DenseVector(new[] { val.X, val.Y, val.Z }));
            return new() { X = vec[0], Y = vec[1], Z = vec[2] };
        }

        /// <summary>
        /// get illuminant-relative primaries from profile
        /// </summary>
        /// <param name="profile"></param>
        /// <returns></returns>
        public unsafe RgbPrimaries GetPrimaries()
        {
            var ir_wtpt = IlluminantRelativeWhitePoint;

            CIExy ir_rxy, ir_gxy, ir_bxy;

            var chrm = (CIExyY*)profile.ReadTag(TagSignature.Chromaticity);
            if (chrm != null)
            {
                ir_rxy = chrm[0].ToXY();
                ir_gxy = chrm[1].ToXY();
                ir_bxy = chrm[2].ToXY();
            }
            else
            {
                var pcs_rXYZ = profile.ReadTag<CIEXYZ>(TagSignature.RedColorant);
                var pcs_gXYZ = profile.ReadTag<CIEXYZ>(TagSignature.GreenColorant);
                var pcs_bXYZ = profile.ReadTag<CIEXYZ>(TagSignature.BlueColorant);


                CIEXYZ ir_rXYZ, ir_gXYZ, ir_bXYZ;
                if (ChromaticAdaptionMatrix != null)
                {
                    ir_rXYZ = ApplyInverseChad(pcs_rXYZ);
                    ir_gXYZ = ApplyInverseChad(pcs_gXYZ);
                    ir_bXYZ = ApplyInverseChad(pcs_bXYZ);
                }
                else
                {
                    ir_rXYZ = CmsGlobal.AdaptToIlluminant(CmsGlobal.D50XYZ, ir_wtpt, pcs_rXYZ);
                    ir_gXYZ = CmsGlobal.AdaptToIlluminant(CmsGlobal.D50XYZ, ir_wtpt, pcs_gXYZ);
                    ir_bXYZ = CmsGlobal.AdaptToIlluminant(CmsGlobal.D50XYZ, ir_wtpt, pcs_bXYZ);
                }

                ir_rxy = ir_rXYZ.ToXY();
                ir_gxy = ir_gXYZ.ToXY();
                ir_bxy = ir_bXYZ.ToXY();
            }

            return new(ir_rxy, ir_gxy, ir_bxy, ir_wtpt.ToXY());
        }

        /// <summary>
        /// use lcms transform to get primaries in D50 XYZ, then adapt to profile illuminant.
        /// </summary>
        /// <remarks>
        /// suffers from precision issues
        /// </remarks>
        public unsafe RgbPrimaries GetPrimaries2()
        {
            var ir_wtpt = IlluminantRelativeWhitePoint;

            var xyzprof = IccProfile.CreateXYZ();
            var t = new CmsTransform(profile, CmsPixelFormat.RGBDouble, xyzprof, CmsPixelFormat.XYZDouble, RenderingIntent.ABSOLUTE_COLORIMETRIC, default);
            var pixels = new ReadOnlySpan<double>(new double[] {
                1, 0, 0,
                0, 1, 0,
                0, 0, 1,
                1, 1, 1
            });
            Span<double> xyz = stackalloc double[3];


            t.DoTransform(MemoryMarshal.Cast<double, byte>(pixels.Slice(0)), MemoryMarshal.Cast<double, byte>(xyz), 1);
            var d50_rXYZ = new CIEXYZ { X = xyz[0], Y = xyz[1], Z = xyz[2] };
            t.DoTransform(MemoryMarshal.Cast<double, byte>(pixels.Slice(3)), MemoryMarshal.Cast<double, byte>(xyz), 1);
            var d50_gXYZ = new CIEXYZ { X = xyz[0], Y = xyz[1], Z = xyz[2] };
            t.DoTransform(MemoryMarshal.Cast<double, byte>(pixels.Slice(6)), MemoryMarshal.Cast<double, byte>(xyz), 1);
            var d50_bXYZ = new CIEXYZ { X = xyz[0], Y = xyz[1], Z = xyz[2] };
            t.DoTransform(MemoryMarshal.Cast<double, byte>(pixels.Slice(9)), MemoryMarshal.Cast<double, byte>(xyz), 1);
            var d50_wXYZ = new CIEXYZ { X = xyz[0], Y = xyz[1], Z = xyz[2] };


            var ir_rXYZ = CmsGlobal.AdaptToIlluminant(CmsGlobal.D50XYZ, ir_wtpt, d50_rXYZ);
            var ir_gXYZ = CmsGlobal.AdaptToIlluminant(CmsGlobal.D50XYZ, ir_wtpt, d50_gXYZ);
            var ir_bXYZ = CmsGlobal.AdaptToIlluminant(CmsGlobal.D50XYZ, ir_wtpt, d50_bXYZ);
            return new(ir_rXYZ.ToXY(), ir_gXYZ.ToXY(), ir_bXYZ.ToXY(), ir_wtpt.ToXY());

        }

        public string GetDescription()
        {
            return profile.GetInfo(InfoType.Description);
        }

        public CIEXYZ GetIlluminantRelativeBlackPoint()
        {
            // NOTE: mediaBlackPointTag is no longer in ICC standard
            var bkpt = profile.ReadTag<CIEXYZ?>(TagSignature.MediaBlackPoint);

            if (bkpt.HasValue)
            {
                // no chad in profile, bkpt is illuminant-relative
                if (ChromaticAdaptionMatrix == null)
                {
                    return bkpt.Value;
                }
                else
                {
                    return ApplyInverseChad(bkpt.Value);
                }
            }

            // no bkpt in tag, use lcms transform
            var wtpt = IlluminantRelativeWhitePoint;
            var t = new CmsTransform(profile, CmsPixelFormat.RGB8, IccProfile.CreateXYZ(), CmsPixelFormat.XYZDouble, RenderingIntent.ABSOLUTE_COLORIMETRIC, default);
            var input = new ReadOnlySpan<byte>(new byte[] { 0, 0, 0 });
            Span<double> outbuf = stackalloc double[3];
            t.DoTransform(input, MemoryMarshal.Cast<double, byte>(outbuf), 1);
            var d50_bkpt = new CIEXYZ { X = outbuf[0], Y = outbuf[1], Z = outbuf[2] };
            return CmsGlobal.AdaptToIlluminant(CmsGlobal.D50XYZ, wtpt, d50_bkpt);
        }

        public void WriteIlluminantRelativeMediaBlackPoint(in CIEXYZ value)
        {
            CIEXYZ valueToWrite;
            if (ChromaticAdaptionMatrix != null)
            {
                var vec = new DenseVector(new double[] { value.X, value.Y, value.Z });
                var pcs_vec = ChromaticAdaptionMatrix * vec;
                valueToWrite = new() { X = pcs_vec[0], Y = pcs_vec[1], Z = pcs_vec[2] };
            }
            else
            {
                valueToWrite = value;
            }
            profile.WriteTag(TagSignature.MediaBlackPoint, valueToWrite);
        }
    }

    internal class DeviceIccContext : IccContext
    {
        CIEXYZ illuminantRelativeBlackPoint;
        double min_nits;
        double max_nits;
        ToneCurve profileRedToneCurve;
        ToneCurve profileGreenToneCurve;
        ToneCurve profileBlueToneCurve;
        ToneCurve profileRedReverseToneCurve;
        ToneCurve profileGreenReverseToneCurve;
        ToneCurve profileBlueReverseToneCurve;

        public DeviceIccContext(IccProfile profile) : base(profile)
        {
            illuminantRelativeBlackPoint = GetIlluminantRelativeBlackPoint();
            (max_nits, min_nits) = GetProfileLuminance();
            profileRedToneCurve = profile.ReadTag<ToneCurve>(TagSignature.RedTRC)!;
            profileGreenToneCurve = profile.ReadTag<ToneCurve>(TagSignature.GreenTRC)!;
            profileBlueToneCurve = profile.ReadTag<ToneCurve>(TagSignature.BlueTRC)!;
            profileRedReverseToneCurve = profileRedToneCurve.Reverse();
            profileGreenReverseToneCurve = profileGreenToneCurve.Reverse();
            profileBlueReverseToneCurve = profileBlueToneCurve.Reverse();
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

        public string GetDeviceDescription()
        {
            var model = profile.GetInfo(InfoType.Model);
            if (!string.IsNullOrEmpty(model)) return model;
            var desc = GetDescription();
            if (!string.IsNullOrEmpty(desc)) return desc;
            return "<Unknown device>";
        }

        private (double MaxNits, double MinNits) GetProfileLuminance()
        {
            var wtpt = IlluminantRelativeWhitePoint;
            double max_nits = 80;
            var lumi = profile.ReadTag<CIEXYZ?>(TagSignature.Luminance);
            if (lumi.HasValue)
            {
                max_nits = lumi.Value.Y;
            }
            var min_nits = 0.005;
            var bkpt = illuminantRelativeBlackPoint;
            if (bkpt.Y != 0)
            {
                var bkpt_scale = bkpt.Y / wtpt.Y;
                min_nits = max_nits * bkpt_scale;
            }
            return (max_nits, min_nits);
        }

        public IccProfile CreateMhc2CscIcc(RgbPrimaries? sourcePrimaries = null, string sourceDescription = "sRGB")
        {
            var wtpt = IlluminantRelativeWhitePoint;
            var vcgt = profile.ReadTag<ToneCurveTriple?>(TagSignature.Vcgt)?.ToArray();

            var devicePrimaries = ProfilePrimaries;

            var deviceOetf = new ToneCurve[] { profileRedReverseToneCurve, profileGreenReverseToneCurve, profileBlueReverseToneCurve };

            var srgbTrc = IccProfile.Create_sRGB().ReadTag<ToneCurve>(TagSignature.RedTRC)!;
            var sourceEotf = new ToneCurve[] { srgbTrc, srgbTrc, srgbTrc };

            sourcePrimaries ??= RgbPrimaries.sRGB;

            var srgb_to_xyz = RgbToXYZ(RgbPrimaries.sRGB);
            var xyz_to_srgb = XYZToRgb(RgbPrimaries.sRGB);
            var source_rgb_to_device_rgb = XYZToRgb(devicePrimaries) * RgbToXYZ(sourcePrimaries);

            // max luminance after CSC
            var source_white_to_device_rgb = source_rgb_to_device_rgb * new DenseVector(new double[] { 1, 1, 1 });
            var source_white_to_device_xyz = RgbToXYZ(devicePrimaries) * source_white_to_device_rgb;
            var mapped_y = source_white_to_device_xyz[1];
            var profile_max_nits = max_nits * (mapped_y / wtpt.Y);
            Console.WriteLine($"mapped max luminance: {profile_max_nits} cd/m2");

            // MHC2 matrix pipeline:
            // transformed_linear_rgb = xyz_to_srgb * user_matrix * srgb_to_xyz * linear_rgb
            // to eliminate unnecessary sRGB/XYZ transform, use
            // user_matrix = srgb_to_xyz * real_user_rgb_to_rgb_matrix * xyz_to_srgb

            var user_matrix = srgb_to_xyz * source_rgb_to_device_rgb * xyz_to_srgb;


            if (ReferenceEquals(sourcePrimaries, RgbPrimaries.sRGB))
            {
                // eliminate redundant sRGB/XYZ transform for better precision
                // user_matrix = srgb_to_xyz * real_user_rgb_to_rgb_matrix * xyz_to_srgb
                // real_user_rgb_to_rgb_matrix = xyz_to_device_rgb * srgb_to_xyz
                // user_matrix = srgb_to_xyz * xyz_to_device_rgb * (srgb_to_xyz * xyz_to_srgb)
                //             = srgb_to_xyz * xyz_to_device_rgb * I
                //             = srgb_to_xyz * xyz_to_device_rgb
                user_matrix = srgb_to_xyz * XYZToRgb(devicePrimaries);
            }

            var mhc2_matrix = new double[,] {
               { user_matrix[0,0], user_matrix[0,1], user_matrix[0,2], 0 },
               { user_matrix[1,0], user_matrix[1,1], user_matrix[1,2], 0 },
               { user_matrix[2,0], user_matrix[2,1], user_matrix[2,2], 0 },
            };

            double[,] mhc2_lut;
            if (vcgt != null)
            {
                var lut_size = 1024;
                mhc2_lut = new double[3, lut_size];
                for (int ch = 0; ch < 3; ch++)
                {
                    for (int iinput = 0; iinput < lut_size; iinput++)
                    {
                        var input = (float)iinput / (lut_size - 1);
                        var linear = sourceEotf[ch].EvalF32(input);
                        var dev_output = deviceOetf[ch].EvalF32(linear);
                        if (vcgt != null)
                        {
                            dev_output = vcgt[ch].EvalF32(dev_output);
                        }
                        mhc2_lut[ch, iinput] = dev_output;
                    }
                }
            }
            else
            {
                mhc2_lut = new double[,]
                {
                    { 0, 1 },
                    { 0, 1 },
                    { 0, 1 },
                };
            }

            var mhc2d = new MHC2Tag
            {
                MinCLL = min_nits,
                MaxCLL = profile_max_nits,
                Matrix3x4 = mhc2_matrix,
                RegammaLUT = mhc2_lut
            };

            var mhc2 = mhc2d.ToBytes();

            var outputProfile = IccProfile.CreateRGB(sourcePrimaries.White.ToXYZ().ToCIExyY(), new CIExyYTRIPLE
            {
                Red = sourcePrimaries.Red.ToXYZ().ToCIExyY(),
                Green = sourcePrimaries.Green.ToXYZ().ToCIExyY(),
                Blue = sourcePrimaries.Blue.ToXYZ().ToCIExyY()
            }, new ToneCurveTriple(srgbTrc, srgbTrc, srgbTrc));

            outputProfile.WriteTag(TagSignature.Luminance, new CIEXYZ { Y = profile_max_nits });

            var outctx = new IccContext(outputProfile);
            outctx.WriteIlluminantRelativeMediaBlackPoint(illuminantRelativeBlackPoint);

            // copy device description from device profile
            var copy_tags = new TagSignature[] { TagSignature.DeviceMfgDesc, TagSignature.DeviceModelDesc };

            foreach (var tag in copy_tags)
            {
                var tag_ptr = profile.ReadTag(tag);
                if (tag_ptr != IntPtr.Zero)
                {
                    outputProfile.WriteTag(tag, tag_ptr);
                }
            }

            // set output profile description
            outputProfile.HeaderManufacturer = profile.HeaderManufacturer;
            outputProfile.HeaderModel = profile.HeaderModel;
            outputProfile.HeaderAttributes = profile.HeaderAttributes;
            outputProfile.HeaderRenderingIntent = RenderingIntent.ABSOLUTE_COLORIMETRIC;

            var new_desc = $"CSC: {sourceDescription} ({GetDeviceDescription()})";
            Console.WriteLine("Output profile description: " + new_desc);
            var new_desc_mlu = new MLU(new_desc);
            outputProfile.WriteTag(TagSignature.ProfileDescription, new_desc_mlu);

            outputProfile.WriteRawTag(MHC2Tag.Signature, mhc2);

            outputProfile.ComputeProfileId();

            return outputProfile;
        }

        public IccProfile CreatePQ10DecodeIcc()
        {
            var sourcePrimaries = RgbPrimaries.Rec2020;
            var devicePrimaries = ProfilePrimaries;

            // var rgb_transform = RgbToRgb(sourcePrimaries, devicePrimaries);
            // rgb_transform = XYZToRgb(devicePrimaries) * RgbToXYZ(sourcePrimaries);
            // var xyz_transform = RgbToXYZ(sourcePrimaries) * rgb_transform * XYZToRgb(sourcePrimaries);
            var xyz_transform = RgbToXYZ(sourcePrimaries) * XYZToRgb(devicePrimaries);

            var mhc2_matrix = new double[,] {
               { xyz_transform[0,0], xyz_transform[0,1], xyz_transform[0,2], 0 },
               { xyz_transform[1,0], xyz_transform[1,1], xyz_transform[1,2], 0 },
               { xyz_transform[2,0], xyz_transform[2,1], xyz_transform[2,2], 0 },
            };

            var vcgt = profile.ReadTag<ToneCurveTriple?>(TagSignature.Vcgt)?.ToArray();
            var deviceOetf = new ToneCurve[] { profileRedReverseToneCurve, profileGreenReverseToneCurve, profileBlueReverseToneCurve };

            var lut_size = 4096;
            var mhc2_lut = new double[3, 4096];
            for (int ch = 0; ch < 3; ch++)
            {
                for (int iinput = 0; iinput < lut_size; iinput++)
                {
                    var pqinput = (double)iinput / (lut_size - 1);
                    var nits = ST2084.SignalToNits(pqinput);
                    var linear = Math.Max(nits - min_nits, 0) / (max_nits - min_nits);
                    var dev_output = deviceOetf[ch].EvalF32((float)linear);
                    if (vcgt != null)
                    {
                        dev_output = vcgt[ch].EvalF32(dev_output);
                    }
                    // Console.WriteLine($"Channel {ch}: PQ {iinput} -> {nits} cd/m2 -> SDR {dev_output * 255}");
                    mhc2_lut[ch, iinput] = dev_output;
                }
            }

            var mhc2d = new MHC2Tag
            {
                MinCLL = min_nits,
                MaxCLL = max_nits,
                Matrix3x4 = mhc2_matrix,
                RegammaLUT = mhc2_lut
            };

            var mhc2 = mhc2d.ToBytes();

            var outputProfile = IccProfile.CreateRGB(devicePrimaries.White.ToXYZ().ToCIExyY(), new CIExyYTRIPLE
            {
                Red = devicePrimaries.Red.ToXYZ().ToCIExyY(),
                Green = devicePrimaries.Green.ToXYZ().ToCIExyY(),
                Blue = devicePrimaries.Blue.ToXYZ().ToCIExyY()
            }, new ToneCurveTriple(profileRedToneCurve, profileGreenToneCurve, profileBlueToneCurve));

            // copy characteristics from device profile
            var copy_tags = new TagSignature[] { TagSignature.Luminance, TagSignature.DeviceMfgDesc, TagSignature.DeviceModelDesc };
            foreach (var tag in copy_tags)
            {
                var tag_ptr = profile.ReadTag(tag);
                if (tag_ptr != IntPtr.Zero)
                {
                    outputProfile.WriteTag(tag, tag_ptr);
                }
            }

            var outctx = new IccContext(outputProfile);
            outctx.WriteIlluminantRelativeMediaBlackPoint(illuminantRelativeBlackPoint);

            // set output profile description
            outputProfile.HeaderManufacturer = profile.HeaderManufacturer;
            outputProfile.HeaderModel = profile.HeaderModel;
            outputProfile.HeaderAttributes = profile.HeaderAttributes;
            outputProfile.HeaderRenderingIntent = RenderingIntent.ABSOLUTE_COLORIMETRIC;

            var new_desc = $"CSC: HDR10 to SDR ({GetDeviceDescription()})";
            Console.WriteLine("Output profile description: " + new_desc);
            var new_desc_mlu = new MLU(new_desc);
            outputProfile.WriteTag(TagSignature.ProfileDescription, new_desc_mlu);

            outputProfile.WriteRawTag(MHC2Tag.Signature, mhc2);

            outputProfile.ComputeProfileId();

            return outputProfile;
        }

        public IccProfile CreateSdrAcmIcc(bool calibrateTransfer)
        {
            var mhc2_matrix = new double[,] {
               { 1, 0, 0, 0 },
               { 0, 1, 0, 0 },
               { 0, 0, 1, 0 },
            };

            double[,] mhc2_lut;

            ToneCurveTriple outproftrc;

            if (calibrateTransfer)
            {
                var vcgt = profile.ReadTag<ToneCurveTriple?>(TagSignature.Vcgt)?.ToArray();
                var sourceEotf = IccProfile.Create_sRGB().ReadTag<ToneCurve>(TagSignature.RedTRC)!;
                outproftrc = new ToneCurveTriple(sourceEotf, sourceEotf, sourceEotf);
                var deviceOetf = new ToneCurve[] { profileRedReverseToneCurve, profileGreenReverseToneCurve, profileBlueReverseToneCurve };
                var lut_size = 1024;
                mhc2_lut = new double[3, lut_size];
                for (int ch = 0; ch < 3; ch++)
                {
                    for (int iinput = 0; iinput < lut_size; iinput++)
                    {
                        var input = (float)iinput / (lut_size - 1);
                        var linear = sourceEotf.EvalF32(input);
                        var dev_output = deviceOetf[ch].EvalF32(linear);
                        if (vcgt != null)
                        {
                            dev_output = vcgt[ch].EvalF32(dev_output);
                        }
                        mhc2_lut[ch, iinput] = dev_output;
                    }
                }
            }
            else
            {
                outproftrc = new ToneCurveTriple(profileRedToneCurve, profileGreenToneCurve, profileBlueToneCurve);
                mhc2_lut = new double[,]{ { 0, 1 }, { 0, 1 }, { 0, 1 } };
            }


            var mhc2d = new MHC2Tag
            {
                MinCLL = min_nits,
                MaxCLL = max_nits,
                Matrix3x4 = mhc2_matrix,
                RegammaLUT = mhc2_lut
            };

            var mhc2 = mhc2d.ToBytes();

            var devicePrimaries = ProfilePrimaries;
            var outputProfile = IccProfile.CreateRGB(devicePrimaries.White.ToXYZ().ToCIExyY(), new CIExyYTRIPLE
            {
                Red = devicePrimaries.Red.ToXYZ().ToCIExyY(),
                Green = devicePrimaries.Green.ToXYZ().ToCIExyY(),
                Blue = devicePrimaries.Blue.ToXYZ().ToCIExyY()
            }, outproftrc);

            // copy characteristics from device profile
            var copy_tags = new TagSignature[] {
                TagSignature.Luminance,
                TagSignature.DeviceMfgDesc,
                TagSignature.DeviceModelDesc,
            };

            foreach (var tag in copy_tags)
            {
                var tag_ptr = profile.ReadTag(tag);
                outputProfile.WriteTag(tag, tag_ptr);
            }

            // copy vcgt to output profile if it is not consumed
            if (!calibrateTransfer)
            {
                outputProfile.WriteTag(TagSignature.Vcgt, profile.ReadTag(TagSignature.Vcgt));
            }

            // the profile is not read by regular applications
            //var outctx = new IccContext(outputProfile);
            //outctx.WriteIlluminantRelativeMediaBlackPoint(illuminantRelativeBlackPoint);

            // SDR ACM will not work if the profile has negative XYZ colorants (possibly due to limited precision)
            foreach (var sig in new TagSignature[] { TagSignature.RedColorant, TagSignature.GreenColorant, TagSignature.BlueColorant })
            {
                var xyz = outputProfile.ReadTag<CIEXYZ>(sig);
                if (xyz.X < 0) xyz.X = 0;
                if (xyz.Y < 0) xyz.Y = 0;
                if (xyz.Z < 0) xyz.Z = 0;
            }

            // set output profile description

            outputProfile.HeaderManufacturer = profile.HeaderManufacturer;
            outputProfile.HeaderModel = profile.HeaderModel;
            outputProfile.HeaderAttributes = profile.HeaderAttributes;
            outputProfile.HeaderRenderingIntent = profile.HeaderRenderingIntent;

            outputProfile.ProfileVersion = profile.ProfileVersion;

            var new_desc = $"SDR ACM: {profile.GetInfo(InfoType.Description)}";
            Console.WriteLine("Output profile description: " + new_desc);
            var new_desc_mlu = new MLU(new_desc);
            outputProfile.WriteTag(TagSignature.ProfileDescription, new_desc_mlu);

            outputProfile.WriteRawTag(MHC2Tag.Signature, mhc2);

            outputProfile.ComputeProfileId();

            return outputProfile;
        }
    }
}
