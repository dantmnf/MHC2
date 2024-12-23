using System;
using System.IO;
using LittleCms;
using System.CommandLine;
using LittleCms.Data;
using System.CommandLine.Parsing;

namespace MHC2Gen
{


    internal class Program
    {
        public enum NamedGamut
        {
            sRGB,
            AdobeRGB,
            P3D65,
            BT2020
        }

        public enum KeepWhitePointArg
        {
            Simple,
            Bradford
        }

        static int Main(string[] args)
        {
            var outProfDescOpt = new Option<string?>("--profile-desc", "description of output profile");
            var outProfVerOpt = new Option<double?>("--profile-version", "ICC version of output profile");

            var srcGamutOpt = new Option<NamedGamut?>("--source-gamut", "specify source gamut for transform");
            var srcGamutIccOpt = new Option<string?>("--source-gamut-icc", "specify source gamut for transform with ICC profile, only primaries and white point are used");

            var keepWhitePointOpt = new Option<KeepWhitePointArg?>("--keep-whitepoint", "keep profile white point");
            var chromAdaptOpt = new Option<bool>("--chromatic-adaptation", "equivalent to --keep-whitepoint=Bradford");

            var calibTransOpt = new Option<bool>("--calibrate-transfer", "calibrate output transfer to sRGB");
            var noBpcOpt = new Option<bool>("--no-bpc", "disable black point compensation");
            var reallyWantGamma22 = new Option<bool>("--i-really-want-gamma-2.2") { IsHidden = true };

            var minNitsOpt = new Option<double?>("--min-nits", "override minimum brightness nits");
            var maxNitsOpt = new Option<double?>("--max-nits", "override maximum brightness nits");

            var devProfArg = new Argument<string>("device profile");
            var outProfArg = new Argument<string>("output profile");

            var sdrCscCmd = new Command("sdr-csc", "create a matrix-LUT proofing profile for a given device profile")
            {
                outProfDescOpt,
                outProfVerOpt,
                srcGamutOpt,
                srcGamutIccOpt,
                reallyWantGamma22,
                noBpcOpt,
                keepWhitePointOpt,
                chromAdaptOpt,
                devProfArg,
                outProfArg,
            };

            var sdrAcmCmd = new Command("sdr-acm", "create profile for SDR advanced color")
            {
                outProfDescOpt,
                outProfVerOpt,
                calibTransOpt,
                noBpcOpt,
                reallyWantGamma22,
                keepWhitePointOpt,
                chromAdaptOpt,
                devProfArg,
                outProfArg
            };

            var hdrDecodeCmd = new Command("hdr-decode", "create a matrix-LUT profile to convert Windows HDR10 output to SDR (hard clip, no tone mapping)")
            {
                outProfDescOpt,
                outProfVerOpt,
                minNitsOpt,
                maxNitsOpt,
                chromAdaptOpt,
                devProfArg,
                outProfArg
            };

            var rootcmd = new RootCommand()
            {
                sdrCscCmd, sdrAcmCmd, hdrDecodeCmd
            };

            

            sdrCscCmd.AddValidator((result) =>
            {
                if (result.GetValueForOption(srcGamutOpt) != null && result.GetValueForOption(srcGamutIccOpt) != null)
                {
                    result.ErrorMessage = "source-gamut and source-gamut-icc cannot be used together.";
                }
            });

            var keepWhitePointValidator = new ValidateSymbolResult<CommandResult>((result) =>
            {
                if (result.GetValueForOption(chromAdaptOpt))
                {
                    var keepwp = result.GetValueForOption(keepWhitePointOpt);
                    if (keepwp.HasValue && keepwp.Value != KeepWhitePointArg.Bradford)
                    {
                        result.ErrorMessage = "keep-whitepoint and chromatic-adaptation specified different method.";
                    }
                }
            });

            KeepWhitePoint GetKeepWhitePointType(ParseResult result)
            {
                var keepwp = result.GetValueForOption(keepWhitePointOpt);
                if (keepwp.HasValue)
                {
                    return keepwp switch
                    {
                        KeepWhitePointArg.Simple => KeepWhitePoint.Simple,
                        KeepWhitePointArg.Bradford => KeepWhitePoint.Bradford,
                        _ => KeepWhitePoint.None
                    };
                }

                if (result.GetValueForOption(chromAdaptOpt))
                {
                    return KeepWhitePoint.Bradford;
                }

                return KeepWhitePoint.None;
            }

            sdrCscCmd.SetHandler((invocation) =>
            {
                var srcgamut = RgbPrimaries.sRGB;
                var srcdesc = "sRGB";
                var parse = invocation.ParseResult;
                var namedgamut = parse.GetValueForOption(srcGamutOpt);
                var iccfile = parse.GetValueForOption(srcGamutIccOpt);
                var deviceProfile = parse.GetValueForArgument(devProfArg);
                var profdesc = parse.GetValueForOption(outProfDescOpt);
                var profver = parse.GetValueForOption(outProfVerOpt);
                var outputProfile = parse.GetValueForArgument(outProfArg);
                var keepwp = GetKeepWhitePointType(parse);

                if (namedgamut.HasValue)
                {
                    (srcgamut, srcdesc) = namedgamut.Value switch
                    {
                        NamedGamut.sRGB => (RgbPrimaries.sRGB, "sRGB"),
                        NamedGamut.AdobeRGB => (RgbPrimaries.AdobeRGB, "AdobeRGB"),
                        NamedGamut.P3D65 => (RgbPrimaries.P3D65, "Display P3"), // tone response is fixed to sRGB, so it becomes Display P3
                        NamedGamut.BT2020 => (RgbPrimaries.Rec2020, "BT2020"),
                        _ => throw new ArgumentOutOfRangeException()
                    };
                }
                else if (iccfile != null)
                {
                    var srcctx = new IccContext(IccProfile.Open(File.ReadAllBytes(iccfile)));
                    srcgamut = srcctx.ProfilePrimaries;
                    srcdesc = srcctx.GetDescription();
                }

                var devicc = IccProfile.Open(File.ReadAllBytes(deviceProfile));
                var ctx = new DeviceIccContext(devicc);
                ctx.KeepProfileWhitePoint = keepwp;
                ctx.ReallyWantGamma22 = parse.GetValueForOption(reallyWantGamma22);
                ctx.UseBlackPointCompensation = !parse.GetValueForOption(noBpcOpt);
                var mhc2icc = ctx.CreateMhc2CscIcc(srcgamut, srcdesc);
                SetProfileProp(mhc2icc, profdesc, profver);
                File.WriteAllBytes(outputProfile, mhc2icc.GetBytes());
                Console.WriteLine("Written profile {0}", outputProfile);
            });

            sdrAcmCmd.SetHandler((invocation) =>
            {
                var parse = invocation.ParseResult;
                var deviceProfile = parse.GetValueForArgument(devProfArg);
                var profdesc = parse.GetValueForOption(outProfDescOpt);
                var profver = parse.GetValueForOption(outProfVerOpt);
                var outputProfile = parse.GetValueForArgument(outProfArg);
                var keepwp = GetKeepWhitePointType(parse);
                var calibrate = parse.GetValueForOption(calibTransOpt);

                if (keepwp == KeepWhitePoint.Bradford)
                {
                    invocation.Console.WriteLine("Warning: using Bradford chromatic adaptation with ACM is not supported and will produce strange colors with ICC shim apps");
                }

                var devicc = IccProfile.Open(File.ReadAllBytes(deviceProfile));
                var ctx = new DeviceIccContext(devicc);
                ctx.KeepProfileWhitePoint = keepwp;
                ctx.ReallyWantGamma22 = parse.GetValueForOption(reallyWantGamma22);
                ctx.UseBlackPointCompensation = !parse.GetValueForOption(noBpcOpt);
                var mhc2icc = ctx.CreateSdrAcmIcc(calibrate);
                SetProfileProp(mhc2icc, profdesc, profver);
                File.WriteAllBytes(outputProfile, mhc2icc.GetBytes());
                Console.WriteLine("Written profile {0}", outputProfile);
            });

            hdrDecodeCmd.SetHandler((invocation) =>
            {
                var parse = invocation.ParseResult;
                var deviceProfile = parse.GetValueForArgument(devProfArg);
                var profdesc = parse.GetValueForOption(outProfDescOpt);
                var profver = parse.GetValueForOption(outProfVerOpt);
                var outputProfile = parse.GetValueForArgument(outProfArg);
                var minnits = parse.GetValueForOption(minNitsOpt);
                var maxnits = parse.GetValueForOption(maxNitsOpt);
                var keepwp = GetKeepWhitePointType(parse);

                var devicc = IccProfile.Open(File.ReadAllBytes(deviceProfile));
                var ctx = new DeviceIccContext(devicc);
                ctx.KeepProfileWhitePoint = keepwp;
                var mhc2icc = ctx.CreatePQ10DecodeIcc(maxnits, minnits);
                SetProfileProp(mhc2icc, profdesc, profver);
                File.WriteAllBytes(outputProfile, mhc2icc.GetBytes());
                Console.WriteLine("Written profile {0}", outputProfile);
            });

            try
            {
                return rootcmd.Invoke(args);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.ToString());
                return 255;
            }
        }

        private static void SetProfileProp(IccProfile prof, string? desc, double? ver)
        {
            if (desc != null)
            {
                prof.WriteTag(SafeTagSignature.ProfileDescriptionTag, new MLU(desc));
            }
            if (ver.HasValue)
            {
                prof.ProfileVersion = ver.Value;
            }
            prof.ComputeProfileId();
        }
    }
}
