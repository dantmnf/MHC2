This repository collects information about next generation (WIP?) color management in Windows, called “Modern Color” or “Advanced Color”.

> This is a long-awaited feature so I dig it out before its offical announcement. No NDA violated here.

Should you have any further information, feel free to create issues, discussions or pull requests.

# Hardware Color Space Transform DDI

The Windows equivalent to [Linux DRM color management](https://docs.kernel.org/gpu/drm-kms.html#c.drm_crtc_enable_color_mgmt).

From `ntddvdeo.h`, we have [`IOCTL_COLORSPACE_TRANSFORM_SET`](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddvdeo/ni-ntddvdeo-ioctl_colorspace_transform_set)([`COLORSPACE_TRANSFORM_SET_INPUT`](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddvdeo/ns-ntddvdeo-colorspace_transform_set_input)). Reverse engineering of `Windows.Internal.Graphics.Display.DisplayColorManagement.dll` supposes it is applied on `{monitor instance path}\color`.
> I always get `ERROR_ACCESS_DENIED` when trying to open the device.

From `d3dkmthk.h`, we have [`D3DKMTSetMonitorColorSpaceTransform`](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmthk/nf-d3dkmthk-d3dkmtsetmonitorcolorspacetransform)([`D3DKMT_SET_COLORSPACE_TRANSFORM`](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmthk/ns-d3dkmthk-_d3dkmt_set_colorspace_transform)).
> All my experiments end in `STATUS_NOT_SUPPORTED`.

Check color management capabilities with [CapsCheck](CapsCheck) tool, specifically, the `MatrixDDI` value.

The Windows ICC loader (`mscms.dll`) passes a matrix and LUT originated from ICC profile to `Windows.Internal.Graphics.Display.DisplayColorManagement.dll`.

# Access Color Space Transform DDI via ICC Profile

The `MHC2` tag is a Microsoft-specific private extension to ICC profiles. The tag contains minimum[^1] and peak[^2] luminance values for the display, a color transform matrix, and 3 regamma LUTs for each channel. 

The transform matrix and regamma LUT are transformed to LUT-matrix-LUT form and passed to `IOCTL_COLORSPACE_TRANSFORM_SET`.

Similar to `vcgt`, the ICC profile should describe characteristics after matrix and regamma LUT transform.

Below is the data structure of the MHC2 tag, which is inferred from several public-available ICC profiles and reverse engineering of Windows ICC loader.

```cpp
// NOTE: all values are stored in big endian

__attribute__((scalar_storage_order("big-endian")))
struct MHC2Tag {
    uint32_t signature = 'MHC2';
    uint32_t reserved = 0;
    uint32_t regamma_lut_size;
    ICCs15Fixed16Number min_luminance; // in cd/m2
    ICCs15Fixed16Number max_luminance; // in cd/m2
    uint32_t matrix_offset = offsetof(MHC2Tag, matrix);
    uint32_t channel0_regamma_lut_offset = offsetof(MHC2Tag, channel0_regamma_lut);
    uint32_t channel1_regamma_lut_offset = offsetof(MHC2Tag, channel1_regamma_lut);
    uint32_t channel2_regamma_lut_offset = offsetof(MHC2Tag, channel2_regamma_lut);
    ICCs15Fixed16Number matrix[12]; // 4x3 matrix, row-major
    ICCs15Fixed16ArrayType<regamma_lut_size> channel0_regamma_lut;
    ICCs15Fixed16ArrayType<regamma_lut_size> channel1_regamma_lut;
    ICCs15Fixed16ArrayType<regamma_lut_size> channel2_regamma_lut;
}

template<size_t size>
__attribute__((scalar_storage_order("big-endian")))
struct ICCs15Fixed16ArrayType {
    uint32_t signature = 'sf32';
    uint32_t reserved = 0;
    ICCs15Fixed16Number values[size];
}
```


## Color Transform Matrix

All collected ICC samples have the same semi-identity color transform matrix.

$$\begin{bmatrix}
1 & 0 & 0 & 0 \\
0 & 1 & 0 & 0 \\
0 & 0 & 1 & 0
\end{bmatrix}$$

The last column seems to be offset.

## Regamma LUT

The observed behavior is like `vcgt` in ICC profile. Intermediate values are linearly interpolated from known points.

The GDI SetDeviceGammaRamp will merge together with MHC2 LUT, but the apply order is to be determined.

## Pipeline

Public headers, reverse engineering and experiments reveals the following pipeline:

$$ 
\begin{bmatrix}
R' \\
G'\\
B' 
\end{bmatrix} =
\textup{LUT} \left(
  \textup{Regamma} \left(
    \begin{bmatrix}
      \textup{XYZ} \\
      to \\
      \textup{RGB}
    \end{bmatrix}
    \cdot
    \boldsymbol{M}
    \cdot
    \begin{bmatrix}
      \textup{RGB} \\
      to \\
      \textup{XYZ}
    \end{bmatrix}
    \cdot
    \textup{Degamma} \left( 
      \begin{bmatrix}
        R \\
        G \\
        B
      \end{bmatrix} 
    \right)
   \right)
\right)
$$

RGB/XYZ (3×3) matrices[^3] and degamma/regamma function are fixed in this pipeline:

* For SDR output, RGB/XYZ transform is based on sRGB and degamma/regamma transform is based on sRGB transfer function (or gamma 2.2[^4]).
* For HDR output, RGB/XYZ transform is based on Rec. 2020 and degamma/regamma transform is based on ST 2084 (PQ).

We can do some linear algebra 101 exercise to eliminate fixed RGB/XYZ matrices in pipeline and use a RGB-to-RGB matrix:

$$\boldsymbol{M} = \begin{bmatrix}\textup{RGB} \\
to \\
\textup{XYZ} \end{bmatrix}
\cdot
\begin{bmatrix}\textup{RGB} \\
to \\
\textup{RGB} \end{bmatrix}
\cdot
\begin{bmatrix}\textup{XYZ} \\
to \\
\textup{RGB} \end{bmatrix}$$

and given

$$\begin{bmatrix}\textup{XYZ} \\
to \\
\textup{RGB} \end{bmatrix} =
\begin{bmatrix}\textup{RGB} \\
to \\
\textup{XYZ} \end{bmatrix}^{-1}$$

which will mathematically transform the pipeline to a more famaliar one

$$
\begin{bmatrix}
R' \\
G'\\
B' 
\end{bmatrix} =
\textup{LUT} \left(
  \textup{Regamma} \left(
    \begin{bmatrix}
      \textup{RGB} \\
      to \\
      \textup{RGB}
    \end{bmatrix}
    \cdot
    \textup{Degamma} \left( 
      \begin{bmatrix}
        R \\
        G \\
        B
      \end{bmatrix} 
    \right)
   \right)
\right)
$$


### Example

A matrix of 

$$
\begin{bmatrix}\textup{sRGB} \\
to \\
\textup{XYZ} \end{bmatrix} \cdot \begin{bmatrix} 0 & 1 & 0 \\
1 & 0 & 0 \\
0 & 0 & 1 \end{bmatrix} \cdot \begin{bmatrix}\textup{XYZ} \\
to \\
\textup{sRGB} \end{bmatrix}
$$

in SDR mode swaps red and green channels, check [profiles/SwapRedGreen.icm](profiles/SwapRedGreen.icm).

Since degamma/regamma function are fixed, we need to use corresponding TRC (sRGB or gamma 2.2[^4]) in the ICC profile that have non-identity XYZ transform matrix.


## Experiments

* Older Intel GPUs (at least UHD 630) doesn’t support matrix transform DDI and they are not adding new features to driver any more, nor implementing DDI for an [already implemented feature](https://intel.github.io/drivers.gpu.control-library/Control/api.html?#ctlpixeltransformationsetconfig).
* AMD GPUs (at least RX 6800 XT) don’t apply the matrix to, or mess up, the mouse cursor. [Their own API](https://gpuopen-librariesandsdks.github.io/adl/group__COLORAPI.html#ga2b586a1579951747c384bd32df4492a3) doesn’t suffer from this issue.
* Interactively adjust the matrix with [InteractiveMatrix](InteractiveMatrix) tool.
  * Assign `nvIccAdvancedColorIdentity.icm` to target display, and it will change the matrix values then reload calibration.

## MHC2Gen Tool

A proof-of-concept tool to generate MHC2-enabled ICC profiles can be found in [MHC2Gen](MHC2Gen).

> 💡 The Windows calibration loader must be enabled to load `MHC2` transform.
> > The last release of DisplayCAL calibration loader unconditionally disables a scheduled task of Windows calibration loader. You may need to re-enable `\Microsoft\Windows\WindowsColorSystem\Calibration Loader` for the profile to be loaded automatically after logout or reboot.


### Example 1: calibrate to sRGB in SDR mode

```
MHC2Gen sdr-csc "C:\...\DisplayCAL\storage\...\MODEL #1 2022-01-01 00-00 0.3127x 0.329y sRGB F-S XYZLUT+MTX.icm" "MODEL CSC sRGB.icm"
```

### Example 2: convert HDR10 to SDR in GPU

```
MHC2Gen hdr-decode "C:\...\DisplayCAL\storage\...\MODEL #1 2022-01-01 00-00 0.3127x 0.329y sRGB F-S XYZLUT+MTX.icm" "MODEL PQ10 decode.icm"
```

The wire signal is converted to SDR but still tagged HDR. This is tricky to use, you need to put Windows in HDR mode but display in SDR mode, possibly with EDID override and/or OSD settings.

### Recommendations for creating device ICC profiles

From [ledoge/novideo_srgb](https://github.com/ledoge/novideo_srgb), another LUT-matrix-LUT solution:

> To achieve optimal results, consider creating a custom testchart in DisplayCAL with a high number of neutral (grayscale) patches, such as 256. With that, a grayscale calibration (setting "Tone curve" to anything other than "As measured") should be unnecessary unless your display lacks RGB gain controls, but can lead to better accuracy, especially on poorly behaved displays. The number of colored patches should not matter much. Additionally, configuring DisplayCAL to generate a "Curves + matrix" profile with "Black point compensation" disabled should also result in a lower average error than using an XYZ LUT profile. This advice is based on what worked well for a handful of users, so if you have anything else to add, please let me know.

# Color-Managed Desktop Composition

Advanced color, often referred to as HDR, is introduced in Windows 10. It performs color-managed composition in scRGB color space, and output to an HDR display in Rec. 2020 color space.

Windows 11 22H2 update introduced advanced color for SDR displays, which can be activated by a trivial registry tweak:

```reg
Windows Registry Editor Version 5.00

[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers]
"EnableAcmSupportDeveloperPreview"=dword:00000001
```

With supported hardware configuration (see below), a new setting will appear:

![][auto color management]

To specify display characteristics, add an ICC profile with identity MHC2 tag for that display (don’t check “Add as Advanced Color Profile”).

> 💡 Generate the profile with MHC2Gen:
> ```
> MHC2Gen sdr-acm "C:\...\DisplayCAL\storage\...\MODEL #1 2022-01-01 00-00 0.3127x 0.329y sRGB F-S XYZLUT+MTX.icm" "MODEL SDR ACM.icm"
> ```

As of version 22622.598, only `vcgt`, `lumi`, `MHC2` and primaries values in ICC profile are used. Extra calibration to sRGB (or gamma 2.2, see above) tone response via `vcgt` or `MHC2` regamma LUT is needed for optimal results.

It is expected future releases will use more characteristics (preferably 3D LUT) in ICC profile, and work without the `MHC2` tag.

## Hardware Requirements

A supported hardware configuration consists of:

* GPU that support color space transform DDI (Intel UHD 630 is not the case)
* *Non*-HDR display (this may subject to change)

If display is HDR-capable, Windows will prefer HDR output (at present), i.e. color encoded in Rec. 2020 and brightness encoded in SMPTE ST 2084 (PQ) instead of native gamut and transfer, and expects the display controller to convert them to appropriate electrical signal for the panel. Except for some [professional][XDR] or [reference][HX310] displays, this should be considered unreliable, inaccurate, and impossible to calibrate.

## How to unfuck your [ScamHDR 400] or other HDRn’t displays
### i.e. How to force disable fake HDR 

**TL; DR: No way at reasonable cost.**

Microsoft has a good tradition that trusting hardware vendors’ marketing scam over professional users. [This also applies to HDR displays.](https://support.microsoft.com/en-us/windows/hdr-settings-in-windows-2d767185-38ec-7fdc-6f97-bbc6c5ef24e6#:~:text=Colors%20do%20not%20display%20correctly%20on%20an%20external%20HDR%2Dcapable%20display.)

If your display claims to be HDR-capable, you can disable it in one of the following ways:

* Override EDID
  * in Windows registry (cost: 0, effect varies on GPU driver: may have glitches, or can’t override HDR metadata, or doesn’t work at all)
  * with workstation-grade GPUs (at 2x cost with similar performance)
  * by intercepting EDID in physical link (cost: unlimited)
  * Use an external box that can override EDID (e.g. a ~$200 3DLUT loader, maybe not HDCP-capable)
* Replace it with [some][XDR] [professional][Creator Extreme] [display][HX310] (cost: $3.5k+)


[ScamHDR 400]: https://displayhdr.org/performance-criteria-cts1-1/
[XDR]: https://www.apple.com/pro-display-xdr/
[Creator Extreme]: https://www.lenovo.com/us/en/p/accessories-and-software/monitors/office/62a6rar3us
[HX310]: https://pro.sony/ue_US/products/broadcastpromonitors/bvm-hx310
[auto color management]: https://user-images.githubusercontent.com/2252500/194107647-788c3cab-6730-4728-b337-266ab9867481.png

[^1]: The mediaBlackPointTag (`bkpt`) is no longer in ICC specification.

[^2]: Peak luminance of probably a small part of the display. Maximum average full-frame luminance should reside in the standard luminanceTag (`lumi`).

[^3]: Check http://www.brucelindbloom.com/index.html?Eqn_RGB_XYZ_Matrix.html for XYZ/RGB conversion matrix.

[^4]: headers suppose a gamma 2.2 transfer (`OUTPUT_WIRE_COLOR_SPACE_G22_P709`) but sRGB transfer function coefficients are found in `Windows.Internal.Graphics.Display.DisplayColorManagement.dll`, which merges XYZ matrix, the MHC2 regamma LUT and the GDI gamma ramp to an RGB LUT-matrix-LUT pipeline.
