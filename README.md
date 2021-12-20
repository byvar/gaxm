# GaXM
Experimental GAX (Shin'en GBA audio engine) to XM converter

All GAX versions are supported, so it should be possible to create XM files for most games using this engine.
However, many GAX features are not supported in XM. As such the resulting files will likely sound different from in-game.

Some of these features are:

* Tempo in XM is specified using a BPM and Speed value (= number of ticks per row). The tempo change effect either affects the BPM value (when parameter >= 32) or the Speed value (when parameter < 32), but not both. GAX's tempo system is different: it only uses a Speed value very similar to XM's (that matches XM's tempo when BPM is about 149-150). Because the tempo change effect can only affect the Speed value, it can be set to larger values than in XM. Additionally, to achieve that would require a non-integer speed value, compositions in GAX often change the tempo on each row. GAX includes a separate effect for this that starts this speed value alternating system and allows one to use different effects in the next rows. XM does not have this, so I translated this effect by using the closest single speed value that is faster. As a result, many GAX -> XM conversions are faster than the original track.

* While XM processes one pattern at a time per channel, GAX processes two: the pattern specified for that channel and the pattern specified in the current instrument. Instrument patterns can advance at a different speed (again, number of ticks per row) than the regular pattern. These "instrument rows" include information like the sample (& corresponding pitch, loop info, etc.), relative note number, and 2 effects which include volume, changing the instrument pattern speed... Arpeggios are a great use case for this system, so they are often missing entirely from GAX -> XM conversions.

* Of course, some that I haven't fully reverse-engineered yet. :)

However, for some games these exports sound close to perfect. Most likely, they can be perfected by creating a custom GAX player using the GAX structs provided in [BinarySerializer.GBA.Audio](https://github.com/BinarySerializer/BinarySerializer.GBA.Audio).

## Note
If you download the repo as a zip folder the submodules won't be included, causing the project not to compile. To solve this, download the submodule repos as well and place them in the specified folders:
* [BinarySerializer](https://github.com/BinarySerializer/BinarySerializer) (submodules/BinarySerializer)
* [BinarySerializer.Audio](https://github.com/BinarySerializer/BinarySerializer.Audio) (submodules/BinarySerializer.Audio)
* [BinarySerializer.GBA.Audio](https://github.com/BinarySerializer/BinarySerializer.GBA.Audio) (submodules/BinarySerializer.GBA.Audio)