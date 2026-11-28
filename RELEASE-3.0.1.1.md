# FFmpeg Forge 3.0.1.1

A small update of 3.0.1 that adds two lossless audio formats requested by users: Monkey's Audio (.ape) and
Meridian Lossless Packing (MLP), as raw .mlp or inside Matroska audio (.mka).

## New output formats (AUDIO category)

### APE — Monkey's Audio (lossless, Hi-Res)

- FFmpeg can decode Monkey's Audio but has no encoder for it (`-c:a apes` does not exist in FFmpeg). FFmpeg Forge
  therefore decodes with FFmpeg (trim, channels, volume, filters and sample rate applied; 16 or 24-bit kept) and
  encodes with `MAC.exe`, the official Monkey's Audio encoder.
- AUDIO SETTINGS > **APE compression**: Fast, Normal, High, Extra High (default) or Insane.
- The source tags (title, artist, album, album artist, year, track, disc, genre, comment, composer and others) and
  the cover art are written as an APEv2 tag. The cover is stored as `Cover Art (Front)`, which tag editors and
  players such as foobar2000 and Mp3tag read.
- `MAC.exe` is not included. Install Monkey's Audio (free, BSD-3-Clause) from monkeysaudio.com with the
  **Download Monkey's Audio** button. FFmpeg Forge then finds it automatically in Program Files. You can also put
  `MAC.exe` in a `MonkeysAudio` folder next to `FFmpegForge.exe`, or select it with **Monkey's Audio encoder…**.

### MLP — Meridian Lossless Packing

- Two choices:
  - `MLP — Meridian Lossless Packing (raw .mlp)`: the raw bitstream, which cannot hold tags or a cover.
  - `MKA — MLP in Matroska (tags + cover)`: the same stream in Matroska audio, with the source tags and the cover
    art attached as `cover.jpg` / `cover.png`.
- Uses FFmpeg's experimental MLP encoder (`-c:a mlp -strict experimental`). It accepts 16 or 24-bit audio at 44.1,
  48, 88.2, 96, 176.4 or 192 kHz, up to 5.1 channels. Other sample rates and layouts are converted to the nearest
  supported ones automatically.
- Checked bit-exact: a 24-bit / 96 kHz FLAC encoded to .mlp and .mka decodes to identical samples.

## Other changes

- Version 3.0.1.1 in the title bar, the About box and the file properties.
- The portable README now states in English that FFmpeg 9.0.2 is included in `bin\`.

## Notes

- Monkey's Audio progress is shown in two parts: FFmpeg decoding (0 to 40 %) and MAC.exe encoding (40 to 100 %).
- A temporary WAV file is written to the Windows temporary folder during an APE conversion and deleted afterwards.
