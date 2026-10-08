# 15: Uploaded narration and music

**What to build:** A member attaches their own narration file and their own music file to a Variant, confirms they hold the rights, sets the music volume, and gets a Rendered Video with that audio mixed in.

**Blocked by:** 09

**Status:** ready-for-agent

- [ ] Narration and music can each be uploaded, replaced and removed
- [ ] An upload is refused unless the member confirms they hold the rights; the confirmation is recorded with who and when
- [ ] Audio files are validated by decoding and limited in size and length
- [ ] Audio is loudness-normalised and mixed with the chosen music volume
- [ ] Audio longer than the video is cut with a fade; shorter audio does not shorten the video
- [ ] ffprobe confirms an AAC track of the video's duration, and a test confirms it is not silent
- [ ] A video with no uploaded audio still renders with a silent track
