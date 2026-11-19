FSLTL Downscaler
by FranklinFjM
https://flightsim.to/profile/FranklinFjM

WHAT IT DOES
Reduces the texture size of FSLTL Traffic Base for MSFS 2020 and MSFS 2024 and
updates layout.json and manifest.json, so MSFS loads the package without any
further steps.
Example: FSLTL 1.7.0 at 1024 px shrinks from 55.9 GB to 5.9 GB.
Less VRAM use, more FPS at busy airports. The traffic itself stays the same.

HOW TO USE
1. Close MSFS.
2. Unzip this archive to any folder, for example your Desktop.
   Don't put it into your Community folder.
3. Double-click FSLTL-Downscaler.exe.
4. The tool finds FSLTL for MSFS 2020 and MSFS 2024 automatically. If you
   have both, click "Change" to switch. If FSLTL isn't found, click "Change"
   and select the fsltl-traffic-base folder inside your Community folder.
   Linked folders (e.g. Addon Linker) are supported.
5. Choose a texture size and click "Downscale".

WINDOWS SMARTSCREEN
The program isn't code-signed yet. Windows may show "Windows protected your PC".
Click "More info" and then "Run anyway". You can check the file against the
SHA256 checksum on the download page.

BACKUP AND DISK SPACE
The original textures are moved to a folder called "FSLTL-Downscaler-Backup"
next to your Community folder. If FSLTL is linked into the Community folder,
the backup is created on the same drive as the real FSLTL folder. Disk space is
only freed once you delete that folder. Keep it until you're sure everything
works in MSFS.

UNDO
Click "Restore original" to put the original textures back.

AFTER AN FSLTL UPDATE
The FSLTL installer brings back the full-size textures. Run the tool again.
An old backup from before the update can then be deleted.

UNINSTALL
Delete FSLTL-Downscaler.exe. Settings are stored in %APPDATA%\FSLTL-Downscaler.

REQUIREMENTS
Windows 10 or 11, 64 bit. No installation, no admin rights, no internet needed.

USE AT YOUR OWN RISK
This tool changes files of your FSLTL installation. It keeps a backup of every
original file, but it comes without any warranty. See LICENSE.txt.

MADE WITH AI
This tool was written with the help of AI (Claude by Anthropic) and tested by
the author.

FSLTL Downscaler isn't affiliated with FSLTL and doesn't contain or distribute
any FSLTL files.
