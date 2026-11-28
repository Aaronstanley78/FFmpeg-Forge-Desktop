/*
 * ForgeOptiX.exe - FFmpeg Forge 3.0.1 OptiX Denoiser helper
 *
 * Denoises raw video frames with the NVIDIA OptiX AI denoiser (AOV model, beauty layer). The OptiX runtime is part
 * of the NVIDIA driver (nvoptix.dll) and the CUDA driver API comes from nvcuda.dll: no SDK DLL is shipped.
 * Built with the NVIDIA OptiX SDK headers (github.com/NVIDIA/optix-dev, NVIDIA SDK licence), not included here.
 *
 * stdin : raw RGB24 frames (w x h)        stdout: raw RGB24 frames (w x h)
 * usage : ForgeOptiX.exe -w W -h H [-blend 0..1] [-gpu N]      (blend 0 = fully denoised, 1 = original)
 *         ForgeOptiX.exe -probe
 *
 * Build : x86_64-w64-mingw32-g++ -O2 -std=c++17 -I shim -I <optix-dev>/include ForgeOptiX.cpp -o ForgeOptiX.exe
 *         -static -lcfgmgr32 -ladvapi32
 */
#include <windows.h>
#include <cstdio>
#include <cstdarg>
#include <cstdlib>
#include <cstring>
#include <cmath>
#include <vector>
#include <fcntl.h>
#include <io.h>
#include <optix.h>
#include <optix_function_table_definition.h>
#include <optix_stubs.h>

// ---------------------------------------------------------------- CUDA driver API (nvcuda.dll)
typedef CUresult (__stdcall *PFN_cuInit)(unsigned int);
typedef CUresult (__stdcall *PFN_cuDeviceGetCount)(int*);
typedef CUresult (__stdcall *PFN_cuDeviceGet)(CUdevice*, int);
typedef CUresult (__stdcall *PFN_cuDeviceGetName)(char*, int, CUdevice);
typedef CUresult (__stdcall *PFN_cuCtxCreate)(CUcontext*, unsigned int, CUdevice);
typedef CUresult (__stdcall *PFN_cuCtxDestroy)(CUcontext);
typedef CUresult (__stdcall *PFN_cuMemAlloc)(CUdeviceptr*, size_t);
typedef CUresult (__stdcall *PFN_cuMemFree)(CUdeviceptr);
typedef CUresult (__stdcall *PFN_cuMemcpyHtoD)(CUdeviceptr, const void*, size_t);
typedef CUresult (__stdcall *PFN_cuMemcpyDtoH)(void*, CUdeviceptr, size_t);
typedef CUresult (__stdcall *PFN_cuStreamCreate)(CUstream*, unsigned int);
typedef CUresult (__stdcall *PFN_cuStreamSynchronize)(CUstream);
typedef CUresult (__stdcall *PFN_cuDriverGetVersion)(int*);

static PFN_cuInit cuInit_; static PFN_cuDeviceGetCount cuDeviceGetCount_; static PFN_cuDeviceGet cuDeviceGet_;
static PFN_cuDeviceGetName cuDeviceGetName_; static PFN_cuCtxCreate cuCtxCreate_; static PFN_cuCtxDestroy cuCtxDestroy_;
static PFN_cuMemAlloc cuMemAlloc_; static PFN_cuMemFree cuMemFree_; static PFN_cuMemcpyHtoD cuMemcpyHtoD_;
static PFN_cuMemcpyDtoH cuMemcpyDtoH_; static PFN_cuStreamCreate cuStreamCreate_; static PFN_cuStreamSynchronize cuStreamSynchronize_;
static PFN_cuDriverGetVersion cuDriverGetVersion_;

static void logf_(const char* fmt, ...)
{
    va_list ap; va_start(ap, fmt);
    fputs("ForgeOX ", stderr); vfprintf(stderr, fmt, ap); fputc('\n', stderr); fflush(stderr);
    va_end(ap);
}

static bool loadCuda()
{
    HMODULE m = LoadLibraryA("nvcuda.dll");
    if (!m) return false;
#define GETP(var, name) var = (decltype(var))GetProcAddress(m, name); if (!var) { logf_("nvcuda.dll: %s missing", name); return false; }
    GETP(cuInit_, "cuInit"); GETP(cuDeviceGetCount_, "cuDeviceGetCount"); GETP(cuDeviceGet_, "cuDeviceGet");
    GETP(cuDeviceGetName_, "cuDeviceGetName"); GETP(cuCtxCreate_, "cuCtxCreate_v2"); GETP(cuCtxDestroy_, "cuCtxDestroy_v2");
    GETP(cuMemAlloc_, "cuMemAlloc_v2"); GETP(cuMemFree_, "cuMemFree_v2"); GETP(cuMemcpyHtoD_, "cuMemcpyHtoD_v2");
    GETP(cuMemcpyDtoH_, "cuMemcpyDtoH_v2"); GETP(cuStreamCreate_, "cuStreamCreate"); GETP(cuStreamSynchronize_, "cuStreamSynchronize");
    GETP(cuDriverGetVersion_, "cuDriverGetVersion");
#undef GETP
    return true;
}

#define CU(x) do { CUresult r_ = (x); if (r_ != 0) { logf_("CUDA error %d in %s", (int)r_, #x); return 3; } } while (0)
#define OX(x) do { OptixResult r_ = (x); if (r_ != OPTIX_SUCCESS) { logf_("OptiX error %d (%s) in %s", (int)r_, optixGetErrorName(r_), #x); return 4; } } while (0)

static bool readAll(void* buf, size_t n)
{
    char* p = (char*)buf; size_t got = 0;
    while (got < n) { size_t r = fread(p + got, 1, n - got, stdin); if (r == 0) return false; got += r; }
    return true;
}

int main(int argc, char** argv)
{
    int W = 0, H = 0, gpu = 0; float blend = 0.f; bool probe = false;
    for (int i = 1; i < argc; i++)
    {
        if (!strcmp(argv[i], "-w") && i + 1 < argc) W = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-h") && i + 1 < argc) H = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-blend") && i + 1 < argc) blend = (float)atof(argv[++i]);
        else if (!strcmp(argv[i], "-gpu") && i + 1 < argc) gpu = atoi(argv[++i]);
        else if (!strcmp(argv[i], "-probe")) probe = true;
    }
    if (blend < 0) blend = 0; if (blend > 1) blend = 1;
    if (!probe && (W < 8 || H < 8)) { logf_("usage: ForgeOptiX.exe -w W -h H [-blend 0..1] [-gpu N] | -probe"); return 2; }

    if (!loadCuda()) { logf_("no NVIDIA driver found (nvcuda.dll): the OptiX Denoiser needs an NVIDIA GPU"); return 5; }
    CU(cuInit_(0));
    int nDev = 0; CU(cuDeviceGetCount_(&nDev));
    if (nDev <= 0) { logf_("no CUDA device"); return 5; }
    if (gpu < 0 || gpu >= nDev) gpu = 0;
    CUdevice dev; CU(cuDeviceGet_(&dev, gpu));
    char name[256] = {0}; cuDeviceGetName_(name, 255, dev);
    int drv = 0; cuDriverGetVersion_(&drv);
    OptixResult ir = optixInit();
    if (ir != OPTIX_SUCCESS)
    {
        logf_("OptiX %d.%d is not available in this NVIDIA driver (error %d %s): update the NVIDIA driver", OPTIX_VERSION / 10000, (OPTIX_VERSION % 10000) / 100, (int)ir, optixGetErrorName(ir));
        return 6;
    }
    if (probe) { printf("OK %s (CUDA driver %d.%d, OptiX %d.%d)\n", name, drv / 1000, (drv % 1000) / 10, OPTIX_VERSION / 10000, (OPTIX_VERSION % 10000) / 100); return 0; }

    CUcontext cuCtx; CU(cuCtxCreate_(&cuCtx, 0, dev));
    CUstream stream; CU(cuStreamCreate_(&stream, 0));
    OptixDeviceContext ctx;
    OptixDeviceContextOptions copt = {};
    OX(optixDeviceContextCreate(cuCtx, &copt, &ctx));

    OptixDenoiserOptions dopt = {};
    dopt.guideAlbedo = 0; dopt.guideNormal = 0; dopt.denoiseAlpha = OPTIX_DENOISER_ALPHA_MODE_COPY;
    OptixDenoiser den;
    OX(optixDenoiserCreate(ctx, OPTIX_DENOISER_MODEL_KIND_AOV, &dopt, &den));
    OptixDenoiserSizes sz = {};
    OX(optixDenoiserComputeMemoryResources(den, (unsigned)W, (unsigned)H, &sz));
    size_t scratchSize = sz.withoutOverlapScratchSizeInBytes;
    CUdeviceptr dState, dScratch, dIn, dOut, dIntensity, dAvg;
    CU(cuMemAlloc_(&dState, sz.stateSizeInBytes));
    CU(cuMemAlloc_(&dScratch, scratchSize));
    size_t imgBytes = (size_t)W * H * 4 * sizeof(float);
    CU(cuMemAlloc_(&dIn, imgBytes)); CU(cuMemAlloc_(&dOut, imgBytes));
    CU(cuMemAlloc_(&dIntensity, sizeof(float))); CU(cuMemAlloc_(&dAvg, 3 * sizeof(float)));
    OX(optixDenoiserSetup(den, stream, (unsigned)W, (unsigned)H, dState, sz.stateSizeInBytes, dScratch, scratchSize));

    OptixImage2D in = {}, out = {};
    in.data = dIn; in.width = W; in.height = H; in.rowStrideInBytes = W * 4 * sizeof(float); in.pixelStrideInBytes = 4 * sizeof(float); in.format = OPTIX_PIXEL_FORMAT_FLOAT4;
    out = in; out.data = dOut;
    OptixDenoiserLayer layer = {}; layer.input = in; layer.output = out;
    OptixDenoiserGuideLayer guide = {};
    OptixDenoiserParams params = {};
    params.hdrIntensity = dIntensity; params.hdrAverageColor = dAvg; params.blendFactor = blend;

    // sRGB <-> linear (the denoiser works on linear light)
    float lut[256];
    for (int i = 0; i < 256; i++) { float c = i / 255.f; lut[i] = c <= 0.04045f ? c / 12.92f : powf((c + 0.055f) / 1.055f, 2.4f); }
    std::vector<unsigned char> enc(4097);
    for (int i = 0; i <= 4096; i++)
    {
        float l = i / 4096.f, s = l <= 0.0031308f ? l * 12.92f : 1.055f * powf(l, 1.f / 2.4f) - 0.055f;
        int v = (int)lroundf(s * 255.f); enc[i] = (unsigned char)(v < 0 ? 0 : v > 255 ? 255 : v);
    }

    _setmode(_fileno(stdin), _O_BINARY); _setmode(_fileno(stdout), _O_BINARY);
    setvbuf(stdout, NULL, _IOFBF, 1 << 22);
    size_t px = (size_t)W * H;
    std::vector<unsigned char> rgb(px * 3);
    std::vector<float> f(px * 4);
    logf_("OptiX Denoiser on %s: %dx%d, strength %d %%", name, W, H, (int)lroundf((1 - blend) * 100));
    long frames = 0; DWORD t0 = GetTickCount();
    while (readAll(rgb.data(), rgb.size()))
    {
        for (size_t i = 0; i < px; i++) { f[i * 4] = lut[rgb[i * 3]]; f[i * 4 + 1] = lut[rgb[i * 3 + 1]]; f[i * 4 + 2] = lut[rgb[i * 3 + 2]]; f[i * 4 + 3] = 1.f; }
        CU(cuMemcpyHtoD_(dIn, f.data(), imgBytes));
        OX(optixDenoiserComputeIntensity(den, stream, &in, dIntensity, dScratch, scratchSize));
        OX(optixDenoiserComputeAverageColor(den, stream, &in, dAvg, dScratch, scratchSize));
        OX(optixDenoiserInvoke(den, stream, &params, dState, sz.stateSizeInBytes, &guide, &layer, 1, 0, 0, dScratch, scratchSize));
        CU(cuStreamSynchronize_(stream));
        CU(cuMemcpyDtoH_(f.data(), dOut, imgBytes));
        for (size_t i = 0; i < px * 4; i += 4)
            for (int c = 0; c < 3; c++)
            {
                float v = f[i + c]; int k = (int)(v * 4096.f + 0.5f); k = k < 0 ? 0 : k > 4096 ? 4096 : k;
                rgb[i / 4 * 3 + c] = enc[k];
            }
        if (fwrite(rgb.data(), 1, rgb.size(), stdout) != rgb.size()) break;
        frames++;
        if (frames % 48 == 0) logf_("%ld frames, %.1f fps", frames, frames * 1000.0 / (double)(GetTickCount() - t0 + 1));
    }
    fflush(stdout);
    logf_("done: %ld frames", frames);
    optixDenoiserDestroy(den); optixDeviceContextDestroy(ctx);
    cuMemFree_(dIn); cuMemFree_(dOut); cuMemFree_(dState); cuMemFree_(dScratch); cuMemFree_(dIntensity); cuMemFree_(dAvg);
    cuCtxDestroy_(cuCtx);
    return 0;
}
