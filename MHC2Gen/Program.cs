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
            var rootcmd = new RootCommand();

            var sdrcsccmd = new Command("sdr-csc", "create a matrix-LUT calibration profile for a given device profile");
            var sdracmcmd = new Command("sdr-acm", "create profile for SDR advanced color");
            var hdrdecodecmd = new Command("hdr-decode", "create a matrix-LUT profile to convert Windows HDR10 output to SDR (hard clip, no tone mapping)");

            var srcgamutoption = new Option<NamedGamut?>("--source-gamut", "specify source gamut for transform");
            var srcgamuticcoption = new Option<string?>("--source-gamut-icc", "specify source gamut for transform with ICC profile, only primaries and white point are used");

            var calibtransopt = new Option<bool>("--calibrate-transfer", "calibrate output transfer to sRGB");

            var devprofarg = new Argument<string>("device profile");
            var outprofarg = new Argument<string>("output profile");

            sdrcsccmd.Add(srcgamutoption);
            sdrcsccmd.Add(srcgamuticcoption);
            sdrcsccmd.Add(devprofarg);
            sdrcsccmd.Add(outprofarg);

            sdracmcmd.Add(calibtransopt);
            sdracmcmd.Add(devprofarg);
            sdracmcmd.Add(outprofarg);

            hdrdecodecmd.Add(devprofarg);
            hdrdecodecmd.Add(outprofarg);

            rootcmd.Add(sdrcsccmd);
            rootcmd.Add(sdracmcmd);
            rootcmd.Add(hdrdecodecmd);

            sdrcsccmd.AddValidator((result) =>
            {
                if (result.GetValueForOption(srcgamutoption) != null && result.GetValueForOption(srcgamuticcoption) != null)
                {
                    result.ErrorMessage = "source-gamut and source-gamut-icc cannot be used together.";
                }
            });

            sdrcsccmd.SetHandler((namedgamut, iccfile, deviceProfile, outputProfile) =>
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
                File.WriteAllBytes(outputProfile, mhc2icc.GetBytes());
                Console.WriteLine("Written profile {0}", outputProfile);
            }, srcgamutoption, srcgamuticcoption, devprofarg, outprofarg);

            sdracmcmd.SetHandler((calibrate, deviceProfile, outputProfile) =>
            {
                var devicc = IccProfile.Open(File.ReadAllBytes(deviceProfile));
                var ctx = new DeviceIccContext(devicc);
                var mhc2icc = ctx.CreateSdrAcmIcc(calibrate);
                File.WriteAllBytes(outputProfile, mhc2icc.GetBytes());
                Console.WriteLine("Written profile {0}", outputProfile);
            }, calibtransopt, devprofarg, outprofarg);

            hdrdecodecmd.SetHandler((deviceProfile, outputProfile) =>
            {
                var devicc = IccProfile.Open(File.ReadAllBytes(deviceProfile));
                var ctx = new DeviceIccContext(devicc);
                var mhc2icc = ctx.CreatePQ10DecodeIcc();
                File.WriteAllBytes(outputProfile, mhc2icc.GetBytes());
                Console.WriteLine("Written profile {0}", outputProfile);
            }, devprofarg, outprofarg);

            return rootcmd.Invoke(args);
        }
    }
}
