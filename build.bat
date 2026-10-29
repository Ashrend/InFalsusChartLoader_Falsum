@echo off
setlocal
cd /d "%~dp0"

rem Usage: build.bat [Release^|Debug] [--MelonLoader^|--BepInEx]
rem Defaults: Release and MelonLoader.
set CONFIG=Release
set LOADER=MelonLoader

:parse
if "%~1"=="" goto :parsed
set "KNOWN=0"
if /i "%~1"=="Release" ( set "KNOWN=1" & set "CONFIG=Release" )
if /i "%~1"=="Debug" ( set "KNOWN=1" & set "CONFIG=Debug" )
if /i "%~1"=="--MelonLoader" ( set "KNOWN=1" & set "LOADER=MelonLoader" )
if /i "%~1"=="--BepInEx" ( set "KNOWN=1" & set "LOADER=BepInEx" )
if "%KNOWN%"=="0" goto :badarg
shift
goto :parse

:parsed
set BUILD_CONFIG=%CONFIG%
if /i "%LOADER%"=="BepInEx" set BUILD_CONFIG=BepInEx%CONFIG%

rem Warnings are errors, and a rebuild makes the analyzers run.
set WARN=-warnaserror -t:Rebuild

rem Pause at the end unless CI is set.
set PAUSE=pause
if defined CI set PAUSE=

where dotnet >nul 2>&1 || goto :nodotnet

echo ============================================================
echo  InFalsusChartLoader_falsum - %CONFIG% build (%LOADER%)
echo  %CD%
echo ============================================================
echo.

dotnet build InFalsusChartLoader_falsum.csproj -c %BUILD_CONFIG% --nologo %WARN% -p:Loader=%LOADER%
if errorlevel 1 goto :failed

if /i "%CONFIG%"=="Release" goto :checkartifact
goto :built

:checkartifact

echo.
echo Checking that the Release build carries the function only...

rem ---------------------------------------------------------------------------
rem Release artifact check: the artifact must carry the shipping strings only.
rem ---------------------------------------------------------------------------
set "PSF=%TEMP%\InFalsusChartLoader_falsum_artifact_check_%BUILD_CONFIG%.ps1"
> "%PSF%" echo $ErrorActionPreference='Stop';
>> "%PSF%" echo $p='bin\%BUILD_CONFIG%\InFalsusChartLoader_falsum.dll';
>> "%PSF%" echo if(-not (Test-Path $p)){ Write-Host ('No artifact at '+$p); exit 1 };
>> "%PSF%" echo $b=[IO.File]::ReadAllBytes($p); $fail=0;
>> "%PSF%" echo $bad=@('charts folder: ','decoded ','streaming assets: ','interop types by name',' hooked','could not be resolved','note records measure','chart folder ','the pack list has','is not an array','has no element class','class pointer could not be read','probe: ','--- before ---','--- after ---','charts folder exists?','kept ','packToAssets    ','readings=','chart[0]','audio name ','audio read index ','is not ours','jacket asked for','jacket load claimed','read bookkeeping reached','outstanding-read sets','outstanding=','noread=','a call into the game','s rendering types raised','is missing','pack screen');
>> "%PSF%" echo $good=@('InFalsusChartLoader_falsum loading...','packs: ','offsets asked=','hooks installed','GameAssembly.dll','is already the name of a song this game');
>> "%PSF%" echo foreach($s in $bad){ $n=[Text.Encoding]::Unicode.GetBytes($s); $h=0;
>> "%PSF%" echo   for($i=0;$i -le $b.Length-$n.Length;$i++){ $ok=$true;
>> "%PSF%" echo     for($j=0;$j -lt $n.Length;$j++){ if($b[$i+$j] -ne $n[$j]){ $ok=$false; break } };
>> "%PSF%" echo     if($ok){ $h++ } };
>> "%PSF%" echo   if($h -gt 0){ Write-Host ('  FAIL  '+$s+'   found '+$h); $fail=1 }
>> "%PSF%" echo   else { Write-Host ('  ok    '+$s) } };
>> "%PSF%" echo foreach($s in $good){ $n=[Text.Encoding]::Unicode.GetBytes($s); $h=0;
>> "%PSF%" echo   for($i=0;$i -le $b.Length-$n.Length;$i++){ $ok=$true;
>> "%PSF%" echo     for($j=0;$j -lt $n.Length;$j++){ if($b[$i+$j] -ne $n[$j]){ $ok=$false; break } };
>> "%PSF%" echo     if($ok){ $h++ } };
>> "%PSF%" echo   if($h -eq 0){ Write-Host ('  FAIL  '+$s+'   missing'); $fail=1 }
>> "%PSF%" echo   else { Write-Host ('  ok    '+$s+'   '+$h) } };

rem ---------------------------------------------------------------------------
rem Every UTF-16 literal of 12 characters or more must be on the allow list.
rem ---------------------------------------------------------------------------
>> "%PSF%" echo $str=New-Object 'System.Collections.Generic.HashSet[string]';
>> "%PSF%" echo foreach($o in 0,1){ $c=New-Object System.Text.StringBuilder; for($i=$o;$i -lt $b.Length-1;$i+=2){ $n=$b[$i] + ($b[$i+1] * 256);
>> "%PSF%" echo   if($n -ge 32 -and $n -lt 127){ [void]$c.Append([char]$n) } else { if($c.Length -ge 12){ [void]$str.Add($c.ToString()) }; [void]$c.Clear() } }
>> "%PSF%" echo   if($c.Length -ge 12){ [void]$str.Add($c.ToString()) } };
>> "%PSF%" echo $allow=@(' could not be read: ',' has filter ',' hooks installed',' record(s) filed under this build''s song ids',' unresolved=',''' difficulty ',''' is already the name of a song this game ',''' was left out: ',''' was not loaded. Change the id ','^<exception could not be read: ','^<exception whose class could not be named^>','^<exception with no class^>','AddressableHandleAutoReleaser','AddressableHandleAutoReleaser._MIA could not be found; custom jackets will not be substituted onto the song list','Assembly Version','DobbyDestroy','Dictionary.KeyCollection','Dictionary.ValueCollection','FileDescription','Game.Common.dll','GameAssembly.dll','GameplayBackgrounds','InFalsusChartLoader_falsum','InFalsusChartLoader_falsum loading...','InFalsusChartLoader_falsum.dll','InternalName','LegalCopyright','MakeGenericMethod','NativeClassPtr','NativeFieldInfoPtr_','OriginalFilename','PackSongCardMember','PackVisualMemberLarge','ProductVersion','SongData could not be found; the new songs will not show','SongData._yOA could not be found; the new songs will not show','Sprites/Default','StringFileInfo','StringTypeMapping','SupportsTextureFormat','TextureFormat','UnityEngine.','UnityEngine.CoreModule','UnityEngine.CoreModule.dll','Unlit/Texture','VS_VERSION_INFO','chart loading failed: ','detour faulted, custom charts disabled: ','ifapp.Game.Common','ifapp.Game.Scenes','in its if file.','no custom song could have its audio registered','no zlib data','offsets asked=','preview_seconds','registering the custom songs failed: ','set_mainTexture','the custom songs could not be added to the game''s song list','the game''s asset manager could not be reached; no custom audio can be registered, so no custom song can be played','the game''s asset manager would not take another file','the game''s chart decoder could not be reached; no charts will load','the game''s song list refresh raised: ','SongInfo.Size','SongChartInfo.Size','PackInfo.Size','SongChartInfo','SongSelectScene','DynamicStringMapping','PackSelectSceneAssets','packToAssets','packIdTypeMapping','songIdTitleTypeMapping','songIdArtistTypeMapping','jacketIllustratorNameTypeMapping','ArtistReadingOverride','LocalizationToTitleReadingOverride','PreviewStartSeconds','PreviewEndSeconds','DisplayChartDesigner','DisplayJacketDesigner','LevelSectionIndicator',': no method by that name in this build','the custom pack could not be added; the custom songs are in the song list but have no pack of their own','the pack row accessor could not be located in this build; the custom pack will wear whatever this build puts in its row','the game''s pack row getter could not be found by name in this build; the custom pack will wear whatever this build puts in its row','SingleFileSaveDataV2','GameResultsV4','GameResultKey','GameResultKey.Size','GameResultV4','GameResultsV4.highscores','no self-test (the result record''s size could not be asked of the game)','MemoryPackSerializer','MemoryPack.dll','CreateDefault','Game.Data.dll','mscorlib.dll','ifapp.Game.Data','TryGetResult','IFCL_temp.sav','custom results: ','no save object','no container','the save method is not on the container in this build','the game''s save could not be reached; the custom songs'' scores will not be read or saved','the save folder could not be read; the custom songs'' scores will not be read or saved','the custom results file exists but could not be read, so it was left alone; the custom songs'' scores will not be read or saved','the custom results could not be prepared; the custom songs'' scores will not be read or saved: ','the custom results'' stored song ids could not be rewritten, so scores recorded under an earlier build''s ids will not be found: ','the custom results file could not be written; this session''s custom scores are not saved: ','the game''s result table could not be found; custom scores will go into the game''s own save file','IFCL self test','no self-test (the record reader could not be found)','the table failed its self-test: ','the self-test could not be run: ','EncounterResults','UpdateEncounterResult','the game''s encounter results could not be found; a custom song''s encounter result will be written into the game''s own save',': no field by that name in this build',' could not be measured off the live arrays in this build: ','the game''s song and pack tables could not be read by name in this build; no custom song will be registered','the game''s data assets could not be found by name in this build; no custom song will be registered','SongSelectScene''s payload could not be read by name in this build; which difficulty is selected will not be known, so a song with a picture per difficulty will not show one','the read bookkeeping''s fields could not be read by name in this build; a served read will not release its block','the note record''s fields could not be read by name in this build; no chart can be checked, so none will load','the note records'' stride could not be measured in this build; no chart can be checked, so none will load','the pack''s card fields could not be read by name in this build; the cards will keep the difficulty they were first built with',': no usable field by that name in this build','StringTypeMapping.Mapping could not be found on its own class in this build; the custom pack''s texts will not be written','the pack''s fields could not be read by name in this build; the custom pack keeps the game''s own visuals and text','the array holds ',' element(s) in ','there is no array to measure','SongSelectPackAssets','PackSelectLargeNonCompleted','PackSelectNonCompleted',': no class to ask in this build','the save''s difficulty could not be read by name in this build; which difficulty is selected will not be known before the song select has been visited',
>> "%PSF%" echo 'AddComponent','DontDestroyOnLoad','get_isPlaying','get_isPrepared','set_audioOutputMode','set_isLooping','set_playOnAwake','set_renderMode','set_targetTexture','RenderTexture','UnityEngine.Video','UnityEngine.VideoModule.dll','GeneralSaveState','GeneralSaveStateV5','LastSelectedDifficulty','InFalsusChartLoader_falsum background video','no custom song could be registered','the song table does not index the way this build expects ',' carries id ','); no custom song was ','registered, because their names cannot be checked against the game''s','get_mainTexture','F!G"H#I#J#K)L)','; this mod''s background substitution is off for this session','Material.get_mainTexture could not be found','Material.set_mainTexture could not be found','Material.set_mainTexture raised: ','Material''s copy constructor could not be found','no clip material is loaded','the background material could not be cloned from the game''s own: ','the clone material could not be created','the clone returned but has no native object','the game''s own material did not load','the material''s main texture could not be read','there is no material to clone','the game''s empty-chart constructor is not in this build; a chart that fails to decode cannot be served','the custom pack has no row of visuals of its own; it will wear whatever this build draws for it','the background video could not be started with the song; the background will not move with it','the background video could not be placed on the chart clock; it will run out of step with the chart','the chart''s clock could not be read by name in this build; the in-play background video will not play','_NH._UoA: no method by that name in this build; a custom song''s difficulties will follow the game''s own locks, and a fresh save may leave them locked','DifficultyUnlockContainer',': no enum member by that name in this build','the results screen''s unlock progress will keep showing for custom songs');
>> "%PSF%" echo $allow += @('imported, songs: ','registered ',': invalid call',': invalid integer ',': invalid number ',': missing value',': unknown command ','chartDesigner','difficulties','falsum asset has no file name','falsum audio not found: ','falsum chart not found: ','falsum could not be read: ','falsum difficulty entry is not an object','falsum difficulty index out of range: ','falsum duplicate difficulty index: ','falsum folder could not be read: ','falsum has no audio section','falsum has no available difficulty','falsum has no difficulties array','falsum has no song.json','falsum song.json is not an object','flick needs five values','folder does not start with [Pack]','hold needs four values','jacketDesigner','no if file and no .falsum package');
>> "%PSF%" echo $allow += @('imported, songs: ','registered ');
>> "%PSF%" echo $allow += @('pack contains no valid songs','pack file could not be read: ','pack file not found','pack folder could not be listed: ','pack has no name object','pack has no non-empty name','pack is not a JSON object','pack is not valid JSON: ','pack_cover.png','PackSelectCompleted','PackSelectCompletedHover','PackSelectLargeCompleted','PackSelectLargeCompletedHover','PackSelectLargeNonCompletedHover','PackSelectNonCompletedHover','skyarea needs a line value','skyarea needs eleven values','song.json has an empty ','song.json has no ','tap needs three values','the [Pack] folder has no usable id','unknown note type: ','unsafe songId: ');
>> "%PSF%" echo $extra=Join-Path (Get-Location) 'release-allowlist.txt'; if(Test-Path $extra){ foreach($line in Get-Content $extra){ $allow += $line } };
>> "%PSF%" echo $arr=@($str); [Array]::Sort($arr); $off=0;
>> "%PSF%" echo foreach($s in $arr){ if($allow -notcontains $s){ Write-Host ('  OFF-LIST  '+$s); $off++ } };
>> "%PSF%" echo if($off -ne 0){ Write-Host ('  '+$off+' literal(s) in the artifact are on no list - a release should carry none of them.'); $fail=1 }
>> "%PSF%" echo else { Write-Host ('  ok    all '+$str.Count+' literal(s) of 12+ characters are accounted for') };
>> "%PSF%" echo if($fail -ne 0){ Write-Host 'The Release artifact carries a diagnostic string, or is missing one it should have.'; exit 1 };
>> "%PSF%" echo Write-Host 'Passed: the Release build carries the function only.';

powershell -NoProfile -ExecutionPolicy Bypass -File "%PSF%"
if errorlevel 1 set "CHECKFAILED=1"
del "%PSF%" >nul 2>&1
if defined CHECKFAILED goto :checkfailed

:built
echo.
echo Build succeeded.
echo Output: bin\%BUILD_CONFIG%\InFalsusChartLoader_falsum.dll
echo.
echo Nothing was copied into the game. Installing the DLL is a separate,
if /i "%LOADER%"=="BepInEx" (
  echo explicit step - put it in the game's BepInEx\plugins folder yourself, with the Charts
  echo folder at the game root.
) else (
  echo explicit step - put it in the game's Mods folder yourself, with the Charts
  echo folder beside the mod folder it lands in.
)
echo.
%PAUSE%
exit /b 0

:badarg
echo ERROR: unknown argument: %~1
echo.
echo Usage: build.bat [Release^|Debug] [--MelonLoader^|--BepInEx]
echo.
%PAUSE%
exit /b 1

:nodotnet
echo ERROR: dotnet was not found on PATH.
echo.
echo Install the .NET SDK - the project targets net6.0.
echo.
%PAUSE%
exit /b 1

:failed
echo.
echo BUILD FAILED - see the errors above.
echo.
%PAUSE%
exit /b 1

:checkfailed
echo.
echo BUILD FAILED - the Release artifact carries a diagnostic string, or is
echo missing one it should have. See the FAIL lines above.
echo.
%PAUSE%
exit /b 1


