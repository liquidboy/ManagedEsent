setlocal
set version=%1
if %version%.==. goto :usage

@rem ==============
@rem Workflow:
@rem
@rem createbinaries.bat
@rem  -Compiles binaries
@rem  -Copies binaries to tosign-version
@rem
@rem Manual
@rem  -Sign the binaries (DLLs only!) using https://codesign.gtm.microsoft.com/
@rem  -Copy the signed DLLs to signed-version
@rem
@rem processsignedbinaries.bat
@rem  -Copies (PDBs from tosign, and DLLs from signed) to tozip-version and nuget-version.
@rem  -Zips up the files in tozip-version. (requirement: zip.exe)
@rem
@rem Manual
@rem  -Upload the zip file
@rem  -run `nuget push`
@rem ==============


@set msbuildpath="%ProgramFiles(x86)%\MSBuild\12.0\Bin\MSBuild.exe"
@if not exist %msbuildpath% set msbuildpath=%windir%\microsoft.net\framework\v4.0.30319\msbuild.exe
@rem verbosity=minimal;Summary would be better, but
set msbuildexe=%msbuildpath% /nologo /property:Configuration=Release

@echo =-=-=-=-=-=-=-=
@echo Compiling... (http://xkcd.com/303/)

%msbuildexe% ..\EsentInterop.standard\EsentInterop.Standard.csproj
if errorlevel 1 goto :eof

@rem %msbuildexe% ..\EsentInterop\EsentInteropMetro.csproj
@rem if errorlevel 1 goto :eof

%msbuildexe% ..\EsentCollections.Standard\EsentCollections.Standard.csproj
if errorlevel 1 goto :eof

%msbuildexe% ..\Esent.Isam.Standard\Esent.Isam.Standard.csproj
if errorlevel 1 goto :eof

@echo =-=-=-=-=-=-=-=
@echo Copying output files to staging area...
set dest=%~dp0tosign-%version%

for %%i in ( esent.collections.standard.dll esent.collections.standard.pdb esent.collections.standard.xml ) do (
  xcopy /d ..\EsentCollections.Standard\bin\release\%%i %dest%\
)

for %%i in ( esent.interop.standard.dll esent.interop.standard.pdb esent.interop.standard.wsa.dll esent.interop.standard.wsa.pdb esent.interop.standard.xml esent.interop.standard.wsa.xml ) do (
  xcopy /d ..\EsentInterop.Standard\bin\release\%%i %dest%\
)

for %%i in ( esent.isam.standard.dll esent.isam.standard.pdb esent.isam.standard.xml ) do (
  xcopy /d ..\Esent.Isam.Standard\bin\release\%%i %dest%\
)

for %%i in ( esedb.py esedbshelve.py ) do (
  xcopy /d ..\esedb\%%i %dest%\
)

@echo =-=-=-=-=-=-=-=
@echo.
@echo The next step is to sign the binaries (DLLs only!) using https://codesign.gtm.microsoft.com/
@echo The source dir for the signing is %dest%
@echo Use both Strong Naming (72) and an Authenticode certificate (10006).
@echo DisplayName =
@echo ManagedEsent-%version%
@echo URL =
@echo http://managedesent.codeplex.com
@echo.
@echo Wait for the mail, and then copy the files to %~dp0signed-%version%
@echo.
@echo And then run processsignedbinaries.bat %version%
@echo.
@goto :eof

:usage
@echo Usage: %0 [version-number]
@echo   e.g. %0 1.8.1
