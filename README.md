This repository collects information about next generation (WIP?) color management in Windows, called “Modern Color” or “Advanced Color”.

No NDA violated here.

Should you have any further information, feel free to create issues or pull requests.

# MHC2 Tag in ICC Profile

The MHC2 tag contains minimum and maximum luminance values for the display, a color transform matrix, and 3 regamma LUTs for each channel.

The transform matrix and regamma LUT are applied in hardware. The ICC profile should describe characteristics after MHC2 transform.

Below is the data structure of the MHC2 tag, which is inferred from several public-available ICC profiles and reverse engineering of Windows ICC loader (`mscms.dll`).

```cpp
// NOTE: all values are stored in big endian

__attribute__((scalar_storage_order("big-endian")))
struct MHC2 {
    uint32_t signature = 'MHC2';
    uint32_t reserved = 0;
    uint32_t regamma_lut_size;
    int32_t min_luminance; // in cd/m2, divide 65536
    int32_t max_luminance; // in cd/m2, divide 65536
    uint32_t matrix_offset = offsetof(MHC2, matrix);
    uint32_t channel0_regamma_lut_offset = offsetof(MHC2, channel0_regamma_lut);
    uint32_t channel1_regamma_lut_offset = offsetof(MHC2, channel1_regamma_lut);
    uint32_t channel2_regamma_lut_offset = offsetof(MHC2, channel2_regamma_lut);
    int32_t matrix[12]; // 4x3 matrix, row-major, divide 65536
    ICCs15Fixed16ArrayType<regamma_lut_size> channel0_regamma_lut;
    ICCs15Fixed16ArrayType<regamma_lut_size> channel1_regamma_lut;
    ICCs15Fixed16ArrayType<regamma_lut_size> channel2_regamma_lut;
}

template<size_t size>
__attribute__((scalar_storage_order("big-endian")))
struct ICCs15Fixed16ArrayType {
    uint32_t signature = 'sf32';
    uint32_t reserved = 0;
    uint32_t values[size]; // divide 65536
}
```

## Color Transform Matrix

All collected ICC samples have the same semi-identity color transform matrix.

```
[[1, 0, 0, 0],
 [0, 1, 0, 0],
 [0, 0, 1, 0]]
```

From `Icm.h` in Windows SDK 10.0.22000.0:
```cpp
struct WCS_DEVICE_MHC2_CAPABILITIES
{
    UINT Size;                      //  Size of structure in bytes
    BOOL SupportsMhc2;              //  Indicates if display supports MHC2

    UINT RegammaLutEntryCount;      //  Max number of entries in the regamma lut

    // Color space transform (CSC) matrix (row-major)
    UINT CscXyzMatrixRows;          //  Number of rows in the color transform matrix
    UINT CscXyzMatrixColumns;       //  Number of columns in the color transform matrix
};
```

It seems that the transform applies in XYZ space, and the last column is offset.

I don’t know why they make this decision, while all other implementations expect an RGB-to-RGB transform.

A matrix of `RGB_to_XYZ(sRGB) * matrix([[0, 1, 0], [1, 0, 0], [0, 0, 1]]) * XYZ_to_RGB(sRGB)` attached to a sRGB profile effectively swaps red and green channels without tampering white point, check [profiles/SwapRedGreen.icm](profiles/SwapRedGreen.icm).

Check http://www.brucelindbloom.com/index.html?Eqn_RGB_XYZ_Matrix.html for XYZ/RGB conversion matrix.

## Regamma LUT

The observed behavior is like `vcgt` in ICC profile. Intermediate values are linearly interpolated from known points.

The GDI SetDeviceGammaRamp, IDirect3DDevice9::SetGammaRamp, and IDXGIOutput::SetGammaControl API apply another LUT on top of MHC2 LUT.

## Experiments

* Older Intel GPUs (at least UHD 630) can’t add values from other channels with the matrix.
* AMD GPUs (at least RX 6800 XT) don’t apply the matrix to mouse cursor.
* Despite the GPUs above all have a working matrix transform function (e.g. hue shift or driver-level color-management).
* Interactively adjust the matrix with [InteractiveMatrix](InteractiveMatrix) tool.
  * Assign `nvIccAdvancedColorIdentity.icm` to target display, and it will change the matrix values then reload calibration.
* A proof-of-concept tool to generate MHC2-enabled ICC profiles can be found in [MHC2Gen](MHC2Gen).
  * grab `lcms2.dll` from [my another project](https://github.com/dantmnf/AMDColorTweaks)
  * example 1: calibrate to sRGB in SDR mode (i.e. Advanced Color disabled)
    ```
    MHC2Gen sdr-calib "C:\...\DisplayCAL\storage\...\MODEL #1 2022-01-01 00-00 0.3127x 0.329y sRGB F-S XYZLUT+MTX.icm" "C:\Windows\System32\spool\drivers\color\sRGB Color Space Profile.icm" "MODEL calibrated to sRGB.icm"
    ```
  * example 2: decode Advanced Color HDR in GPU (disable HDR decode in display)
    ```
    MHC2Gen hdr-decode "C:\...\DisplayCAL\storage\...\MODEL #1 2022-01-01 00-00 0.3127x 0.329y sRGB F-S XYZLUT+MTX.icm" "MODEL PQ10 decode.icm"
    ```

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
[auto color management]: https://user-images.githubusercontent.com/2252500/162628301-e2ead0a7-de96-406f-8b6d-419a1bdf7660.jpg
