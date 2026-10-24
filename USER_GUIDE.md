# Xiaoma Player + Recorder user guide

Open the built application or drag files and folders into its window. Keep the complete application folder together. The native edition uses Microsoft Edge WebView2 Evergreen Runtime and .NET Framework 4.8 on 64-bit Windows. Preferences and cache are stored in the adjacent `.data` folder.

## Playback and viewing

Images, sequences, models and document modules load when needed. Opening an image automatically lists nearby images in natural filename order. Previous/Next wraps at either end. Opening a numbered image identifies matching frames in the same directory without creating a temporary video. Sequences initially pause; press Space or Play to start. Mixed folders group each sequence into one playlist item.

- Space: play or pause.
- D/F or [/]: previous or next frame; sequences also support arrow keys.
- Tab or Info: media details, including codec, dimensions, frame rate and duration where available.
- Use the narrow right-hand playlist to choose media. Hover a thumbnail to see its filename.
- The Folder button uses the Windows folder picker; Ctrl+L or Alt+D focuses its address bar.

Video/audio playback, volume and full screen remain in the same window. Images and videos zoom around the center. Thumbnail work runs in the background; the list renders visible cards to reduce memory use. The encoder is used for output operations rather than running at startup.

## Supported content and boundaries

Images: PNG, JPEG, WebP, GIF, BMP, AVIF, SVG, ICO, TIFF, TGA, EXR and HDR. Numbered sequences support the relevant raster formats. Video and audio compatibility depends on the installed Chromium/WebView2 codecs as well as the container. Containers such as MP4, MOV and WebM do not guarantee that every codec inside will play.

Documents: PDF and basic DOCX/PPTX previews. Installed Microsoft Office is preferred for complex Word, PowerPoint and Excel conversion into a preview cache without modifying originals. Password-protected files must first be unlocked in the original application. Text formats include TXT, Markdown, logs, CSV, TSV and JSON.

Models: GLB/glTF, OBJ/MTL, FBX, STL, PLY and 3MF; supported loaders include Draco, Meshopt and KTX2. Procedural materials and application-specific effects may not reproduce exactly. EXR/HDR currently use a fixed preview transform, without OCIO or multilayer/AOV selection. HEIC, RAW, PSD, USD and Blend are not supported by this viewer edition.

Video frame stepping estimates frame rate from playback samples and falls back to 30 fps before sampling. Variable-frame-rate sources are not guaranteed frame-accurate. Sequence output detects usable hardware encoders and falls back to CPU when necessary; image color conversion remains format-dependent. Upscaling does not create detail absent from the source.

## Recording and updates

Use Recorder to choose a region, save location and audio sources. See the recording guide for pause, stop, recovery and trimming. Never force-terminate the application while it saves a recording. Signed update packages retain settings; forks should configure their own update service. This repository contains source only, without publisher private keys, bundled test media or third-party reference applications.
