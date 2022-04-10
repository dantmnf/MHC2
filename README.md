This repository collects information about next generation (WIP?) color management in Windows, called “Modern Color” or “Advanced Color”.

No NDA violated here.

# MHC2 Tag in ICC Profile

The MHC2 tag contains minimum and maximum luminance values for the display, a color transform matrix, and 3 regamma LUTs for each channel.

The transform matrix and regamma LUT are applied in hardware. The ICC profile should describe characteristics after MHC2 transform.

Below is the data structure of the MHC2 tag, which is inferred from several public-available ICC profiles and reverse engineering of Windows ICC loader (`mscms.dll`).

```cpp
// NOTE: all values are stored in big endian

__attribute__((scalar_storage_order("big-endian")))
struct MHC2 {
    uint32_t tag_type = 'MHC2';
    uint32_t reserved = 0;
    uint32_t regamma_lut_size;
    int32_t min_luminance; // in cd/m2, divide 65536
    int32_t max_luminance; // in cd/m2, divide 65536
    uint32_t matrix_offset = offsetof(MHC2, matrix);
    uint32_t channel0_regamma_lut_offset = offsetof(MHC2, channel0_regamma_lut);
    uint32_t channel1_regamma_lut_offset = offsetof(MHC2, channel1_regamma_lut);
    uint32_t channel2_regamma_lut_offset = offsetof(MHC2, channel2_regamma_lut);
    int32_t matrix[12]; // 4x3 matrix, row-major, divide 65536
    regamma_lut<regamma_lut_size> channel0_regamma_lut;
    regamma_lut<regamma_lut_size> channel1_regamma_lut;
    regamma_lut<regamma_lut_size> channel2_regamma_lut;
}

template<size_t lut_size>
__attribute__((scalar_storage_order("big-endian")))
struct regamma_lut {
    uint32_t magic = 'sf32';
    uint32_t reserved = 0;  // not read by mscms
    uint32_t lut[lut_size]; // divide 65536
}
```

## Color Transform Matrix

All collected ICC samples have the same semi-identity color transform matrix.

```
[[1, 0 ,0 ,0],
 [0, 1 ,0 ,0],
 [0, 0 ,1 ,0]]
```

## Regamma LUT

The observed behavior is like `vcgt` in ICC profile. Intermediate values are linearly interpolated from known points.

The GDI SetDeviceGammaRamp, IDirect3DDevice9::SetGammaRamp, and IDXGIOutput::SetGammaControl API apply another LUT on top of MHC2 LUT.

## Experiments

* Older Intel GPUs (at least UHD 630) can’t add values from other channels with the matrix.
* AMD GPUs (at least RX 6800 XT) don’t apply the matrix to mouse cursor.
  * Caveat: a matrix of `[[0,1,0,0],[1,0,0,0],[0,0,1,0]]` not only swaps red and green channels, but also add a red tint.
* Interactively adjust the matrix with [InteractiveMatrix](InteractiveMatrix) tool.
  * Assign `nvIccAdvancedColorIdentity.icm` to target display, and it will change the matrix values then reload calibration.
* A proof-of-concept tool to generate MHC2-enabled ICC profiles can be found in [MHC2Gen](MHC2Gen).

# Advanced Color for SDR

Advanced color, often referred to as HDR, is introduced in Windows 10. It performs color-managed composition in scRGB color space, and output to an HDR display in Rec. 2020 color space.

Windows 11 introduced advanced color for SDR displays, which can be activated by some trivial registry tweaks:

```reg
Windows Registry Editor Version 5.00

[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers]
"EnableAcmSupportDeveloperPreview"=dword:00000001
"EnableIntegratedPanelAcmByDefault"=dword:00000001
"MicrosoftApprovedAcmSupport"=dword:00000001

[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\MonitorDataStore]

[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\MonitorDataStore\{SOME_RANDOM_MONITOR_IDENTIFIER}]
"AutoColorManagementSupported"=dword:00000001
"WCGEnforced"=dword:00000001
```

With supported hardware configuration (see below), a new setting will appear:

![][auto color management]

To specify display characteristics, add an ICC profile as advanced color profile, if not working, add an identity MHC2 tag to that profile. Note that vcgt will not be applied, don’t set calibration target when creating profile.

A supported hardware configuration consists of:

* Supported GPU (Intel UHD 630 is not the case)
* *Non*-HDR display.

If display is HDR-capable, Windows will prefer HDR output, i.e. color encoded in Rec. 2020 and brightness encoded in SMPTE ST 2084 (PQ) instead of native gamut and transfer, and expects the display controller to convert them to appropriate electrical signal for the panel. Except for some [professional][XDR] or [reference][HX310] displays, this should be considered unreliable, inaccurate, and impossible to calibrate

## How to unfuck your [ScamHDR 400] or other HDRn’t displays
### i.e. How to force disable fake HDR 

**TL; DR: No way at reasonable cost.**

Microsoft has a good tradition that trust hardware vendor over professional users. This also applies to HDR displays.

If your display claims to be HDR-capable, you can disable it by one of the following ways:

* Override EDID
  * in Windows registry (cost: 0, may have glitch, or can’t override HDR metadata, or doesn’t work at all)
  * with workstation-grade GPUs (at 2x cost with similar performance)
  * by intercepting EDID in physical link (cost: unlimited)
* Replace it with [some][XDR] [professional][Creator Extreme] [display][HX310] (cost: $3.5k+)



[ScamHDR 400]: https://displayhdr.org/performance-criteria-cts1-1/
[XDR]: https://www.apple.com/pro-display-xdr/
[Creator Extreme]: https://www.lenovo.com/us/en/p/accessories-and-software/monitors/office/62a6rar3us
[HX310]: https://pro.sony/ue_US/products/broadcastpromonitors/bvm-hx310
[auto color management]: placeholder