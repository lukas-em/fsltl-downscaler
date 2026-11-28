# FSLTL Downscaler

A free Windows tool that reduces the texture size of **FSLTL Traffic Base** for **MSFS 2020** and **MSFS 2024**. It also updates `layout.json` and `manifest.json`, so MSFS loads the package without any further steps.

Example: FSLTL 1.7.0 at 1024 px shrinks from 55.9 GB to 5.9 GB.

**Download:** [FSLTL Downscaler on Flightsim.to](https://flightsim.to/addon/119267/fsltl-downscaler-smaller-fsltl-textures-more-fps-in-msfs-2024)

## How to use

1. Close MSFS.
2. Unzip the archive to any folder, for example your Desktop. Don't put it into your Community folder.
3. Double-click `FSLTL-Downscaler.exe`.
4. The tool finds FSLTL for MSFS 2020 and MSFS 2024 automatically. If you have both, click "Change" to switch. If FSLTL isn't found, click "Change" and select the `fsltl-traffic-base` folder inside your Community folder. Linked folders (e.g. Addon Linker) are supported.
5. Choose a texture size and click "Downscale".

Click "Restore original" to put the original textures back. A ZIP backup is unpacked for this and needs the full size of the originals as free space. After an FSLTL update the full-size textures are back. Just run the tool again. The tool tells you when an old backup from before the update is left over and offers to delete it.

Something isn't working? Click "Advanced" and then "Copy diagnostics", and paste the text into your comment on Flightsim.to or into a GitHub issue.

## How it works

* **Textures with mipmaps:** the top mip levels are removed and the DDS header is adjusted. No recompression and no quality loss.
* **Textures without mipmaps:** decoded, resized with a box filter and recompressed in their original format with a full mipmap chain ([BCnEncoder.Net](https://github.com/Nominom/BCnEncoder.NET)).
* Textures are only ever made smaller. The aspect ratio and the format stay the same.
* In `layout.json` only the `size` and `date` entries of the changed textures are updated. Everything else stays byte-identical.
* Target sizes: 2048, 1024 or 512.

## Safety

* Under "Advanced options" you choose how the originals are kept: **Folder** (fast, takes as much space as the originals), **ZIP** (compressed after the run, FSLTL textures shrink to about a fifth, every file is checked against its original before the folder is removed) or **No backup** (originals are deleted, "Restore original" isn't possible). An existing folder backup can be compressed later with "Compress to ZIP".
* With a folder or ZIP backup, every original file is moved to `FSLTL-Downscaler-Backup` next to your Community folder before anything is replaced. If FSLTL is linked into the Community folder, the backup is created on the same drive as the real FSLTL folder. If you move the FSLTL folder later, the tool still finds its backup. Disk space is only freed once you delete the backup with the "Delete backup" button. After that, "Restore original" is no longer possible.
* No internet access, no admin rights, no telemetry.
* Windows 10 or 11, 64 bit. No installation needed.
* The program isn't code-signed yet. If Windows SmartScreen appears, click "More info" and then "Run anyway". You can check the file against the SHA256 checksum on the download page.

## Building from source

Requires the .NET 10 SDK.

```powershell
powershell -ExecutionPolicy Bypass -File .\build-release.ps1
```

The script creates the EXE, the release ZIP and the SHA256 checksums in the `release` folder. Local builds aren't signed.

Official releases are built by GitHub Actions (`.github/workflows/release.yml`) from this repository.

| Project | Purpose |
|---|---|
| `src/FsltlDownscaler.Core` | Texture processing, layout.json and manifest.json, backup and restore |
| `src/FsltlDownscaler.Gui` | Windows Forms user interface |
| `src/FsltlDownscaler.Cli` | Command line version for testing |

## Code signing policy

FSLTL Downscaler is applying to the SignPath Foundation open source program. Until the application is approved, all releases are unsigned.

Once approved: Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

* Authors and reviewers: [lukas-em](https://github.com/lukas-em)
* Approvers: [lukas-em](https://github.com/lukas-em)

Only binaries built by GitHub Actions from this repository will be signed. Every signing request will be approved by hand.

**Privacy:** This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

## Made with AI

The program code, the description and the preview graphics were created with the help of AI (Claude by Anthropic). The idea, all testing in MSFS 2024 and the in-sim screenshots are my own. The tool contains no AI-generated textures.

## Disclaimer

Use at your own risk. This tool changes files of your FSLTL installation. It keeps a backup of every original file, but it comes without any warranty.

FSLTL Downscaler isn't affiliated with FSLTL and doesn't contain or distribute any FSLTL files.

## License

[MIT](LICENSE) · by [FranklinFjM](https://flightsim.to/profile/FranklinFjM)
