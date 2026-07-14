# Voices of the Boyne - LudoEngine System Documentation

Complete Guide for Developers & Audio Producers

## Table of Contents

1\. System Overview

2\. Architecture

3\. File Structure

4\. Startup Flow

5\. JSON Configuration

6\. Audio Setup & FMOD Studio

7\. Audio Production Specifications

8\. Core Gameplay Loop

9\. Adding New Sites & Characters

10\. Key Concepts

11\. Common Issues & Fixes

12\. Testing Checklist

13\. Code Entry Points

## 1\. System Overview

LudoEngine is an spatial-audio experience system for location-based immersive storytelling. It guides players through physical space using binaural 3D audio cues tied to GPS locations.

Core Concept: Player hears "a voice from the northeast" → turns to face that direction → hears the character's story.

The system is designed to be non-intrusive and invisible to the player. No UI gamification, no targeting indicators. Audio is the primary medium.

## 2\. Architecture

Core Managers:

GameManager: Overall game state and transitions

UIManager: Menus and text displays

TimeLayerManager: Handles past/present/future transitions

POIManager: Manages points of interest and their audio playback

SiteManager: Loads and switches between different geographic sites

AudioService: Handles all FMOD audio calls and parameter management

LocationService: GPS/compass data from device

HeadTrackingService: Device head orientation

Data Layer:

GameDataService: Loads JSON configuration files

StorageService: Saves player progress locally

The system is event-driven. TimeLayerManager fires events when layer changes, POIManager listens and reloads POIs for that layer.

## 3\. File Structure

Project folder organization:

Assets/

Scripts/

Core/ (GameManager, UIManager, SiteManager, TimeLayerManager, Site.cs)

Game/ (POIManager, POI.cs)

Services/ (AudioService, GameDataService, LocationService, HeadTrackingService, Interfaces/)

StreamingAssets/

Sites/

site_metadata.json (UNDERSCORE, not hyphen)

\[SiteName\]/ (Debug, BattleOfBoyne, etc)

site_data.json

Audio/ (Master.bank, Master.strings.bank)

Prefabs/UI/ (SiteButton.prefab)

## 4\. Startup Flow

1\. LoadingScene loads GameDataService (no data yet, just initializes)

2\. GameScene spawns GameManager, UIManager, POIManager

3\. POIManager.Start() waits for AudioService to initialize (async)

4\. SiteManager.Awake() reads site_metadata.json from StreamingAssets/Sites/

5\. MainMenu shows site selection buttons

6\. User selects site → SiteManager.LoadSite(siteId)

7\. LoadSite() calls:

AudioService.LoadBanksForSite(siteId) - loads FMOD banks

GameDataService.LoadSiteData(siteId) - loads site_data.json

TimeLayerManager.ReloadCurrentLayer() - loads POIs for current time layer

8\. POIManager.OnTimeLayerChanged() loads POIs from JSON

9\. Game starts in Wander mode (LocationService tracking, audio cues playing)

## 5\. JSON Configuration

### 5.1 site_metadata.json

Location: StreamingAssets/Sites/site_metadata.json

This file is read once at startup. SiteManager parses it to populate site selection buttons.

Example:

{ "sites":

\[ {

"siteId": "debug",

"siteName": "Debug Site",

"latitude": 53.3428,

"longitude": -6.2548,

"description": "Testing location"

},

{

"siteId": "battle_1690",

"siteName": "Battle of the Boyne 1690",

"latitude": 53.6522,

"longitude": -6.4214,

"description": "Historic battlefield"

}

\] }

}

### 5.2 site_data.json

Location: StreamingAssets/Sites/\[SiteId\]/site_data.json

Contains game configuration parameters and all POI data for this site.

Key Configuration Parameters:

| Parameter        | Default | Description                                          |
| ---------------- | ------- | ---------------------------------------------------- |
| proximityRadius  | 15.0    | Distance player must be to trigger dialogue playback |
| dialogueRadius   | 50.0    | Distance at which music begins fading in             |
| maxCueRadius     | 200.0   | Distance at which navigation cues stop playing       |
| cueStagingDelay  | 2.0     | Seconds between each navigation cue playing          |
| cyclePauseDelay  | 3.0     | Pause after completing cycle of all cues             |
| targetLockTime   | 2.5     | Seconds player must aim at POI to lock target        |
| targetLockAngle  | 15.0    | Angular range (degrees) to detect targeting          |
| maxMaxActiveCues | 4       | Maximum number of concurrent navigation cues         |

## 6\. Audio Setup & FMOD Studio

### 6.1 Event Structure

Parent Event: nav_cue (2D event)

Parameter Sheet: Vertical = Character_ID (discrete)

Each character gets its own track row

On each track, place Event Instruments referencing child events

Child Events (per character): nav_cue_char22_cue1, nav_cue_char22_cue2, etc

Parameters: Direction (continuous, 0-360)

8 Single Instruments, each triggered by Direction range

### 6.2 Direction Ranges (8 sectors)

| Direction | Angle             | Trigger Condition            |
| --------- | ----------------- | ---------------------------- |
| 0         | 0° (Front)        | Direction -22.5 to 22.5      |
| 1         | 45° (Front-Right) | Direction 22.5 to 67.5       |
| 2         | 90° (Right)       | Direction 67.5 to 112.5      |
| 3         | 135° (Back-Right) | Direction 112.5 to 157.5     |
| 4         | 180° (Back)       | Direction 157.5 to 202.5     |
| 5         | 225° (Back-Left)  | Direction 202.5 to 247.5     |
| 6         | 270° (Left)       | Direction 247.5 to 292.5     |
| 7         | 315° (Front-Left) | Direction 292.5 to 360/-22.5 |

### 6.3 Parent Event Setup

1\. Create 2D event: nav_cue

2\. Add Parameter Sheet (vertical = Character_ID)

3\. Create Audio Track for each character ID (1, 22, 23, etc)

4\. On each track, add 4 Event Instruments:

Drag nav_cue_char22_cue1 from Events Browser

Set trigger: Trigger == 1 AND CueIndex == 1

Drag nav_cue_char22_cue2

Set trigger: Trigger == 1 AND CueIndex == 2

(repeat for cue3, cue4)

### 6.4 Child Event Setup

1\. Create 2D event: nav_cue_char22_cue1

2\. Add parameter: Direction (Continuous, 0-360)

3\. Check "Expose recursively via event instruments"

4\. Add 8 Single Instruments to timeline (position doesn't matter)

5\. For each instrument, set trigger condition (see Direction Ranges above)

6\. Drag audio file into each instrument:

char22_cue1_dir000.wav (stereo, binaural, 48kHz)

char22_cue1_dir045.wav

(etc for all 8 directions)

### 6.5 Master Track Settings

Add Volume automation on NormalizedDistance parameter:

Distance 0.0 → Volume 0dB (close, full volume)

Distance 1.0 → Volume -48dB (far, very quiet)

Build banks: Press F7 in FMOD Studio

Copy to: Assets/StreamingAssets/Sites/\[SiteId\]/Audio/

## 7\. Audio Production Specifications

### 7.1 Navigation Cues (Binaural)

These are 3D spatial audio files that have already been spatialized during recording. FMOD will NOT add any spatializer - the spatial information is baked into the stereo mix.

| Parameter         | Specification                                                             |
| ----------------- | ------------------------------------------------------------------------- |
| Channels          | Stereo (MANDATORY - binaural data requires L/R channels)                  |
| Sample Rate       | 48 kHz                                                                    |
| Bit Depth         | 16-bit or 24-bit (24-bit preferred for quality)                           |
| Duration          | 2.0 - 3.0 seconds per cue                                                 |
| Format            | WAV (lossless, for archival and FMOD import)                              |
| Loudness          | \-18 LUFS ± 1 (normalized loudness)                                       |
| Peak Level        | No peaks above -0.3 dBFS (leave headroom for FMOD processing)             |
| Naming Convention | char\[ID\]\_cue\[1-4\]\_dir\[000-315\].wav (e.g., char22_cue1_dir090.wav) |

### 7.2 Recording Binaural Audio

Equipment & Setup:

Binaural microphone (Neumann KU 100 recommended, or similar dummy head with ear microphones)

Audio interface supporting 2-channel input at 48 kHz

Recording software (Reaper, Pro Tools, Audition, Logic Pro)

Recording for 8 Directions:

Perform or play audio source while recording from each of 8 positions around the microphone:

0° (Front): Directly in front of microphone

45° (Front-Right): 45° clockwise from front

90° (Right): 90° to the right

Continue for 135°, 180°, 225°, 270°, 315°

Consistent distance: Keep audio source at same distance (approx 1-2 meters) for all recordings

Consistent performance: Perform same content from each position (don't re-record with different takes)

### 7.3 Post-Processing & Audio Production Workflow

Step 1: Recording (In DAW)

Record at 48 kHz, 24-bit stereo in your DAW

Use gain staging to peak at approximately -6dBFS during recording

Step 2: Editing

Trim to exact length (remove silence at start/end, but leave short fade-in/fade-out)

Fade in: 50ms (very quick, avoid artifacts)

Fade out: 100ms (natural tail without clicks)

Remove DC offset (if recorded with DC bias)

Step 3: EQ & Tone Shaping

Apply high-pass filter (HPF) at 80 Hz to remove rumble

Cut excess presence (boost 2-5 kHz if thin, reduce if harsh)

Avoid aggressive EQ - preserve binaural spatial cues

Step 4: Compression (Optional)

Gentle compression recommended (DO NOT crush dynamics)

Suggested settings: Ratio 2:1-4:1, Threshold -20dB, Attack 10ms, Release 50ms

Goal: Control peaks, not flatten the audio

Step 5: Limiting (Essential for Safety)

Apply limiter at -0.3 dBFS (brick wall, prevent clipping)

Threshold: -0.3 dBFS

Ratio: ∞:1 (hard limiter)

Attack: 1-2ms (catch peaks immediately)

Release: 100ms (fast recovery)

Step 6: Loudness Normalization

Normalize to -18 LUFS (loudness standard for immersive audio)

Use loudness analysis plugin (e.g., Waves WLM Plus, iZotope RX, Logic Pro's loudness meter)

After loudness matching, peaks should be around -3 to -1 dBFS

Step 7: Stereo Checking

Verify stereo file has proper channel separation (left ≠ right)

Mono sum check: Does it collapse to mono? If so, you've lost spatial info

Phase check: Ensure no phase issues between L/R (use correlation meter)

Step 8: Final Export

Export as: WAV, Stereo, 48 kHz, 24-bit

NO dithering (24-bit doesn't need it)

NO metadata (keep file clean)

### 7.4 Music & Dialogue (FMOD Spatializer - NOT Binaural)

These are standard mono recordings that FMOD will spatialize in real-time using its 3D spatializer. They do NOT need pre-recorded binaural processing.

| Parameter                   | Specification                                                        |
| --------------------------- | -------------------------------------------------------------------- |
| Channels                    | Mono (FMOD will spatialize)                                          |
| Sample Rate                 | 48 kHz (dialogue), 44.1 kHz acceptable (music)                       |
| Bit Depth                   | 16-bit (music can be compressed, dialogue prefer lossless)           |
| Format                      | WAV for dialogue, OGG Vorbis for music (smaller, acceptable quality) |
| Loudness                    | Music: -20 LUFS, Dialogue: -16 LUFS                                  |
| Peak Level                  | No peaks above -0.3 dBFS                                             |
| OGG Bitrate (if compressed) | 128-192 kbps (128 acceptable for music, 192 for dialogue)            |

### 7.5 Music Post-Processing Workflow

Same as navigation cues through Step 6, with these modifications:

Loudness target: -20 LUFS (slightly lower than cues, music is background)

Compression more aggressive (3:1 ratio) to glue mix together

Export: Mono (if only one music track per POI), or Stereo if recording stereo mix

If exporting to OGG: Use OGG encoder at 128 kbps (mono saves to ~64 kbps)

### 7.6 Dialogue Post-Processing Workflow

Similar to music, with dialogue-specific settings:

Loudness target: -16 LUFS (dialogue louder than music for intelligibility)

De-esser (optional): Reduce sibilance (S sounds) if too aggressive

EQ: Slight presence peak (3-5 kHz) to improve clarity in outdoor environment

Compression: 4:1 ratio to keep dialogue level consistent

Export: Mono, 48 kHz, 16-bit WAV (lossless for highest quality)

### 7.7 File Naming & Organization

Navigation Cues:

char\[ID\]\_cue\[1-4\]\_dir\[000/045/090/135/180/225/270/315\].wav

Example: char22_cue1_dir090.wav

Music:

character*\[ID\]\_music*\[variant\].ogg or .wav

Example: character_22_music_loop.ogg

Dialogue:

character*\[ID\]\_dialogue*\[description\].wav

Example: character_22_dialogue_intro.wav

### 7.8 Quality Assurance Checklist for Audio Files

Before importing to FMOD:

Navigation cues: Stereo files, 48 kHz, 24-bit, -18 LUFS ±1

Navigation cues: Duration 2-3 seconds, no silence at end

Navigation cues: Verify stereo has channel separation (not mono)

Music: Mono (or stereo if intentional), 44.1 kHz or 48 kHz, 16-bit or 24-bit, -20 LUFS ±1

Dialogue: Mono, 48 kHz, 16-bit WAV or OGG, -16 LUFS ±1

All files: Peaks no higher than -0.3 dBFS, properly tagged with metadata

All files: File names match convention (no spaces, use underscore)

## 8\. Core Gameplay Loop

Every frame (Update):

1\. Get player location from LocationService

2\. Get player head direction from HeadTrackingService

3\. For each active POI: Calculate distance, bearing, head-relative direction

4\. Update proximity zone based on distance

5\. If in Wander mode and not in proximity:

Check for targeting (is player facing any POI?)

If target locked: play sequential cues (1, 2, 3, 4)

If not targeted: play preview cues (always Cue 1) from eligible POIs

6\. Check if narration is complete (all dialogue played)

7\. Update UI with current state

## 9\. Adding New Sites & Characters

### 9.1 Add New Site

1\. Create folder: Assets/StreamingAssets/Sites/NewSiteName/

2\. Add to site_metadata.json with siteId, siteName, latitude, longitude

3\. Create site_data.json in the new folder (copy template, update gameConfig and pois)

4\. For each character: Create FMOD child events (nav_cue_char\[ID\]\_cue\[1-4\])

5\. Add 8 Single Instruments per cue event (8 directions)

6\. Import 32 audio files per character (4 cues × 8 directions, stereo binaural, 48 kHz)

7\. Update parent event (nav_cue) - add Audio Track for new character ID, add 4 Event Instruments

8\. Build banks (F7), copy to StreamingAssets/Sites/NewSiteName/Audio/

9\. Test: Load site from menu, verify audio plays in correct directions

### 9.2 Add New Character to Existing Site

1\. Update site_data.json pois.\[timeLayerId\] with new character entry (unique characterId)

2\. Create FMOD child events: nav_cue_char\[newID\]\_cue1, cue2, cue3, cue4

3\. Add 8 Single Instruments to each cue event (8 directions)

4\. Import audio files: 32 stereo binaural files, 48 kHz, 24-bit WAV

5\. Update parent event (nav_cue): Add Audio Track (Character_ID = newID), add 4 Event Instruments with triggers

6\. Build banks (F7), copy to StreamingAssets/Sites/\[Site\]/Audio/

7\. Test: Verify navigation cues play in all 8 directions, sequential cues play when targeted

## 10\. Key Concepts

### 10.1 Direction Parameter

Calculated in AudioService.PlayBinauralNavigationCue():

relativeDirection = (poiBearing - playerHeading + 360) % 360

poiBearing: Geographic compass bearing from player to POI (0-360°)

playerHeading: Player's head direction from compass (0-360°)

Result: Head-relative direction (0-360°, where 0° = straight ahead)

This value is sent to FMOD, triggering the correct directional audio file.

### 10.2 Cue Index

0: Not used (no instrument exists for this)

1: Preview cue (always played for non-targeted POIs)

2, 3, 4: Sequential story cues (played when player targets POI)

Targeted POIs cycle through 1→2→3→4 with delay between each.

Non-targeted POIs cycle through Cue 1 only.

### 10.3 Proximity Zones

0.0: Outside dialogueRadius (no music)

0.0-1.0: Between dialogueRadius and proximityRadius (music fading in)

1.0-2.0: Inside proximityRadius (full music, narration triggers)

Zone value calculated: CalculateZoneFromDistance(distance) in POIManager

### 10.4 Trigger Parameter

Always set to 1 when playing cue, reset to 0 after ~0.1s.

Used to ensure FMOD instrument retriggering doesn't cause audio overlap.

## 11\. Common Issues & Fixes

### 11.1 Audio plays but no sound

Check Master.bank and Master.strings.bank are in StreamingAssets/Sites/\[Site\]/Audio/

Check AudioService.LoadBanksForSite() is called before playing

Check FMOD project includes the character's child events (not missing)

### 11.2 Wrong audio plays (direction incorrect)

Verify Direction parameter is being set in AudioService.PlayBinauralNavigationCue()

Check Direction ranges in FMOD (should be 8 × 45° sectors)

Check bearing calculation: CalculateBearing() in POIManager

Check head tracking is working (open debug display)

### 11.3 Navigation cues don't play

Check sharedCueInstance is valid: AudioService.IsInstanceValid(sharedCueInstance)

Check Character_ID in site_data.json matches FMOD event character ID

Check CueIndex in code matches FMOD instruments (1, 2, 3, 4 only)

Check Trigger condition is CueIndex == correct value

Check audio files exist in FMOD (not missing imports)

### 11.4 Site won't load

Check site_metadata.json has underscore (not hyphen): site_metadata.json

Check site_data.json is in StreamingAssets/Sites/\[SiteId\]/

Check JSON is valid (use JSONLint tool)

Check character latitudes/longitudes are valid for location

### 11.5 Narration doesn't complete

Check character_music FMOD event exists

Check characterAudioEvent in site_data.json matches FMOD event name

Check POI.Initialize() completes without errors

Check narration length in FMOD event is >0 (not 0ms)

## 12\. Testing Checklist

When adding new site or character:

site_metadata.json updated

site_data.json valid JSON (check with linter)

All character IDs exist in FMOD (1-50)

All child events created (nav_cue_char\[ID\]\_cue\[1-4\])

All 32 audio files present per character (4 cues × 8 directions)

Audio files: Stereo, 48 kHz, 24-bit WAV

Direction ranges don't overlap (0-45, 45-90, 90-135, etc)

Banks built (F7 in FMOD)

Banks copied to StreamingAssets/Sites/\[Site\]/Audio/

Parent event tracks added (one per character)

Event Instruments reference correct child events

Trigger conditions set (Character_ID AND CueIndex)

Site loads without errors

Navigation cues play in correct 8 directions

Sequential cues play when targeted

Narration completes

Rewards unlock correctly

Progression tracks properly

## 13\. Code Entry Points

If a developer needs to modify behavior:

### 13.1 Audio Playback

AudioService.cs: PlayBinauralNavigationCue() - main audio call

POIManager.cs: HandleStandardNavigation() - preview cues (non-targeted)

POIManager.cs: HandleTargetedNavigation() - sequential cues (targeted)

### 13.2 Targeting Logic

POIManager.cs: UpdateTargetingLogic() - lock/unlock state machine

POIManager.cs: CheckForPotentialTarget() - initial detection

POIManager.cs: UpdatePotentialTargeting() - timer logic

### 13.3 Progression

POIManager.cs: OnPOINarrationComplete() - triggered when story finishes

POI.cs: CheckNarrationCompletion() - checks if narration done

### 13.4 Site Loading

SiteManager.cs: LoadSite() - loads banks and data

GameDataService.cs: LoadSiteData() - reads JSON

### 13.5 Data Access

GameDataService.cs: GetPOIsForTimeLayer() - retrieve POI data from JSON

GameDataService.cs: GameConfig property - access game parameters

## Summary

LudoEngine is an immersive audio-spatial experience system. Its architecture prioritizes reliability, clarity, and the seamless integration of audio with geographic space.

The engine loads site data from JSON, streams FMOD audio based on player location and head direction, and guides players through invisible mechanics that feel natural and immersive.

The complexity lies in the audio production (recording binaural cues in 8 directions for multiple characters and cues) and the design discipline to keep the experience invisible and non-gamified.

The code simply executes this design cleanly and reliably.

For questions or implementation support, refer to the relevant code entry points and consult FMOD Studio documentation for advanced parameter routing.