@echo off
REM Test render-one with IndexTTS2 via FAL against a Reina chapter

set CHAPTER=C:\PROJECTS\_nm42\Game_SamaZorel\PLAYERS\Player_04_Reina_at_KMR\CHAPTERS\Chapter_01_Deep_Ocean_Vents_FULLPROSE.md
set VOICE_REF=%~dp0..\Assets\speech_reference\jacqueline.wav
set OUTPUT_DIR=C:\PROJECTS\_nm42\Game_SamaZorel\PLAYERS\Player_04_Reina_at_KMR\CHAPTERS_AUDIO

echo ============================================
echo  NarrationCompiler - IndexTTS2 via FAL test
echo ============================================
echo.
echo Chapter:  %CHAPTER%
echo Voice:    %VOICE_REF%
echo Output:   %OUTPUT_DIR%
echo Provider: indextts2-fal
echo.

if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

pushd %~dp0..\src\NarrationCompiler
dotnet run -- render-one "%CHAPTER%" --provider indextts2-fal --voice-id "%VOICE_REF%" --output-dir "%OUTPUT_DIR%"
popd