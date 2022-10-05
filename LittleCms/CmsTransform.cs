using System;
using System.Collections.Generic;
using System.Text;

using static LittleCms.CmsNative;

namespace LittleCms
{
    public class CmsTransform : CmsObject
    {
        private IccProfile inputProfile;
        private IccProfile outputProfile;

        public CmsTransform(IccProfile inputProfile, CmsPixelFormat inputFormat,
            IccProfile outputProfile, CmsPixelFormat outputFormat,
            RenderingIntent intent, TransformFlags flags)
        {
            this.inputProfile = inputProfile;
            this.outputProfile = outputProfile;

            var handle = CheckError(cmsCreateTransform(inputProfile.Handle, inputFormat, outputProfile.Handle, outputFormat, (uint)intent, (uint)flags));
            AttachObject(handle, true);
        }

        protected override void FreeObject()
        {
            cmsDeleteTransform(Handle);
        }

        public unsafe void DoTransform(ReadOnlySpan<byte> input, Span<byte> output, uint pixelsToTransform)
        {
            fixed (byte* inptr = input, outptr = output)
                cmsDoTransform(Handle, inptr, outptr, pixelsToTransform);
        }

        public unsafe void DoTransform(ReadOnlySpan<byte> input, Span<byte> output, uint pixelsPerLine, uint lineCount,
            uint bytesPerLineIn, uint bytesPerLineOut, uint bytesPerPlaneIn, uint bytesPerPlaneOut)
        {
            fixed (byte* inptr = input, outptr = output)
                cmsDoTransformLineStride(Handle, inptr, outptr, pixelsPerLine, lineCount, bytesPerLineIn, bytesPerLineOut, bytesPerPlaneIn, bytesPerPlaneOut);
        }

    }
}
