using System;
using System.IO;

namespace MHC2Gen
{
    internal class Program
    {
        static int Main(string[] args)
        {
            if (args.Length != 3) {
                Console.WriteLine("Usage: {0} <device profile> <calibration target profile> <output profile>", Environment.GetCommandLineArgs()[0]);
                return 1;
            }
            var deviceProfile = args[0];
            var targetProfile = args[1];
            var outputProfile = args[2];
            var devicc = File.ReadAllBytes(deviceProfile);
            var srgbicc = File.ReadAllBytes(targetProfile);
            var mhc2icc = IccReader.ProcessICC(devicc, srgbicc);
            File.WriteAllBytes(outputProfile, mhc2icc);

            Console.WriteLine("Written profile {0}", outputProfile);
            return 0;
        }
    }
}
