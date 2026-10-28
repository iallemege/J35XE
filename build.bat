@echo off
setlocal EnableExtensions
rem J-35XE runtime (jsonKey SaabJ35). All J-35 code goes here; BIA.Runtime is frozen at 1.0.29.
set "GAME=d:\Steam\steamapps\common\Nuclear Option"
set MANAGED=%GAME%\NuclearOption_Data\Managed
set BEP=%GAME%\BepInEx\core
set PLUGINS=%GAME%\BepInEx\plugins
set VER=2.6.62
set OUT=%~dp0J35XE.dll
set NAMED=J35XE_%VER%.dll
set CSC=%~dp0..\_csc9.cmd

if not exist "%CSC%" (
  echo csc.exe not found
  exit /b 1
)

call "%CSC%" /noconfig /nostdlib /nologo /optimize+ /target:library /platform:anycpu /langversion:10 ^
  /out:"%OUT%" ^
  /r:"%MANAGED%\mscorlib.dll" ^
  /r:"%MANAGED%\netstandard.dll" ^
  /r:"%MANAGED%\System.dll" ^
  /r:"%MANAGED%\System.Core.dll" ^
  /r:"%BEP%\BepInEx.dll" ^
  /r:"%BEP%\0Harmony.dll" ^
  /r:"%MANAGED%\Assembly-CSharp.dll" ^
  /r:"%MANAGED%\Mirage.dll" ^
  /r:"%MANAGED%\UnityEngine.CoreModule.dll" ^
  /r:"%MANAGED%\UnityEngine.PhysicsModule.dll" ^
  /r:"%MANAGED%\UnityEngine.ParticleSystemModule.dll" ^
  /r:"%MANAGED%\UnityEngine.InputLegacyModule.dll" ^
  /r:"%MANAGED%\UnityEngine.AudioModule.dll" ^
  /r:"%MANAGED%\UnityEngine.UnityWebRequestModule.dll" ^
  /r:"%MANAGED%\UnityEngine.UnityWebRequestAudioModule.dll" ^
  /r:"%MANAGED%\UnityEngine.IMGUIModule.dll" ^
  /r:"%MANAGED%\UnityEngine.TextRenderingModule.dll" ^
  /r:"%MANAGED%\UnityEngine.UI.dll" ^
  /r:"%MANAGED%\UnityEngine.UIModule.dll" ^
  /r:"%MANAGED%\Unity.RenderPipelines.Core.Runtime.dll" ^
  /r:"%MANAGED%\Unity.RenderPipelines.Universal.Runtime.dll" ^
  /r:"%MANAGED%\UniTask.dll" ^
  /r:"%MANAGED%\UnityEngine.ImageConversionModule.dll" ^
  /r:"%MANAGED%\UnityEngine.AssetBundleModule.dll" ^
  /r:"%MANAGED%\com.rlabrecque.steamworks.net.dll" ^
  /r:"%MANAGED%\UnityEngine.dll" ^
  "%~dp0..\OritasyShared\SsBag.cs" ^
  "%~dp0src\Plugin.cs" ^
  "%~dp0src\PitchTune.cs" ^
  "%~dp0src\Stores.cs" ^
  "%~dp0src\DrakenArms.cs" ^
  "%~dp0src\AiFly.cs" ^
  "%~dp0src\RbMesh.cs" ^
  "%~dp0src\Sgloc.cs" ^
  "%~dp0src\WingVapor.cs" ^
  "%~dp0src\Eject.cs" ^
  "%~dp0src\SelfCollision.cs" ^
  "%~dp0src\GroundTrack.cs" ^
  "%~dp0src\Jato.cs" ^
  "%~dp0src\Cosmetics.cs" ^
  "%~dp0src\Rig.cs" ^
  "%~dp0src\Pix.cs" ^
  "%~dp0src\Planform.cs" ^
  "%~dp0src\RoundMfd.cs" ^
  "%~dp0src\GuiDraw.cs" ^
  "%~dp0src\CoffinMarks.cs" ^
  "%~dp0src\Ohmd.cs" ^
  "%~dp0src\ThrottleTape.cs" ^
  "%~dp0src\HudHide.cs" ^
  "%~dp0src\Durability.cs" ^
  "%~dp0src\CockpitCam.cs" ^
  "%~dp0src\CockpitEdit.cs" ^
  "%~dp0src\Ost.cs" ^
  "%~dp0src\SkinDrop.cs" ^
  "%~dp0src\SkinList.cs" ^
  "%~dp0src\NetFix.cs" ^
  "%~dp0src\LoadPic.cs"

if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)

for %%F in ("%PLUGINS%\J35XE*.dll") do del /f /q "%%F" 2>nul
copy /Y "%OUT%" "%PLUGINS%\%NAMED%"
if errorlevel 1 (
  echo INSTALL FAILED - close the game and rebuild
  exit /b 1
)
set "STAGE=%~dp0..\..\_cache\j35xe_bag"
if exist "%STAGE%" rmdir /S /Q "%STAGE%"
mkdir "%STAGE%\load"
copy /Y "%~dp0load\*.jpg" "%STAGE%\load\" >nul
copy /Y "%~dp0load\*.png" "%STAGE%\load\" >nul
mkdir "%STAGE%\models"
copy /Y "%~dp0models\Rb_24.obj" "%STAGE%\models\" >nul
copy /Y "%~dp0models\Rb_24.png" "%STAGE%\models\" >nul
copy /Y "%~dp0models\Rb_28.obj" "%STAGE%\models\" >nul
copy /Y "%~dp0models\Rb_28.png" "%STAGE%\models\" >nul
copy /Y "%~dp0models\placeholder.png" "%STAGE%\models\" >nul
powershell -NoProfile -File "%~dp0..\OritasyShared\PackBag.ps1" -BagDir "%STAGE%" -OutFile "%PLUGINS%\J35XE.ssbag"
if errorlevel 1 (
  echo SSBAG PACK FAILED
  exit /b 1
)
if exist "%PLUGINS%\J35XE\load" rmdir /S /Q "%PLUGINS%\J35XE\load"
if not exist "%PLUGINS%\J35XE\Skins" mkdir "%PLUGINS%\J35XE\Skins"
echo Installed: %PLUGINS%\%NAMED% + J35XE.ssbag
endlocal
