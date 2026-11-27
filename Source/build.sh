#!/usr/bin/env bash
# Rebuild FFmpegForge.exe on Linux with Mono's C# compiler (sudo apt install mono-mcs / mono-devel).
# The same .exe runs on Windows (.NET Framework 4) and on Linux (Mono).
cd "$(dirname "$0")"
mcs -nologo -target:winexe -optimize+ -langversion:5 -sdk:4.5 -win32icon:src/app.ico -out:../FFmpegForge.exe \
 -resource:shaders/FSR.glsl,FSR.glsl -resource:shaders/NIS.glsl,NIS.glsl -resource:shaders/SGSR.glsl,SGSR.glsl -resource:shaders/Anime4K.glsl,Anime4K.glsl \
 -resource:shaders/OptiScaler.glsl,OptiScaler.glsl -resource:shaders/FSR2.glsl,FSR2.glsl -resource:src/hrir_ircam.bin,hrir_ircam.bin \
 -resource:aidenoise/forge_ai_denoise.py,forge_ai_denoise.py -resource:aidenoise/network_scunet.py,network_scunet.py -resource:aidenoise/LICENSE-SCUNet.txt,LICENSE-SCUNet.txt \
 -resource:aidenoise/forge_visionnext.py,forge_visionnext.py -resource:aidenoise/forge_custom_ai.py,forge_custom_ai.py -resource:aidenoise/forge_razornext.py,forge_razornext.py -resource:aidenoise/forge_ltx25.py,forge_ltx25.py -resource:aidenoise/forge_optipulsar.py,forge_optipulsar.py \
 -resource:qtgmc/forge_qtgmc.py,forge_qtgmc.py -resource:art/header.jpg,header.jpg -resource:art/drop.jpg,drop.jpg \
 -r:System.Windows.Forms.dll -r:System.Drawing.dll src/*.cs && echo "OK: ../FFmpegForge.exe rebuilt."
