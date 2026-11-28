ForgeOptiX.exe - OptiX Denoiser helper of FFmpeg Forge 3.0.1
============================================================

ForgeOptiX.cpp (GPL-3.0, FFmpeg Forge) streams raw RGB24 frames through the NVIDIA OptiX AI
denoiser. The OptiX runtime (nvoptix.dll) and the CUDA driver (nvcuda.dll) come with the NVIDIA
driver; nothing else is needed at run time.

Building needs the NVIDIA OptiX SDK headers, which are not included here (NVIDIA SDK licence):
  git clone https://github.com/NVIDIA/optix-dev
  x86_64-w64-mingw32-g++ -O2 -std=c++17 -I shim -I optix-dev/include ForgeOptiX.cpp ^
      -o ForgeOptiX.exe -static -s -lcfgmgr32 -ladvapi32
(shim\cuda.h only declares the few CUDA driver types used; the functions are loaded from nvcuda.dll.)

usage: ForgeOptiX.exe -w W -h H [-blend 0..1] [-gpu N]     stdin/stdout: raw RGB24 frames
       ForgeOptiX.exe -probe                               prints the GPU / driver / OptiX version
