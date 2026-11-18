# PVP Chronicle Recorder third-party notices

PVP Chronicle Recorder uses [Flyleaf](https://github.com/SuRGeoNix/Flyleaf),
licensed under LGPL-3.0-or-later, for local video playback. The Recorder downloads
the matching official Flyleaf FFmpeg build only after the user presses the install
button. The download is pinned to release v3.11.8 and verified with SHA256 before
extraction.

Flyleaf uses [FFmpeg](https://ffmpeg.org/) and the Vortice.Windows libraries. The
license files and build configuration distributed in the official Flyleaf runtime
remain authoritative for those native binaries.

The Recorder also uses [SharpCompress](https://github.com/adamhathcock/sharpcompress),
licensed under the MIT License, solely to extract the verified 7z runtime archive.

Source code and license texts:

- FlyleafLib 3.11.11: https://github.com/SuRGeoNix/Flyleaf/tree/0f061769c47d50dfdae5c667435bf6548a0dc121
- Flyleaf.FFmpeg.Bindings 9.0.2: https://github.com/SuRGeoNix/Flyleaf.FFmpeg.Generator/tree/45aa883ca63a95ab4627034a4ade97b91a632c1e
- SharpCompress 0.50.9: https://github.com/adamhathcock/sharpcompress/tree/760eef37ab0b31162065ac16e8923ea6f09d896c
- SharpGen.Runtime and SharpGen.Runtime.COM: https://github.com/SharpGenTools/SharpGenTools/tree/6990bcafe124a4c22515ad19cee5a081da8db67b
- Vortice.Windows: https://github.com/amerkoleci/Vortice.Windows/tree/9e609cb9439c9872aa1b339f177e40ec96f77239
- Vortice.Mathematics: https://github.com/amerkoleci/Vortice.Mathematics/tree/30ddc18a29aba0ae3a4590a3a84cfd6df683cd64

The accompanying license texts are in `licenses/`. The LGPL libraries are
distributed as separate DLLs and can be replaced with interface-compatible
builds. The Recorder's AGPL license applies to the Recorder source, while the
bundled libraries retain their respective licenses.

- Flyleaf: https://github.com/SuRGeoNix/Flyleaf
- Flyleaf FFmpeg builds: https://github.com/SuRGeoNix/Flyleaf/releases/tag/v3.11.8
- FFmpeg legal information: https://ffmpeg.org/legal.html
- SharpCompress: https://github.com/adamhathcock/sharpcompress
