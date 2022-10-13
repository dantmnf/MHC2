This repository collects information about next generation (WIP?) color management in Windows, called “Modern Color” or “Advanced Color”.

> This is a long-awaited feature so I dig it out before its offical announcement. No NDA violated here.

Read official announcement and documentation from Microsoft:
* [Advancing the State of Color Management in Windows](https://devblogs.microsoft.com/directx/auto-color-management/)
* [Windows hardware display color calibration pipeline](https://learn.microsoft.com/en-us/windows/win32/wcs/display-calibration-mhc)
* [Use DirectX with Advanced Color on high/standard dynamic range displays](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/high-dynamic-range)
* [ICC profile behavior with Advanced Color](https://learn.microsoft.com/en-us/windows/win32/wcs/advanced-color-icc-profiles)

or the original reverse-engineered pipeline [here](pipeline.md).

# MHC2Gen Tool

As per documentation, an ICC profile with private extension (MHC ICC profile) is required to use modern capabilities documented above.

[MHC2Gen](MHC2Gen) is an experimental tool to generate MHC ICC profiles from existing ICC profile created with some random calibration solution.

Currently, only profiles created with DisplayCAL are tested.

> 💡 The Windows calibration loader must be enabled to load `MHC2` transform.
> > The last release of DisplayCAL calibration loader unconditionally disables a scheduled task of Windows calibration loader. You may need to re-enable `\Microsoft\Windows\WindowsColorSystem\Calibration Loader` for the profile to be loaded automatically after logout or reboot.


⏬ **[Download latest release](https://github.com/dantmnf/MHC2/releases/tag/ci-build)**

## Example 1: sRGB proofing / clamp / emulation in SDR mode

Matrix: [sRGB to XYZ] -> XYZ to sRGB -> sRGB (or some other custom gamut) to Device RGB -> sRGB to XYZ -> [XYZ to sRGB]

LUT: vcgt(sRGB transfer to device transfer)

```
MHC2Gen sdr-csc [--source-gamut=<sRGB|AdobeRGB|P3D65|BT2020> | --source-gamut-icc=<icc file>] "C:\...\DisplayCAL\storage\...\MODEL #1 2022-01-01 00-00 0.3127x 0.329y sRGB F-S XYZLUT+MTX.icm" "MODEL CSC sRGB.icm"
```

## Example 2: Create profile for SDR Auto Color Management

Matrix: identity

LUT: vcgt, or vcgt(sRGB transfer to device transfer) if `--calibrate-transfer` is specified

```
MHC2Gen sdr-acm [--calibrate-transfer] "C:\...\DisplayCAL\storage\...\MODEL #1 2022-01-01 00-00 0.3127x 0.329y sRGB F-S XYZLUT+MTX.icm" "MODEL SDR ACM.icm"
```

## Example 3: Emulate HDR10 on SDR display

Matrix: [BT2020 RGB to XYZ] -> XYZ to BT2020 RGB -> BT2020 RGB to Device RGB -> BT2020 RGB to XYZ -> [XYZ to BT2020 RGB]

LUT: vcgt(device transfer evaluated with absolute luminance)

```
MHC2Gen hdr-decode [--min-nits=<override minimun luminance>] [--man-nits=<override maximum luminance>] "C:\...\DisplayCAL\storage\...\MODEL #1 2022-01-01 00-00 0.3127x 0.329y sRGB F-S XYZLUT+MTX.icm" "MODEL PQ10 decode.icm"
```

The wire signal is converted to SDR but still tagged HDR. This is tricky to use, you need to put Windows in HDR mode but display in SDR mode, possibly with EDID override and/or OSD settings.

> Create the device profile with desired maximum luminance and dynamic (local or global) dimming disabled.

## Recommendations for creating device ICC profiles

From [ledoge/novideo_srgb](https://github.com/ledoge/novideo_srgb), another LUT-matrix-LUT solution:

> To achieve optimal results, consider creating a custom testchart in DisplayCAL with a high number of neutral (grayscale) patches, such as 256. With that, a grayscale calibration (setting "Tone curve" to anything other than "As measured") should be unnecessary unless your display lacks RGB gain controls, but can lead to better accuracy, especially on poorly behaved displays. The number of colored patches should not matter much. Additionally, configuring DisplayCAL to generate a "Curves + matrix" profile with "Black point compensation" disabled should also result in a lower average error than using an XYZ LUT profile. This advice is based on what worked well for a handful of users, so if you have anything else to add, please let me know.

# Notes for SDR Auto Color Management

As of version 22622.598, only `lumi`, `MHC2` and primaries values in a “valid” MHC ICC profile are used (tone curves and `vcgt` are ignored). Extra calibration to sRGB (or gamma 2.2[^1]) tone response via `MHC2` regamma LUT is needed for optimal results. However, with an “invalid” profile, `vcgt` will be applied.

It is expected future releases will use more characteristics like tone curves and probably PCS LUT in ICC profile, and preferably without the requirement of `MHC2` tag.

SDR Auto Color Management requires a non-HDR-capable display at the moment. You are out of luck of using color managed desktop if your display claims a <b>r<i>a</i></b><i>n<u>d</i>om</u> HDR mapping (HDRn’t) support.

## How to unfuck your [ScamHDR 400] or other HDRn’t displays
### i.e. How to force disable fake HDR 

Microsoft has a good tradition that trusting hardware vendors’ marketing scam over professional users. [This also applies to HDR displays.](https://support.microsoft.com/en-us/windows/hdr-settings-in-windows-2d767185-38ec-7fdc-6f97-bbc6c5ef24e6#:~:text=Colors%20do%20not%20display%20correctly%20on%20an%20external%20HDR%2Dcapable%20display.)

If your display claims to be HDR-capable, you can disable it in one of the following ways:

### Hiding HDR support
  * OSD setting
    * Some display only turns off HDR10 wire signal decoding without hiding HDR capability, i.e. assuming HDR10 wire signal is SDR signal. In this case, [emulating HDR10 on SDR](#example-3-emulate-hdr10-on-sdr-display) can be used to calibrate HDR output.
  * Override EDID
    * in Windows registry (cost: 0, effect varies on GPU driver: may have glitches, or can’t override HDR metadata, or still HDR-capable but doesn't turn on HDR mapping, or doesn’t work at all)
    * with workstation-grade GPUs (at 2x cost with similar performance)
    * by intercepting EDID in physical link (cost: unlimited)
    * Use an external box that can override EDID (e.g. a ~$200 3DLUT loader, maybe not HDCP-capable)

### Replacing
Replace it with [some][XDR] [professional][Creator Extreme] [display][HX310] (cost: $3.5k+)

[ScamHDR 400]: https://displayhdr.org/performance-criteria-cts1-1/
[XDR]: https://www.apple.com/pro-display-xdr/
[Creator Extreme]: https://www.lenovo.com/us/en/p/accessories-and-software/monitors/office/62a6rar3us
[HX310]: https://pro.sony/ue_US/products/broadcastpromonitors/bvm-hx310
[auto color management]: https://user-images.githubusercontent.com/2252500/194107647-788c3cab-6730-4728-b337-266ab9867481.png

[^1]: Windows SDK headers suppose a gamma 2.2 transfer (`OUTPUT_WIRE_COLOR_SPACE_G22_P709`) but experiments show that assuming an sRGB transfer function gives better average delta-E on verification (this may vary on GPU vendor).
