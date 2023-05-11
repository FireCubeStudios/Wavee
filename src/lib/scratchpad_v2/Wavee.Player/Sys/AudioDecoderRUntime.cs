﻿using LanguageExt;
using NAudio.Wave;
using Wavee.VorbisDecoder.Convenience;

namespace Wavee.Player.Sys;

internal static class AudioDecoderRuntime
{
    public static Eff<WaveStream> OpenAudioDecoder(Stream stream, double duration) =>
        from format in ReadFormat(stream)
        from decoder in OpenDecoderPrivate(format, stream, duration)
        select decoder;

    private static Eff<WaveStream> OpenDecoderPrivate(MagicAudioFileType format, Stream stream, double duration)
    {
        return format switch
        {
            MagicAudioFileType.Vorbis => SuccessEff((WaveStream)new VorbisWaveReader(stream, duration, true)),
            MagicAudioFileType.Mp3 => SuccessEff((WaveStream)new WaveChannel32((new Mp3FileReader(stream)))),
            _ => FailEff<WaveStream>(new NotSupportedException($"Unsupported audio format: {format}"))
        };
    }
    private static Eff<MagicAudioFileType> ReadFormat(Stream stream)
    {
        return Eff(() =>
        {
            stream.Seek(0, SeekOrigin.Begin);
            Span<byte> magic = new byte[4];
            stream.ReadExactly(magic);

            stream.Seek(0, SeekOrigin.Begin);
            if (magic.SequenceEqual(OggMagic))
            {
                return MagicAudioFileType.Vorbis;
            }

            if (IsMp3(stream, magic))
            {
                return MagicAudioFileType.Mp3;
            }

            return MagicAudioFileType.Unknown;
        });
    }
    private static bool IsMp3(Stream stream, Span<byte> magic)
    {
        // Check for ID3v2 tags
        if (magic[0] == 0x49 && magic[1] == 0x44 && magic[2] == 0x33)
        {
            return true;
        }
        else
        {
            // Check for MP3 frame sync bits without ID3v2
            return ((magic[0] & 0xFF) == 0xFF) && ((magic[1] & 0xE0) == 0xE0);
        }
    }
    private static byte[] OggMagic = "OggS"u8.ToArray();
    private enum MagicAudioFileType
    {
        Unknown,
        Vorbis,
        Mp3
    }
}