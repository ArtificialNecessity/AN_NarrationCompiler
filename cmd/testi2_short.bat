@echo off
REM Quick test: ONE short sentence through IndexTTS2 via FAL
REM Uses the keystore normally (password prompt), just a tiny input to debug the API call

set CHAPTER=%~dp0test_chapter_short.md
set VOICE_REF=C:\PROJECTS\ASTRO-VIDEO\Assets\speech_reference\Optimistic_female_medium_register_actress.wav
set OUTPUT_DIR=%~dp0..\test_output

echo ============================================
echo  IndexTTS2 via FAL - SHORT sentence test
echo ============================================
echo.
echo Chapter: %CHAPTER%
echo Voice:   %VOICE_REF%
echo Output:  %OUTPUT_DIR%
echo.

if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

pushd %~dp0..\src\NarrationCompiler
dotnet run -- render-one "%CHAPTER%" --provider indextts2-fal --voice-id "%VOICE_REF%" --output-dir "%OUTPUT_DIR%"
popd

echo.
echo Done. Check %OUTPUT_DIR% for output.
pause