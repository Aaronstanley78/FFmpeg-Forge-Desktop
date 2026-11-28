# FFmpeg Forge 3.0.1

## OptiX Denoiser

**VIDEO SETTINGS > OptiX Denoiser** runs NVIDIA's OptiX AI denoiser, the denoiser used by 3D renderers, on your NVIDIA GPU. The OptiX runtime is part of the NVIDIA driver, so there is nothing to install.

- Strength from 0 to 100 %. 60 to 80 % usually looks the most natural on camera footage, because OptiX was trained on rendering noise.
- It runs first, before the Denoiser IA, Deblurring and the upscalers. The audio is kept.
- Videos only.
- Requires an NVIDIA GPU with an up-to-date driver.

## OptiPulsar, now built in

OptiPulsar (AI Upscale) no longer needs Visual Enhancer. Its model is NERVE by Philip Hofmann (Apache-2.0), a fast convolutional super-resolution network, and FFmpeg Forge now runs it with its own built-in engine.

- Click **Install OptiPulsar model...** once in the AI Upscale section (Model: OptiPulsar, Engine: Built-in). FFmpeg Forge installs Python 3.12, PyTorch CUDA and the official NERVE x2 / x4 weights into `OptiPulsar\` next to `FFmpegForge.exe` (about 3 GB).
- Factors x1, x2, x4 and x8 (native x2 and x4), on the GPU (CUDA) or the CPU.
- Videos are streamed from the decoder to the final encoder: no picture files, one single encode.
- The Visual Enhancer engine is still available for OptiPulsar.

## Deblurring with FocusPulsar

The Deblurring section (FocusPulsar, strength 0 to 200 %) is kept. It runs through an existing Visual Enhancer installation:

- Choose your Visual Enhancer folder (the one with `VE_CLI.exe`) with **Visual Enhancer folder...**.
- Download the model once in Visual Enhancer (Models tab).
- FFmpeg Forge writes a Visual Enhancer preset for FocusPulsar. To use your own settings, export a preset from the Visual Enhancer app and save it as `VE-presets\focuspulsar.json`.

## Removed

- Lucid and Lucid 2 (AI Upscale) and ClearMagnetar (Denoiser IA). These models are only available inside Visual Enhancer.
- A saved Lucid choice falls back to VisionNext, and ClearMagnetar falls back to SCUNet.

## Also in this version

The 3.0 interface:

- Graphite design with cyan / indigo accents.
- A workflow header (Decode, Denoise, Deblur, Upscale, HDR, Encode).
- Stage cards with an on / off switch in their header.

## Download

| File | Content |
| --- | --- |
| `FFmpegForge-3.0.1-Portable.rar` | Windows 10 / 11 64-bit portable edition, with the FFmpeg 9.0.2 full build (gyan.dev), ForgeRTX, ForgeOptiX and dovi_tool |

Extract the archive anywhere and start `FFmpegForge.exe`. Nothing is installed and nothing is written to the registry.

## Requirements

- Windows 10 / 11 64-bit with .NET Framework 4.x
- OptiPulsar built in: an NVIDIA GPU (CUDA) is recommended; the CPU mode works without one, more slowly.
- Deblurring (FocusPulsar): an installed Visual Enhancer with the model downloaded (DirectX 12 GPU)

## Licence

FFmpeg Forge is released under the GNU GPL v3. ForgeOptiX.exe is built with the NVIDIA OptiX SDK headers (NVIDIA licence, see LICENSE-OptiX.txt); the OptiX runtime comes with the NVIDIA driver. NERVE (Apache-2.0), FFmpeg, dovi_tool, WanGP and the other AI models keep their own licences. FFmpeg Forge does not contain or redistribute any Visual Enhancer file and is not affiliated with Merserk.
