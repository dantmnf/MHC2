using System;
using System.Collections.Generic;
using System.Text;

using static LittleCms.CmsNative;

namespace LittleCms.Data
{
    public class ToneCurve : CmsObject
    {
        public ToneCurve(nint handle, bool moveOwnership) : base(handle, moveOwnership) { }

        public ToneCurve(double gamma)
        {
            AttachObject(CheckError(cmsBuildGamma(nint.Zero, gamma)), true);
        }

        public ToneCurve(ReadOnlySpan<float> table)
        {
            AttachObject(CheckError(cmsBuildTabulatedToneCurveFloat(nint.Zero, (uint)table.Length, in table[0])), true);
        }

        public ToneCurve(ReadOnlySpan<ushort> table)
        {
            AttachObject(CheckError(cmsBuildTabulatedToneCurve16(nint.Zero, (uint)table.Length, in table[0])), true);
        }

        public static ToneCurve CopyFromObject(nint copyFromObject)
        {
            var handle = CheckError(cmsDupToneCurve(copyFromObject));
            return new(handle, true);
        }
        protected override void FreeObject()
        {
            throw new NotImplementedException();
        }

        public ToneCurve Reverse()
        {
            var handle = CheckError(cmsReverseToneCurve(Handle));
            return new(handle, true);
        }

        public ToneCurve Reverse(uint sampleCount)
        {
            var handle = CheckError(cmsReverseToneCurveEx(sampleCount, Handle));
            return new(handle, true);
        }

        public ushort EvalU16(ushort x) => cmsEvalToneCurve16(Handle, x);
        public float EvalF32(float x) => cmsEvalToneCurveFloat(Handle, x);

    }
}
