<p align="center">
  <img src="assets/icons/wb_logo.png" alt="wb logo" width="150" />
</p>

# wb browser

[![Build](https://img.shields.io/badge/build-passing-brightgreen.svg)](#getting-started)
[![Tests](https://img.shields.io/badge/tests-226%20passed-success.svg)](#running-tests)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%2F%2011%20%7C%20x64%20%7C%20x86-0078D6.svg)](#-prerequisites)
[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.6.2%2B-512BD4.svg)](#-prerequisites)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A modern, lightweight web browser for Windows built on Chromium/CEF with H.264/AAC media playback, built-in ad blocking, YouTube SponsorBlock, dynamic accent themer, Screenshot Studio, and standalone zero-dependency installer packages.

## Getting Started

### 1. Clone the Repository

```cmd
git clone https://github.com/ikmalsaid/wb.git
cd wb
```

### 2. One-Click Build

Run `build.bat` from Command Prompt or double-click it in File Explorer:

```cmd
build.bat
```

By default, `build.bat` builds standalone Setup Wizard installer packages for both x86 and x64 architectures:
- **x64 Installer**: `bin\wb-setup-x64.exe`
- **x86 Installer**: `bin\wb-setup-x86.exe`

*(Note: Installers are compiled directly to `bin\` without cluttering normal build folders. To build unpackaged browser binaries only, use `build.bat -BrowserOnly` or `build.bat -NoInstaller`)*.

### 3. Launch the Browser

Run either executable directly or build and launch in one step:

```cmd
build.bat -Run
```

Or target a specific architecture or options:

```cmd
build.bat x64              :: Builds 64-bit installer
build.bat x86              :: Builds 32-bit installer
build.bat -BrowserOnly     :: Builds browser binaries only (w/o installer)
build.bat x64 -Run         :: Builds and immediately launches 64-bit wb
```

### 4. Standalone Setup Wizard

Package the entire browser into a self-extracting, modern dark-themed Setup Wizard (`wb-setup-x64.exe` / `wb-setup-x86.exe`):

The resulting installer is a single, zero-dependency executable that features:
- **1-Click Quick Setup** (installs to `%LOCALAPPDATA%\Programs\wb` with no UAC prompt needed)
- **Expandable Options** (custom install location, Desktop & Start Menu shortcuts)
- **Automatic File Associations** (`.html`, `.htm`, `.shtml`, `.xhtml`, `.svg`, `.webp`)
- **Windows Default Browser Registration** (`http` and `https` protocols)
- **Built-in Uninstaller** (`uninstall.exe`) integrated into Windows Settings / Installed Apps
- **Silent Deployment** (`wb-setup-x64.exe /silent`)

### Running Tests

Execute the automated test suite across all 223 verification checks:

```cmd
test.bat             :: Runs x64 tests
test.bat all         :: Runs tests on both x64 and x86
test.bat x86         :: Runs x86 tests
```

## Essential Shortcuts

| Shortcut | Action |
| :--- | :--- |
| **Ctrl + T** | New tab |
| **Ctrl + W** | Close current tab |
| **Ctrl + Shift + T** | Reopen closed tab |
| **Ctrl + Shift + N** | New Incognito window |
| **Ctrl + D** | Bookmark current page |
| **Ctrl + B** | Bookmarks manager |
| **Ctrl + H** | Browsing history |
| **Ctrl + J** | Downloads manager |
| **Ctrl + L** / **Alt + D** | Select address bar |
| **Ctrl + R** / **F5** | Reload page |
| **Ctrl + Shift + R** | Hard reload (bypass cache) |
| **Ctrl + F** | Find in page |
| **F12** | Developer tools |
| **Ctrl + Shift + D** | Toggle Debug Console (logcat) |
| **Ctrl + U** | View page source in tab |
| **Ctrl + P** | Save / Print page to PDF |
| **Alt + Left** / **Right** | Back / Forward |


## 📋 Prerequisites

- **Windows 10 or 11** (64-bit or 32-bit).
- **.NET Framework 4.6.2+**.
- **Windows Built-in C# Compiler** (comes standard with .NET Framework; no Visual Studio or SDK required).


## 📄 License

wb is open-source under the [MIT License](LICENSE).
