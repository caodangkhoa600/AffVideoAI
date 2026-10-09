# 15: Uploaded narration and music

**What to build:** A member attaches their own narration file and their own music file to a Variant, confirms they hold the rights, sets the music volume, and gets a Rendered Video with that audio mixed in.

**Blocked by:** 09

**Status:** done

- [x] Narration and music can each be uploaded, replaced and removed
- [x] An upload is refused unless the member confirms they hold the rights; the confirmation is recorded with who and when
- [x] Audio files are validated by decoding and limited in size and length
- [x] Audio is loudness-normalised and mixed with the chosen music volume
- [x] Audio longer than the video is cut with a fade; shorter audio does not shorten the video
- [x] ffprobe confirms an AAC track of the video's duration, and a test confirms it is not silent
- [x] A video with no uploaded audio still renders with a silent track

## Comments

- 2026-10-10, implemented. `dotnet test` passed 576 tests, up from 525, with the worker rendering in its own container. The web app passed its typecheck, its lint and the check that its API types are current. Nobody has listened to a video: every sound the tests upload is a steady tone, and what they measure is levels. Nobody has used the new section of the Variant's page in a browser either; it was type-checked and linted only.

  The upload tests were written at the HTTP seam before the code. The render tests were written after the FFmpeg commands had been tried by hand in the worker image, since the levels they expect come from what those commands do.

  What was built:

  - A Variant has at most one Narration and one Music (`VariantAudio`), under `/api/v1/projects/{projectId}/variants/{variantId}/audio`: list, upload in place of the one of its kind, listen, set the Music's volume, remove.
  - An upload is an MP3 or a WAV. The API decodes the whole of it in managed code (NLayer for MP3, its own reader for WAV) and keeps a 16-bit PCM WAV of the sound, nothing else of the file. The API has no FFmpeg, and the worker's FFmpeg is only handed a WAV this system wrote.
  - The worker measures each track with FFmpeg, brings it to -16 LUFS with one gain, holds its peaks under -1.5 dB with a limiter, turns Music down to its volume, and mixes the two under the joined Scenes.
  - A Rendered Video records which Narration and Music it was mixed with and the Music's volume.
  - The Variant's page has a "Narration and music" section, and the Rendered Video's line says what it has to be heard.
  - CONTEXT.md gained Narration, Music and Rights confirmation.

  How each box was checked:

  - Uploaded, replaced and removed: tests at the HTTP seam for each, including that the replaced or removed file leaves object storage, and that deleting a Project takes its Variants' audio files with it.
  - Refused without the rights, recorded with who and when: an upload with `rightsConfirmed` missing, `false` or anything else is answered 400 and nothing is stored. The member and time are on the audio, and in the audit log as `audio.rights-confirmed`, which a test finds still there after the audio is removed.
  - Validated by decoding, limited in size and length: 20 MB, 1 second to 5 minutes, mono or stereo, 8000 to 48000 samples a second. Tests refuse an empty file, one too large, an image, text called `song.mp3`, an AAC stream, a WAV cut short, an MP3 that is only its header, one too short, one too long, surround sound, 96 kHz and a compressed WAV, and accept ones exactly at the limits of length.
  - Loudness-normalised and mixed at the chosen volume: a tone uploaded at a fiftieth of full scale and one at nine tenths come out within 1.5 dB of each other and of the target. Music alone at 100 is at the target, at 25 is 12 dB under it, at 0 is not heard. With both, the Narration's tone and the Music's are each listened for alone and found about 10.5 dB apart, which is the default volume of 30.
  - Longer is cut with a fade, shorter does not shorten: a 20-second Narration on a 15-second video is at full level a second before the end and at least 12 dB down in the last tenth of a second; a 4-second one is followed by silence, and the video is still 15 seconds.
  - An AAC track of the video's duration that is not silent: ffprobe on a video with both, and its level.
  - No audio renders silent: the test from ticket 09 still passes, and the video records no audio.

  From the code review, which ran on both halves:

  - The gain was first capped so that a track's highest peak stayed under the ceiling. A quiet recording with one loud peak, which is what speech is, would not have been turned up at all. The gain is now capped only at 30 dB and a limiter holds the peaks. A test uploads a quiet tone with a full-scale click every second.
  - The test of both tracks together only checked that there was sound. It now measures each.
  - An AAC stream opens almost as an MP3 frame does, and was passed to the MP3 decoder. It is now told apart by its layer bits and refused.
  - A WAV written to a pipe says its length is the largest there is. It was refused as damaged and is now read to its end.
  - The audio a member listens to now has an entity tag, as a photo has. Decoding an MP3 can be abandoned when the member stops waiting. A volume sent with Narration is not read. A record in the tests was named with a word the glossary avoids.

  Left as it is:

  - A render mixes the audio the Variant has when the worker takes the job, not when the member asked. Audio removed in between fails the render with the reason, and it is not tried again.
  - The limits the ticket did not name are mine: the minimum of 1 second, mono or stereo only, 48000 samples a second at most, and Music's default volume of 30. A 96 kHz WAV is refused although it could be read.
  - The sound is served whole, with no reading in parts, so the player in the page may not let the member skip about, and Safari may not play it. Not tried in any browser.
  - Decoding holds the upload and the decoded sound in memory: about 200 MB at worst for one MP3 at the limits, with no limit on how many are decoded at once.
  - A corrupt MP3 with a second of frames that still decode is accepted: the decoder skips what it cannot read.
  - A volume set at the same moment as the Music is replaced can be lost to the volume the replaced file had.
  - Music at volume 0 is recorded on the video as its Music, at 0, though nothing of it is heard.
  - Duplicating a Variant does not copy its audio.
  - The glossary has no word for Narration and Music together; code and API say "audio". The worker and README say "track" for one of the two in a mix.
  - Removing files from storage when a record goes is now written in three places (assets, audio, Projects).
