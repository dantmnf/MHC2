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
using LittleCms.Data;

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

    public enum KeepWhitePoint
    {
        /// <summary>
        /// don't keep profile white point (absolute colorimetric)
        /// </summary>
        None,
        /// <summary>
        /// omit white point transform
        /// </summary>
        Simple,
        /// <summary>
        /// use Bradford chromatic adaptation to device white point
        /// </summary>
        Bradford
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
            if (profile.TryReadTag(SafeTagSignature.ChromaticAdaptationTag, out var chad))
            {
                ChromaticAdaptionMatrix = DenseMatrix.OfArray(chad);
                InverseChromaticAdaptionMatrix = ChromaticAdaptionMatrix.Inverse();
            }
            (IlluminantRelativeWhitePoint, ProfilePrimaries) = PopulatePrimaries();
        }


        private unsafe CIEXYZ GetIlluminantReletiveWhitePoint()
        {
            if (profile.TryReadTag(SafeTagSignature.MediaWhitePointTag, out var icc_wtpt))
            {
                if (ChromaticAdaptionMatrix == null || profile.HeaderCreator == 0x6170706c /* 'aapl' */)
                {
                    // for profiels without 'chad' tag and Apple profiles, mediaWhitepointTag is illuminant-relative
                    return icc_wtpt;
                }
                else
                {
                    // ... otherwise it is PCS-relative
                    var pcs_wtpt = icc_wtpt;
                    if (ChromaticAdaptionMatrix != null)
                    {
                        return ApplyInverseChad(pcs_wtpt);
                    }
                }
            }
            if (ChromaticAdaptionMatrix != null)
            {
                // no wtpt in icc, sum RGB and reverse chad
                var pcs_rXYZ = profile.ReadTag(SafeTagSignature.RedColorantTag);
                var pcs_gXYZ = profile.ReadTag(SafeTagSignature.GreenColorantTag);
                var pcs_bXYZ = profile.ReadTag(SafeTagSignature.BlueColorantTag);
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
        /// use lcms transform to get illuminant-relative primaries.
        /// </summary>
        private unsafe (CIEXYZ, RgbPrimaries) PopulatePrimaries()
        {
            var ctx = new CmsContext();

            ctx.SetAdaptionState(0);
            
            var xyzprof = IccProfile.CreateXYZ();
            var t = new CmsTransform(ctx, profile, CmsPixelFormat.RGBDouble, xyzprof, CmsPixelFormat.XYZDouble, RenderingIntent.ABSOLUTE_COLORIMETRIC, default);
            var pixels = new ReadOnlySpan<double>(new double[] {
                1, 0, 0,
                0, 1, 0,
                0, 0, 1,
                1, 1, 1
            });
            Span<double> xyz = stackalloc double[3];


            t.DoTransform(pixels.Slice(0), xyz, 1);
            var rXYZ = new CIEXYZ { X = xyz[0], Y = xyz[1], Z = xyz[2] };
            t.DoTransform(pixels.Slice(3), xyz, 1);
            var gXYZ = new CIEXYZ { X = xyz[0], Y = xyz[1], Z = xyz[2] };
            t.DoTransform(pixels.Slice(6), xyz, 1);
            var bXYZ = new CIEXYZ { X = xyz[0], Y = xyz[1], Z = xyz[2] };
            t.DoTransform(pixels.Slice(9), xyz, 1);
            var wXYZ = new CIEXYZ { X = xyz[0], Y = xyz[1], Z = xyz[2] };


            return (wXYZ, new(rXYZ.ToXY(), gXYZ.ToXY(), bXYZ.ToXY(), wXYZ.ToXY()));

        }

        public string GetDescription()
        {
            return profile.GetInfo(InfoType.Description);
        }

        public CIEXYZ GetIlluminantRelativeBlackPoint()
        {
            // NOTE: mediaBlackPointTag is no longer in ICC standard
            if (profile.TryReadTag(SafeTagSignature.MediaBlackPointTag, out var bkpt))
            {
                // no chad in profile, bkpt is illuminant-relative
                if (ChromaticAdaptionMatrix == null)
                {
                    return bkpt;
                }
                else
                {
                    return ApplyInverseChad(bkpt);
                }
            }

            // no bkpt in tag, use lcms transform
            var ctx = new CmsContext();
            ctx.SetAdaptionState(0);
            var t = new CmsTransform(ctx, profile, CmsPixelFormat.RGB8, IccProfile.CreateXYZ(), CmsPixelFormat.XYZDouble, RenderingIntent.ABSOLUTE_COLORIMETRIC, default);
            var input = new ReadOnlySpan<byte>(new byte[] { 0, 0, 0 });
            Span<double> outbuf = stackalloc double[3];
            t.DoTransform(input, outbuf, 1);
            bkpt = new CIEXYZ { X = outbuf[0], Y = outbuf[1], Z = outbuf[2] };
            return bkpt;
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
            profile.WriteTag(SafeTagSignature.MediaBlackPointTag, valueToWrite);
        }

        public static Matrix<double> GetChromaticAdaptationMatrix(CIEXYZ sourceIlluminant, CIEXYZ targetIlluminant)
        {
            // http://www.brucelindbloom.com/index.html?Eqn_ChromAdapt.html
            // Bradford
            var M_a = DenseMatrix.OfArray(new[,] {
                { 0.8951, 0.2664, -0.1614 },
                { -0.7502, 1.7135, 0.0367 },
                { 0.0389, -0.0685, 1.0296 },
            });

            var M_a_inv = M_a.Inverse();

            var cone_s_vec = M_a * new DenseVector(new double[] { sourceIlluminant.X, sourceIlluminant.Y, sourceIlluminant.Z });
            var cone_t_vec = M_a * new DenseVector(new double[] { targetIlluminant.X, targetIlluminant.Y, targetIlluminant.Z });

            var M = M_a_inv * DenseMatrix.Build.DenseOfDiagonalVector(cone_t_vec / cone_s_vec) * M_a;

            return M;
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

        public KeepWhitePoint KeepProfileWhitePoint { get; set; } = KeepWhitePoint.None;

        public bool ReallyWantGamma22 { get; set; } = false;

        public DeviceIccContext(IccProfile profile) : base(profile)
        {
            illuminantRelativeBlackPoint = GetIlluminantRelativeBlackPoint();
            (max_nits, min_nits) = GetProfileLuminance();
            profileRedToneCurve = profile.ReadTag(SafeTagSignature.RedTRCTag);
            profileGreenToneCurve = profile.ReadTag(SafeTagSignature.GreenTRCTag);
            profileBlueToneCurve = profile.ReadTag(SafeTagSignature.BlueTRCTag);
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
            if (profile.TryReadTag(SafeTagSignature.LuminanceTag, out var lumi))
            {
                max_nits = lumi.Y;
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
            var vcgt = profile.ReadTagOrDefault(SafeTagSignature.VcgtTag)?.ToArray();

            var devicePrimaries = ProfilePrimaries;

            var deviceOetf = new ToneCurve[] { profileRedReverseToneCurve, profileGreenReverseToneCurve, profileBlueReverseToneCurve };

            var srgbTrc = ReallyWantGamma22 ? new ToneCurve(2.2) : IccProfile.Create_sRGB().ReadTag(SafeTagSignature.RedTRCTag)!;
            var sourceEotf = new ToneCurve[] { srgbTrc, srgbTrc, srgbTrc };

            sourcePrimaries ??= RgbPrimaries.sRGB;

            var srgb_to_xyz = RgbToXYZ(RgbPrimaries.sRGB);
            var xyz_to_srgb = XYZToRgb(RgbPrimaries.sRGB);


            Matrix<double> user_matrix = DenseMatrix.CreateIdentity(3);

            // pipeline here: input signal converted to XYZ (interpreted as sRGB)

            if (!ReferenceEquals(sourcePrimaries, RgbPrimaries.sRGB))
            {
                user_matrix = RgbToXYZ(sourcePrimaries) * xyz_to_srgb * user_matrix;
            }

            // pipeline here: input signal converted to XYZ (interpreted as custom RGB)

            switch (KeepProfileWhitePoint)
            {
                case KeepWhitePoint.Bradford:
                    user_matrix = GetChromaticAdaptationMatrix(sourcePrimaries.White.ToXYZ(), devicePrimaries.White.ToXYZ()) * user_matrix;
                    break;
                case KeepWhitePoint.Simple:
                    devicePrimaries = devicePrimaries with { White = RgbPrimaries.sRGB.White };
                    break;
            }

            // pipeline here: input signal XYZ adapted to device white point

            // hook: scale white point

            var source_white_to_xyz = user_matrix * new DenseVector(new double[] { 1, 1, 1 });
            var mapped_y = source_white_to_xyz[1];
            var profile_max_nits = max_nits * (mapped_y / wtpt.Y);
            Console.WriteLine($"mapped max luminance: {profile_max_nits} cd/m2");

            // end hook

            user_matrix = XYZToRgb(devicePrimaries) * user_matrix;

            // pipeline here: linear device RGB

            // hack: eliminate fixed sRGB to XYZ transform

            user_matrix = srgb_to_xyz * user_matrix;

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
            }, new RgbToneCurve(srgbTrc, srgbTrc, srgbTrc));

            outputProfile.WriteTag(SafeTagSignature.LuminanceTag, new CIEXYZ { Y = profile_max_nits });

            var outctx = new IccContext(outputProfile);
            outctx.WriteIlluminantRelativeMediaBlackPoint(illuminantRelativeBlackPoint);

            // copy device description from device profile
            var copy_tags = new TagSignature[] { TagSignature.DeviceMfgDescTag, TagSignature.DeviceModelDescTag };

            unsafe
            {
                foreach (var tag in copy_tags)
                {
                    var tag_ptr = profile.ReadTag(tag);
                    if (tag_ptr != null)
                    {
                        outputProfile.WriteTag(tag, tag_ptr);
                    }
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
            outputProfile.WriteTag(SafeTagSignature.ProfileDescriptionTag, new_desc_mlu);

            outputProfile.WriteRawTag(MHC2Tag.Signature, mhc2);

            outputProfile.ComputeProfileId();

            return outputProfile;
        }

        public IccProfile CreatePQ10DecodeIcc(double? maxBrightnessOverride = null, double? minBrightnessOverride = null)
        {
            var sourcePrimaries = RgbPrimaries.Rec2020;
            var devicePrimaries = ProfilePrimaries;

            Matrix<double> user_matrix = DenseMatrix.CreateIdentity(3);


            switch (KeepProfileWhitePoint)
            {
                case KeepWhitePoint.Bradford:
                    user_matrix = GetChromaticAdaptationMatrix(sourcePrimaries.White.ToXYZ(), devicePrimaries.White.ToXYZ()) * user_matrix;
                    break;
                case KeepWhitePoint.Simple:
                    devicePrimaries = devicePrimaries with { White = RgbPrimaries.sRGB.White };
                    break;
            }


            // var rgb_transform = RgbToRgb(sourcePrimaries, devicePrimaries);
            // rgb_transform = XYZToRgb(devicePrimaries) * RgbToXYZ(sourcePrimaries);
            // var xyz_transform = RgbToXYZ(sourcePrimaries) * rgb_transform * XYZToRgb(sourcePrimaries);
            user_matrix = RgbToXYZ(sourcePrimaries) * XYZToRgb(devicePrimaries) * user_matrix;

            var mhc2_matrix = new double[,] {
               { user_matrix[0,0], user_matrix[0,1], user_matrix[0,2], 0 },
               { user_matrix[1,0], user_matrix[1,1], user_matrix[1,2], 0 },
               { user_matrix[2,0], user_matrix[2,1], user_matrix[2,2], 0 },
            };

            var vcgt = profile.ReadTagOrDefault(SafeTagSignature.VcgtTag)?.ToArray();
            var deviceOetf = new ToneCurve[] { profileRedReverseToneCurve, profileGreenReverseToneCurve, profileBlueReverseToneCurve };

            var use_max_nits = maxBrightnessOverride ?? max_nits;
            var use_min_nits = minBrightnessOverride ?? min_nits;

            var lut_size = 4096;
            var mhc2_lut = new double[3, 4096];
            for (int ch = 0; ch < 3; ch++)
            {
                for (int iinput = 0; iinput < lut_size; iinput++)
                {
                    var pqinput = (double)iinput / (lut_size - 1);
                    var nits = ST2084.SignalToNits(pqinput);
                    var linear = Math.Max(nits - use_min_nits, 0) / (use_max_nits - use_min_nits);
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
                MinCLL = use_min_nits,
                MaxCLL = use_max_nits,
                Matrix3x4 = mhc2_matrix,
                RegammaLUT = mhc2_lut
            };

            var mhc2 = mhc2d.ToBytes();

            var outputProfile = IccProfile.CreateRGB(devicePrimaries.White.ToXYZ().ToCIExyY(), new CIExyYTRIPLE
            {
                Red = devicePrimaries.Red.ToXYZ().ToCIExyY(),
                Green = devicePrimaries.Green.ToXYZ().ToCIExyY(),
                Blue = devicePrimaries.Blue.ToXYZ().ToCIExyY()
            }, new RgbToneCurve(profileRedToneCurve, profileGreenToneCurve, profileBlueToneCurve));

            // copy characteristics from device profile
            var copy_tags = new TagSignature[] { TagSignature.DeviceMfgDescTag, TagSignature.DeviceModelDescTag };
            unsafe
            {
                foreach (var tag in copy_tags)
                {
                    var tag_ptr = profile.ReadTag(tag);
                    if (tag_ptr != null)
                    {
                        outputProfile.WriteTag(tag, tag_ptr);
                    }
                }
            }

            outputProfile.WriteTag(SafeTagSignature.LuminanceTag, new CIEXYZ { Y = use_max_nits });

            // the profile is not read by regular applications
            // var outctx = new IccContext(outputProfile);
            // outctx.WriteIlluminantRelativeMediaBlackPoint(illuminantRelativeBlackPoint);

            // set output profile description
            outputProfile.HeaderManufacturer = profile.HeaderManufacturer;
            outputProfile.HeaderModel = profile.HeaderModel;
            outputProfile.HeaderAttributes = profile.HeaderAttributes;
            outputProfile.HeaderRenderingIntent = RenderingIntent.ABSOLUTE_COLORIMETRIC;

            var new_desc = $"CSC: HDR10 to SDR ({GetDeviceDescription()}, {use_max_nits:0} nits)";
            Console.WriteLine("Output profile description: " + new_desc);
            var new_desc_mlu = new MLU(new_desc);
            outputProfile.WriteTag(SafeTagSignature.ProfileDescriptionTag, new_desc_mlu);

            outputProfile.WriteRawTag(MHC2Tag.Signature, mhc2);

            outputProfile.ComputeProfileId();

            return outputProfile;
        }

        public IccProfile CreateSdrAcmIcc(bool calibrateTransfer)
        {
            Matrix<double> user_matrix = DenseMatrix.CreateIdentity(3);

            double[,] mhc2_lut;

            var outputprofileTrc = new RgbToneCurve(profileRedToneCurve, profileGreenToneCurve, profileBlueToneCurve);
            var vcgt = profile.ReadTagOrDefault(SafeTagSignature.VcgtTag)?.ToArray();

            if (calibrateTransfer)
            {
                var sourceEotf = ReallyWantGamma22 ? new ToneCurve(2.2) : IccProfile.Create_sRGB().ReadTag(SafeTagSignature.RedTRCTag);
                outputprofileTrc = new RgbToneCurve(sourceEotf, sourceEotf, sourceEotf);

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
            else if (vcgt != null)
            {
                // move vcgt to mhc2 only
                var lut_size = 1024;
                mhc2_lut = new double[3, lut_size];
                for (int ch = 0; ch < 3; ch++)
                {
                    for (int iinput = 0; iinput < lut_size; iinput++)
                    {
                        var input = (float)iinput / (lut_size - 1);
                        var dev_output = vcgt[ch].EvalF32(input);
                        mhc2_lut[ch, iinput] = dev_output;
                    }
                }
            }
            else
            {
                mhc2_lut = new double[,] { { 0, 1 }, { 0, 1 }, { 0, 1 } };
            }

            var devicePrimaries = ProfilePrimaries;

            switch (KeepProfileWhitePoint)
            {
                case KeepWhitePoint.Bradford:
                    user_matrix = GetChromaticAdaptationMatrix(RgbPrimaries.sRGB.White.ToXYZ(), devicePrimaries.White.ToXYZ()) * user_matrix;
                    break;
                case KeepWhitePoint.Simple:
                    devicePrimaries = devicePrimaries with { White = RgbPrimaries.sRGB.White };
                    break;
            }

            var mhc2_matrix = new double[,] {
               { user_matrix[0,0], user_matrix[0,1], user_matrix[0,2], 0 },
               { user_matrix[1,0], user_matrix[1,1], user_matrix[1,2], 0 },
               { user_matrix[2,0], user_matrix[2,1], user_matrix[2,2], 0 },
            };


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
            }, outputprofileTrc);

            // copy characteristics from device profile
            var copy_tags = new TagSignature[] {
                TagSignature.LuminanceTag,
                TagSignature.DeviceMfgDescTag,
                TagSignature.DeviceModelDescTag,
            };

            unsafe
            {
                foreach (var tag in copy_tags)
                {
                    var tag_ptr = profile.ReadTag(tag);
                    outputProfile.WriteTag(tag, tag_ptr);
                }
            }

            // the profile is not read by regular applications
            //var outctx = new IccContext(outputProfile);
            //outctx.WriteIlluminantRelativeMediaBlackPoint(illuminantRelativeBlackPoint);

            // SDR ACM will not work if the profile has negative XYZ colorants (possibly due to limited precision)
            foreach (var sig in new ISafeTagSignature<CIEXYZ>[] { SafeTagSignature.RedColorantTag, SafeTagSignature.GreenColorantTag, SafeTagSignature.BlueColorantTag })
            {
                var xyz = outputProfile.ReadTag(sig);
                if (xyz.X < 0) xyz.X = 0;
                if (xyz.Y < 0) xyz.Y = 0;
                if (xyz.Z < 0) xyz.Z = 0;
                outputProfile.WriteTag(sig, xyz);
            }

            // set output profile description

            outputProfile.HeaderManufacturer = profile.HeaderManufacturer;
            outputProfile.HeaderModel = profile.HeaderModel;
            outputProfile.HeaderAttributes = profile.HeaderAttributes;
            outputProfile.HeaderRenderingIntent = profile.HeaderRenderingIntent;

            // outputProfile.ProfileVersion = profile.ProfileVersion;

            var new_desc = $"SDR ACM: {profile.GetInfo(InfoType.Description)}";
            Console.WriteLine("Output profile description: " + new_desc);
            var new_desc_mlu = new MLU(new_desc);
            outputProfile.WriteTag(SafeTagSignature.ProfileDescriptionTag, new_desc_mlu);

            outputProfile.WriteRawTag(MHC2Tag.Signature, mhc2);

            outputProfile.ComputeProfileId();

            return outputProfile;
        }
    }
}
