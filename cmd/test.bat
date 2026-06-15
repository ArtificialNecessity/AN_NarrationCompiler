@echo off
REM Test render-one against a single Game_Alpha Ivo chapter

set CHAPTER=C:\PROJECTS\_nm42\Game_Alpha\PLAYERS\Player_01_Ivo\CHAPTERS\Chapter_01_The_Silk_Note.md
set OUTPUT_DIR=C:\PROJECTS\_nm42\Game_Alpha\PLAYERS\Player_01_Ivo\CHAPTERS_AUDIO

echo ============================================
echo  NarrationCompiler - render-one test
echo ============================================
echo.
echo Chapter: %CHAPTER%
echo Output:  %OUTPUT_DIR%
echo.

pushd %~dp0..\src\NarrationCompiler
dotnet run -- render-one "%CHAPTER%" --output-dir "%OUTPUT_DIR%"
popd