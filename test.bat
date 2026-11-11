@echo off
setlocal
cd /d "%~dp0"

set TARGET_ARCH=%1
if "%TARGET_ARCH%"=="" set TARGET_ARCH=x64

if /i "%TARGET_ARCH%"=="all" goto test_all
if /i "%TARGET_ARCH%"=="both" goto test_all

call :run_test %TARGET_ARCH%
endlocal & exit /b %ERRORLEVEL%

:test_all
call :run_test x64
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] x64 test failed!
    endlocal & exit /b %ERRORLEVEL%
)
call :run_test x86
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] x86 test failed!
    endlocal & exit /b %ERRORLEVEL%
)
endlocal & exit /b 0

:run_test
set ARCH=%1
echo ==========================================================
echo [wb Test] Running automated test suite for %ARCH%...
echo ==========================================================

if /i "%ARCH%"=="x86" (
    set CSC_DIR=C:\Windows\Microsoft.NET\Framework\v4.0.30319
) else (
    set CSC_DIR=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
    if not exist "%CSC_DIR%\csc.exe" set CSC_DIR=C:\Windows\Microsoft.NET\Framework\v4.0.30319
)
set CSC=%CSC_DIR%\csc.exe

if not exist "%CSC%" (
    echo [ERROR] Could not locate built-in csc.exe.
    exit /b 1
)

if not exist "bin\tests" mkdir "bin\tests"
set CEF_DLL=packages\CefSharp.H264.%ARCH%\CefSharp.dll
if not exist "%CEF_DLL%" set CEF_DLL=packages\CefSharp.H264.x64\CefSharp.dll
if not exist "%CEF_DLL%" set CEF_DLL=packages\CefSharp.Common\lib\net462\CefSharp.dll
set CEF_CORE_DLL=packages\CefSharp.H264.%ARCH%\CefSharp.Core.dll
if not exist "%CEF_CORE_DLL%" set CEF_CORE_DLL=packages\CefSharp.H264.x64\CefSharp.Core.dll
set CEF_WINFORMS_DLL=packages\CefSharp.H264.%ARCH%\CefSharp.WinForms.dll
if not exist "%CEF_WINFORMS_DLL%" set CEF_WINFORMS_DLL=packages\CefSharp.H264.x64\CefSharp.WinForms.dll

if exist "%CEF_DLL%" (
    copy /y "%CEF_DLL%" "bin\tests\CefSharp.dll" >nul
)
if exist "%CEF_CORE_DLL%" (
    copy /y "%CEF_CORE_DLL%" "bin\tests\CefSharp.Core.dll" >nul
)
if exist "%CEF_WINFORMS_DLL%" (
    copy /y "%CEF_WINFORMS_DLL%" "bin\tests\CefSharp.WinForms.dll" >nul
)

"%CSC%" /nologo /t:exe /platform:%ARCH% /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Xaml.dll /r:"%CSC_DIR%\WPF\PresentationCore.dll" /r:"%CSC_DIR%\WPF\WindowsBase.dll" /r:"%CEF_DLL%" /r:"%CEF_CORE_DLL%" /r:"%CEF_WINFORMS_DLL%" /out:bin\tests\TestRunner_%ARCH%.exe ^
    src\Core\AppServices.cs src\Core\AppStrings.cs src\Core\ClipboardHelper.cs src\Core\CompressedStringHelper.cs src\Core\CrashpadHelper.cs src\Core\LogcatHelper.cs src\Core\JsHelper.cs src\Core\MemoryUtils.cs src\Core\SortMode.cs src\Core\StorageHelper.cs src\Core\SqliteDatabase.cs src\Core\TitleHelper.cs src\Core\UrlHelper.cs src\Core\WebDragDropHelper.cs ^
    src\AdBlock\*.cs ^
    src\SponsorBlock\*.cs ^
    src\Bookmarks\*.cs ^
    src\DataPortability\*.cs ^
    src\Downloads\DownloadItemModel.cs src\Downloads\DownloadManager.cs src\Downloads\DownloadBubblePopup.cs src\Downloads\DownloadsView.cs ^
    src\History\HistoryItem.cs src\History\HistoryManager.cs src\History\HistoryView.cs ^
    src\Browser\ChromeTabControl.cs src\Browser\FaviconManager.cs src\Browser\InternalPageHelper.cs src\Browser\PasskeyFallbackHelper.cs src\Browser\DarkFallbackHelper.cs src\Browser\ShortcutItem.cs src\Browser\ShortcutManager.cs src\Browser\AddShortcutDialog.cs src\Browser\NewTabView.cs src\Browser\IncognitoNewTabView.cs src\Browser\SilhouetteManager.cs src\Browser\SiteMuteManager.cs src\Browser\SourceViewerTemplate.cs src\Browser\WebErrorPage.cs ^
    src\Browser\Suggestions\*.cs ^
    src\Browser\ClearBrowsingData\*.cs ^
    src\Proxy\*.cs ^
    src\Browser\Handlers\LifeSpanHandler.cs ^
    src\UI\*.cs ^
    src\Screenshot\ScreenshotSession.cs src\Screenshot\ScreenshotSessionManager.cs src\Screenshot\ScreenshotAnnotations.cs src\Screenshot\ScreenshotSelectionOverlayForm.cs src\Screenshot\ScreenshotCountdownOverlay.cs src\Screenshot\ScreenshotView.cs src\Screenshot\CdpScreenshotHelper.cs ^
    tests\WbTests.cs

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Test compilation failed for %ARCH% with exit code %ERRORLEVEL%.
    exit /b %ERRORLEVEL%
)

bin\tests\TestRunner_%ARCH%.exe
exit /b %ERRORLEVEL%
