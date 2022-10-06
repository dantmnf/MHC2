using System;
using System.IO;
using LittleCms;
using System.CommandLine;

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
        static int Main(string[] args)
        {

            var outprofdescopt = new Option<string?>("--profile-desc", "description of output profile");
            var outprofveropt = new Option<double?>("--profile-version", "ICC version of output profile");

            var srcgamutoption = new Option<NamedGamut?>("--source-gamut", "specify source gamut for transform");
            var srcgamuticcoption = new Option<string?>("--source-gamut-icc", "specify source gamut for transform with ICC profile, only primaries and white point are used");

            var calibtransopt = new Option<bool>("--calibrate-transfer", "calibrate output transfer to sRGB");

            var devprofarg = new Argument<string>("device profile");
            var outprofarg = new Argument<string>("output profile");

            var sdrcsccmd = new Command("sdr-csc", "create a matrix-LUT calibration profile for a given device profile")
            {
               outprofdescopt, outprofveropt, srcgamutoption, srcgamuticcoption, devprofarg, outprofarg
            };

            var sdracmcmd = new Command("sdr-acm", "create profile for SDR advanced color")
            {
                outprofdescopt, outprofveropt, calibtransopt, devprofarg, outprofarg
            };

            var hdrdecodecmd = new Command("hdr-decode", "create a matrix-LUT profile to convert Windows HDR10 output to SDR (hard clip, no tone mapping)")
            {
                outprofdescopt, outprofveropt, devprofarg, outprofarg
            };

            var rootcmd = new RootCommand()
            {
                sdrcsccmd, sdracmcmd, hdrdecodecmd
            };

            sdrcsccmd.AddValidator((result) =>
            {
                if (result.GetValueForOption(srcgamutoption) != null && result.GetValueForOption(srcgamuticcoption) != null)
                {
                    result.ErrorMessage = "source-gamut and source-gamut-icc cannot be used together.";
                }
            });

            sdrcsccmd.SetHandler((namedgamut, iccfile, deviceProfile, outputProfile, profdesc, profver) =>
            {
                var srcgamut = RgbPrimaries.sRGB;
                var srcdesc = "sRGB";
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
                var mhc2icc = ctx.CreateMhc2CscIcc(srcgamut, srcdesc);
                SetProfileProp(mhc2icc, profdesc, profver);
                File.WriteAllBytes(outputProfile, mhc2icc.GetBytes());
                Console.WriteLine("Written profile {0}", outputProfile);
            }, srcgamutoption, srcgamuticcoption, devprofarg, outprofarg, outprofdescopt, outprofveropt);

            sdracmcmd.SetHandler((calibrate, deviceProfile, outputProfile, profdesc, profver) =>
            {
                var devicc = IccProfile.Open(File.ReadAllBytes(deviceProfile));
                var ctx = new DeviceIccContext(devicc);
                var mhc2icc = ctx.CreateSdrAcmIcc(calibrate);
                SetProfileProp(mhc2icc, profdesc, profver);
                File.WriteAllBytes(outputProfile, mhc2icc.GetBytes());
                Console.WriteLine("Written profile {0}", outputProfile);
            }, calibtransopt, devprofarg, outprofarg, outprofdescopt, outprofveropt);

            hdrdecodecmd.SetHandler((deviceProfile, outputProfile, profdesc, profver) =>
            {
                var devicc = IccProfile.Open(File.ReadAllBytes(deviceProfile));
                var ctx = new DeviceIccContext(devicc);
                var mhc2icc = ctx.CreatePQ10DecodeIcc();
                SetProfileProp(mhc2icc, profdesc, profver);
                File.WriteAllBytes(outputProfile, mhc2icc.GetBytes());
                Console.WriteLine("Written profile {0}", outputProfile);
            }, devprofarg, outprofarg, outprofdescopt, outprofveropt);

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
                prof.WriteTag(TagSignature.ProfileDescription, new MLU(desc));
            }
            if (ver.HasValue)
            {
                prof.ProfileVersion = ver.Value;
            }
            prof.ComputeProfileId();
        }
    }
}
