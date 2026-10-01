# mouSEE development environment setup

How to set up a Windows machine to work on mouSEE. Once you're done, go back to the [README](../README.md#testing) to test it.

## Prerequisites

| Tool | What it's for |
|------|---------------|
| Windows 10/11 and a webcam | Running the program |
| Git | Version control, and Git Bash as a terminal |
| Python 3.12 | Running the tracker |
| .NET SDK 10 | Building and running the C# app |
| MSYS2 (for `make`) | Running the shortcuts in the Makefile |
| VS Code or Visual Studio | Editing the code |

OpenCV, MediaPipe and scikit-learn are Python packages. You don't install them separately, `make setup` installs them into the project's own Python environment (see step 6).

The commands in this guide are for Git Bash. In VS Code you can make it the default terminal: open the terminal dropdown (the arrow next to the `+`), choose **Select Default Profile** and pick **Git Bash**.

## 1. Install Git

1. Download Git for Windows from https://git-scm.com/install/windows
2. Run the installer and go through the steps (the defaults are fine).
3. Open Git Bash and check it worked:

   ```bash
   git -v
   ```

   You should see something like `git version 2.x.x.windows.x`.

## 2. Install Python 3.12

1. Go to https://www.python.org/downloads/windows/ and download the Windows installer (64-bit) for Python 3.12.10 (last 3.12 version that has an installer). Don't get 3.13 or newer, MediaPipe might not work on it.
2. Run the installer. **Tick "Add python.exe to PATH"** at the bottom of the first screen, then click Install Now.
3. Check it worked (in a new terminal):

   ```bash
   py -3.12 --version
   ```

   You should see `Python 3.12.10`.

## 3. Install the .NET SDK

1. Download the .NET 10 SDK from https://dotnet.microsoft.com/download (pick the SDK, not just the Runtime).
2. Run the installer.
3. Check it worked:

   ```bash
   dotnet --list-sdks
   ```

   You should see a line starting with `10.`.

Visual Studio Community (https://visualstudio.microsoft.com/free-developer-offers/) with the .NET desktop development workload is optional. VS Code + the SDK is enough for now, VS might be nicer once we start on the UI. If you install it, still check `dotnet --list-sdks` shows version 10.

## 4. Install make (through MSYS2)

1. Download the installer from https://www.msys2.org/ and run it. Keep the default install folder, `C:\msys64`.
2. From the Start menu, open **MSYS2 MSYS**.
3. Update MSYS2:

   ```bash
   pacman -Syu
   ```

   Answer `Y` when asked. If the window closes during the update, open **MSYS2 MSYS** again and run `pacman -Syu` once more until it says there's nothing to do.
4. Install make:

   ```bash
   pacman -S make
   ```

5. Add MSYS2's tools to your PATH so `make` works in Git Bash and VS Code:
   1. Open the Start menu and search for **"Edit environment variables for your account"**.
   2. Under **User variables**, select **Path** and click **Edit**.
   3. Click **New** and add `C:\msys64\usr\bin`
   4. Move it to the bottom of the list with **Move Down** (otherwise MSYS2 versions of some commands like `find` override the Windows ones).
   5. Click OK on both windows.
6. Fully close and reopen VS Code / Git Bash, then check:

   ```bash
   make --version
   ```

   You should see `GNU Make 4.x`.

## 5. Get the code

```bash
git clone https://github.com/janglesia/mouSEE.git
cd mouSEE
```

## 6. Set up the project

From the project folder:

```bash
make setup
```

This:
1. creates a Python environment in `tracker/.venv` (using Python 3.12),
2. installs the Python packages from `tracker/requirements.txt` into it (MediaPipe, OpenCV, scikit-learn),
3. downloads the face model to `tracker/face_landmarker.task`,
4. builds the C# app.

It takes a few minutes the first time. Running it again only redoes what's missing or out of date.

## 7. Check everything

```bash
git -v
py -3.12 --version
dotnet --list-sdks
make --version
tracker/.venv/Scripts/python -c "import cv2, mediapipe, sklearn; print('OpenCV', cv2.__version__, '/ MediaPipe', mediapipe.__version__, '/ scikit-learn', sklearn.__version__)"
```

If those all print versions, setup is done.

## Troubleshooting

- **Command not found** (`git`, `py`, `dotnet`, `make`): close and reopen VS Code / Git Bash after installing. If it still fails, check the program's folder is in your PATH (for make, that's `C:\msys64\usr\bin`, see step 4 above). Restart the computer if needed.
- **`py -3.12` says no such version**: Python 3.12 isn't installed, see step 2 above.
- **pip fails installing mediapipe**: Python version is probably too new. Use 3.12.

For problems running the program, see [Troubleshooting in the README](../README.md#troubleshooting).

## Summary

| Component | Purpose | Installation |
|-----------|---------|--------------|
| Git | Version control, Git Bash terminal | [Git for Windows](https://git-scm.com/install/windows) |
| Python 3.12 | Runs the tracker | [python.org](https://www.python.org/downloads/windows/) (3.12.10 installer) |
| OpenCV | Reads the webcam | `make setup` (Python package) |
| MediaPipe | Face and eye tracking | `make setup` (Python package) |
| scikit-learn | Machine learning (planned, for calibration) | `make setup` (Python package) |
| .NET SDK 10 | Builds and runs the C# app | [dotnet.microsoft.com](https://dotnet.microsoft.com/download) |
| make | Project shortcuts | MSYS2, `pacman -S make` |
| Visual Studio (optional) | Full C# IDE, handy once the app has a UI | [Visual Studio Community](https://visualstudio.microsoft.com/free-developer-offers/) |
