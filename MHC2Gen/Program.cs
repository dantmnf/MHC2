using System;
using System.IO;

namespace MHC2Gen
{
    internal class Program
    {

        private static void ShowUsage()
        {
            Console.WriteLine("Usage: {0} sdr-calib <device profile> <calibration target profile> <output profile>", Environment.GetCommandLineArgs()[0]);
            Console.WriteLine("Usage: {0} hdr-decode <device profile> <output profile>", Environment.GetCommandLineArgs()[0]);
        }

        static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                ShowUsage();
                return 1;
            }
            var action = args[0];
            if (action == "sdr-calib")
            {
                if (args.Length != 4)
                {
                    ShowUsage();
                    return 1;
                }
                var deviceProfile = args[1];
                var targetProfile = args[2];
                var outputProfile = args[3];
                var devicc = File.ReadAllBytes(deviceProfile);
                var srgbicc = File.ReadAllBytes(targetProfile);
                var mhc2icc = IccReader.ProcessICC(devicc, srgbicc);
                File.WriteAllBytes(outputProfile, mhc2icc);
                Console.WriteLine("Written profile {0}", outputProfile);

            }
            else if (action == "hdr-decode")
            {
                if (args.Length != 3)
                {
                    ShowUsage();
                    return 1;
                }
                var deviceProfile = args[1];
                var outputProfile = args[2];
                var devicc = File.ReadAllBytes(deviceProfile);
                var mhc2icc = IccReader.CreatePQ10DecodeIcc(devicc);
                File.WriteAllBytes(outputProfile, mhc2icc);
                Console.WriteLine("Written profile {0}", outputProfile);

            }
            return 0;
        }
    }
}
