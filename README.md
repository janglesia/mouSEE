# mouSEE

# Eye Tracking Mouse – Development Environment Setup

This guide explains how to set up the development environment and install the dependencies required to work on the **Eye Tracking Mouse** project.

## Prerequisites

The project uses the following tools and libraries:

- Git
- OpenCV
- MediaPipe
- scikit-learn
- SQLite
- Visual Studio
- .NET / C#

---

## 1. Install Git

Download Git for Windows from:

https://git-scm.com/install/windows

1. Click **"Click here to download"**.
2. Run the downloaded installer.
3. Follow the installation instructions.
4. After installation, open **Git Bash**.

Verify that Git was installed successfully:

```bash
git -v
```

You should see the installed Git version displayed.

Example:

```text
git version 2.x.x.windows.x
```

---

## 2. Install OpenCV

OpenCV can be installed differently depending on whether you are using **Python** or **C++**.

### Python

Install OpenCV using pip:

```bash
pip install opencv-python
```

### C++

Go to the OpenCV releases page:

https://opencv.org/releases/

1. Find the latest OpenCV release.
2. Select the **Windows** download option.
3. Open the downloaded file.
4. You will be prompted to select an extraction location.
5. Extract the files to:

```text
C:\
```

This should create an OpenCV directory similar to:

```text
C:\opencv
```

> **Note:** You can use a different installation location, but you will need to modify the environment variable path accordingly.

### Add OpenCV to the Windows PATH

After extracting OpenCV:

1. Open the Windows Start menu.
2. Search for **"Edit the system environment variables"**.
3. Open the Control Panel result.
4. Click **Environment Variables**.
5. Under **System variables**, select `Path`.
6. Click **Edit**.
7. Click **New**.
8. Add:

```text
C:\opencv\build\x64\vc16\bin
```

If OpenCV was extracted somewhere else, modify this path to match your installation location.

9. Click **OK** to save the changes.
10. Restart your computer to apply the changes.

---

## 3. Install MediaPipe

Install MediaPipe for Python with:

```bash
python -m pip install mediapipe
```

---

## 4. Install scikit-learn

Install scikit-learn using pip:

```bash
pip install scikit-learn
```

Verify that the installation was successful:

```bash
python -m pip show scikit-learn
```

If installed correctly, the command should display information about your installed version of scikit-learn.

---

## 5. Install SQLite

Download the SQLite client from the Microsoft Store:

https://apps.microsoft.com/detail/9nlnlnk84x6w

1. Click **Download**.
2. Open the SQLite client installer.
3. Follow the installation instructions.
4. Open the application to verify that it was installed.

### Verify SQLite from Git Bash

Open Git Bash and run:

```bash
sqlite3
```

If SQLite is installed correctly, the SQLite command-line interface should open.

To exit SQLite, run:

```text
.exit
```

---

## 6. Install Visual Studio and .NET/C#

Download **Visual Studio Community** from:

https://visualstudio.microsoft.com/free-developer-offers/

1. Find **Visual Studio Community**.
2. Click **Free Download**.
3. Open the Visual Studio Installer.
4. Continue through the installation process until you reach the **Workloads** screen.

Under:

**Workloads → Desktop & Mobile**

select:

```text
.NET desktop development
```

Click **Install** or **Modify** to install the required components.

The installation may take several minutes.

Once installation is complete, restart your computer to finalize the changes.

---

## 7. Verify Your Development Environment

After completing the installation, verify the main command-line tools.

### Git

```bash
git -v
```

### OpenCV for Python

```bash
python -c "import cv2; print(cv2.__version__)"
```

### MediaPipe

```bash
python -c "import mediapipe; print(mediapipe.__version__)"
```

### scikit-learn

```bash
python -m pip show scikit-learn
```

### SQLite

```bash
sqlite3
```

If each command runs successfully, your development environment should be ready.

---

## Development Environment Summary

| Component | Purpose | Installation |
|---|---|---|
| **Git** | Version control | Git for Windows |
| **OpenCV** | Computer vision and image processing | `pip install opencv-python` or Windows C++ release |
| **MediaPipe** | Computer vision / tracking | `python -m pip install mediapipe` |
| **scikit-learn** | Machine learning | `pip install scikit-learn` |
| **SQLite** | Local database | SQLite Client |
| **Visual Studio** | C#/.NET development environment | Visual Studio Community |
| **.NET Desktop Development** | Desktop UI development | Visual Studio workload |

---

## Troubleshooting

### Command Not Found

If commands such as `git` or `sqlite3` are not recognized:

1. Confirm that the program was installed successfully.
2. Check that the program's installation directory was added to the Windows `PATH`.
3. Close and reopen Git Bash or your terminal.
4. Restart your computer if necessary.

### OpenCV C++ PATH Issues

Make sure the following directory, or the equivalent directory for your OpenCV installation, is included in your system `PATH`:

```text
C:\opencv\build\x64\vc16\bin
```

If you installed OpenCV somewhere else, update the path accordingly.

---

## Getting Started

Once all dependencies have been installed, clone the project repository:

```bash
git clone https://github.com/janglesia/mouSEE.git
```

Navigate into the project directory:

```bash
cd mouSEE
```

You are now ready to begin development on the **Eye Tracking Mouse** project.
