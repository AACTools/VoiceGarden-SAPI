using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotNetTtsWrapper.Models;
using NaturalVoiceSAPIAdapter.SapiInterop;

namespace NaturalVoiceSAPIAdapter;

[ComVisible(true)]
[Guid("013AB33B-AD1A-401C-8BEE-F6E2B046A94E")]
[ClassInterface(ClassInterfaceType.None)]
public class TTSEngine : ISpTTSEngine, ISpObjectWithToken
{
    private ISpObjectToken? _token;
    private string _engineName = "";
    private string _voiceId = "";
    private AbstractTtsClient? _ttsClient;
    private int _sampleRate = 24000;
    private ushort _bitsPerSample = 16;
    private ushort _channels = 1;

    public int Speak(
        uint dwSpeakFlags,
        ref Guid rguidFormatId,
        IntPtr pWaveFormatEx,
        IntPtr pTextFragList,
        ISpTTSEngineSite pOutputSite)
    {
        if (pTextFragList == IntPtr.Zero || pOutputSite == null)
            return SapiConstants.E_POINTER;

        EnsureTtsClient();

        var text = ExtractTextFromFragments(pTextFragList);
        if (string.IsNullOrEmpty(text))
            return SapiConstants.S_OK;

        try
        {
            var options = BuildTtsOptions(pTextFragList);
            var result = _ttsClient!.SynthToBytesAsync(text, options).GetAwaiter().GetResult();

            if (result?.AudioData != null && result.AudioData.Length > 0)
            {
                byte[] pcmData = EnsurePcm16(result.AudioData);
                WriteAudioToSite(pOutputSite, pcmData);
            }

            FireEndStreamEvent(pOutputSite);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TTS synthesis error: {ex.Message}");
        }

        return SapiConstants.S_OK;
    }

    public int GetOutputFormat(
        IntPtr pTargetFmtId,
        IntPtr pTargetWaveFormatEx,
        out Guid pOutputFormatId,
        out IntPtr ppCoMemOutputWaveFormatEx)
    {
        pOutputFormatId = SapiConstants.SPDFID_WaveFormatEx;

        int formatSize = Marshal.SizeOf<WAVEFORMATEX>();
        IntPtr pFormat = Marshal.AllocCoTaskMem(formatSize);

        var wf = new WAVEFORMATEX
        {
            wFormatTag = 1,
            nChannels = _channels,
            nSamplesPerSec = (uint)_sampleRate,
            wBitsPerSample = _bitsPerSample,
        };
        wf.nBlockAlign = (ushort)(wf.nChannels * wf.wBitsPerSample / 8);
        wf.nAvgBytesPerSec = wf.nSamplesPerSec * wf.nBlockAlign;
        wf.cbSize = 0;

        Marshal.StructureToPtr(wf, pFormat, false);
        ppCoMemOutputWaveFormatEx = pFormat;

        return SapiConstants.S_OK;
    }

    public int SetObjectToken(ISpObjectToken pToken)
    {
        _token = pToken;
        ReadVoiceConfig(pToken);
        return SapiConstants.S_OK;
    }

    public int GetObjectToken(out ISpObjectToken ppToken)
    {
        ppToken = _token!;
        return _token != null ? SapiConstants.S_OK : SapiConstants.E_POINTER;
    }

    private void ReadVoiceConfig(ISpObjectToken token)
    {
        try
        {
            token.GetStringValue("EngineName", out IntPtr pEngine);
            _engineName = Marshal.PtrToStringUni(pEngine) ?? "";
            Marshal.FreeCoTaskMem(pEngine);
        }
        catch { }

        try
        {
            token.GetStringValue("VoiceId", out IntPtr pVoice);
            _voiceId = Marshal.PtrToStringUni(pVoice) ?? "";
            Marshal.FreeCoTaskMem(pVoice);
        }
        catch { }

        try
        {
            ISpDataKey configKey;
            token.OpenKey("NaturalVoiceConfig", out configKey);

            try
            {
                configKey.GetStringValue("EngineName", out IntPtr pEngine);
                _engineName = Marshal.PtrToStringUni(pEngine) ?? _engineName;
                Marshal.FreeCoTaskMem(pEngine);
            }
            catch { }

            try
            {
                configKey.GetStringValue("VoiceId", out IntPtr pVoice);
                _voiceId = Marshal.PtrToStringUni(pVoice) ?? _voiceId;
                Marshal.FreeCoTaskMem(pVoice);
            }
            catch { }

            try
            {
                configKey.GetDWORD("SampleRate", out uint sr);
                _sampleRate = (int)sr;
            }
            catch { }
        }
        catch { }
    }

    private void EnsureTtsClient()
    {
        if (_ttsClient != null) return;

        ITtsCredentials? credentials = BuildCredentials();
        _ttsClient = TtsFactory.CreateClient(_engineName, credentials);

        if (!string.IsNullOrEmpty(_voiceId))
        {
            _ttsClient.SetVoice(_voiceId);
        }
    }

    private ITtsCredentials? BuildCredentials()
    {
        ITtsCredentials? creds = null;

        if (_token != null)
        {
            try
            {
                _token.OpenKey("NaturalVoiceConfig", out ISpDataKey configKey);
                creds = CredentialBuilder.FromTokenConfig(_engineName, configKey);
                if (creds != null) return creds;
            }
            catch { }
        }

        creds = TryReadRegistryCredentials();
        if (creds != null) return creds;

        return EnvFallbackCredentials(_engineName);
    }

    private ITtsCredentials? TryReadRegistryCredentials()
    {
        try
        {
            using var baseKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\NaturalVoiceSAPIAdapter\VoiceTokens");
            if (baseKey == null) return null;

            foreach (var subKeyName in baseKey.GetSubKeyNames())
            {
                if (!subKeyName.StartsWith(_engineName + "_", StringComparison.OrdinalIgnoreCase)) continue;
                using var subKey = baseKey.OpenSubKey(subKeyName);
                if (subKey == null) continue;

                using var configKey = subKey.OpenSubKey("NaturalVoiceConfig");
                if (configKey == null) continue;

                string? voiceId = configKey.GetValue("VoiceId") as string;
                if (voiceId != _voiceId && !string.IsNullOrEmpty(_voiceId)) continue;

                var apiKey = configKey.GetValue("ApiKey") as string;
                var region = configKey.GetValue("Region") as string;
                var secretKey = configKey.GetValue("SecretKey") as string;
                var modelPath = configKey.GetValue("ModelPath") as string;

                if (string.IsNullOrEmpty(apiKey) && string.IsNullOrEmpty(modelPath)) continue;

                return _engineName.ToLowerInvariant() switch
                {
                    "azure" => new AzureCredentials
                    {
                        SubscriptionKey = apiKey ?? "",
                        Region = region ?? "eastus"
                    },
                    "openai" => new OpenAICredentials { ApiKey = apiKey ?? "" },
                    "elevenlabs" => new ElevenLabsCredentials { ApiKey = apiKey ?? "" },
                    "google" => new GoogleCredentials { ApiKey = apiKey ?? "" },
                    "polly" => new PollyCredentials
                    {
                        AccessKeyId = apiKey ?? "",
                        SecretAccessKey = secretKey ?? "",
                        Region = region ?? "us-east-1"
                    },
                    "sherpaonnx" => new SherpaOnnxCredentials
                    {
                        ModelPath = modelPath,
                    },
                    _ => null
                };
            }
        }
        catch { }
        return null;
    }

    private static ITtsCredentials? EnvFallbackCredentials(string engineName)
    {
        return engineName.ToLowerInvariant() switch
        {
            "azure" when !string.IsNullOrEmpty(
                Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY")
                ?? Environment.GetEnvironmentVariable("MICROSOFT_TOKEN")) => new AzureCredentials
            {
                SubscriptionKey = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY")
                    ?? Environment.GetEnvironmentVariable("MICROSOFT_TOKEN") ?? "",
                Region = Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION")
                    ?? Environment.GetEnvironmentVariable("MICROSOFT_REGION") ?? "eastus"
            },
            "openai" when !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY")) =>
                new OpenAICredentials { ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "" },
            "elevenlabs" when !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY")) =>
                new ElevenLabsCredentials { ApiKey = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY") ?? "" },
            "google" when !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GOOGLE_API_KEY")) =>
                new GoogleCredentials { ApiKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY") ?? "" },
            "polly" when !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID")) =>
                new PollyCredentials
                {
                    AccessKeyId = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID") ?? "",
                    SecretAccessKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY") ?? "",
                    Region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1"
                },
            _ => null
        };
    }

    private static string ExtractTextFromFragments(IntPtr pTextFragList)
    {
        var sb = new StringBuilder();
        IntPtr current = pTextFragList;

        while (current != IntPtr.Zero)
        {
            var frag = Marshal.PtrToStructure<SPVTEXTFRAG>(current);
            if (frag.State.eAction == SPVACTIONS.SPVA_Speak ||
                frag.State.eAction == SPVACTIONS.SPVA_SpellOut)
            {
                if (frag.pTextStart != IntPtr.Zero && frag.ulTextLen > 0)
                {
                    string? text = Marshal.PtrToStringUni(frag.pTextStart, (int)frag.ulTextLen);
                    if (text != null)
                        sb.Append(text);
                }
            }

            current = frag.pNext;
        }

        return sb.ToString();
    }

    private static TtsOptions BuildTtsOptions(IntPtr pTextFragList)
    {
        var options = new TtsOptions { Format = AudioFormat.Wav };

        if (pTextFragList != IntPtr.Zero)
        {
            var frag = Marshal.PtrToStructure<SPVTEXTFRAG>(pTextFragList);
            if (frag.State.RateAdj != 0)
            {
                options.Rate = MapSapiRate(frag.State.RateAdj);
            }
            if (frag.State.Volume > 0 && frag.State.Volume != 100)
            {
                options.Volume = (int)frag.State.Volume;
            }
        }

        return options;
    }

    private static SpeechRate MapSapiRate(int sapiRate)
    {
        return sapiRate switch
        {
            <= -5 => SpeechRate.XSlow,
            < 0 => SpeechRate.Slow,
            0 => SpeechRate.Medium,
            < 5 => SpeechRate.Fast,
            _ => SpeechRate.XFast,
        };
    }

    private byte[] EnsurePcm16(byte[] audioData)
    {
        if (audioData.Length < 44) return audioData;

        if (audioData[0] == 'R' && audioData[1] == 'I' &&
            audioData[2] == 'F' && audioData[3] == 'F')
        {
            int dataOffset = 12;
            while (dataOffset < audioData.Length - 8)
            {
                string chunkId = System.Text.Encoding.ASCII.GetString(audioData, dataOffset, 4);
                int chunkSize = BitConverter.ToInt32(audioData, dataOffset + 4);
                if (chunkId == "data")
                {
                    int pcmStart = dataOffset + 8;
                    int pcmLength = Math.Min(chunkSize, audioData.Length - pcmStart);
                    byte[] pcm = new byte[pcmLength];
                    Buffer.BlockCopy(audioData, pcmStart, pcm, 0, pcmLength);
                    return pcm;
                }
                dataOffset += 8 + chunkSize;
                if (chunkSize % 2 != 0) dataOffset++;
            }
        }

        return audioData;
    }

    private void WriteAudioToSite(ISpTTSEngineSite site, byte[] pcmData)
    {
        int offset = 0;
        while (offset < pcmData.Length)
        {
            uint actions = site.GetActions();
            if ((actions & SapiConstants.SPVES_ABORT) != 0)
                break;

            int chunkSize = Math.Min(4096, pcmData.Length - offset);
            IntPtr pBuffer = Marshal.AllocHGlobal(chunkSize);
            try
            {
                Marshal.Copy(pcmData, offset, pBuffer, chunkSize);
                site.Write(pBuffer, (uint)chunkSize, out _);
            }
            finally
            {
                Marshal.FreeHGlobal(pBuffer);
            }
            offset += chunkSize;
        }
    }

    private static void FireEndStreamEvent(ISpTTSEngineSite site)
    {
        var ev = new SPEVENT
        {
            eEventId = SapiEventIds.SPEI_END_INPUT_STREAM,
            elParamType = SapiEventParamTypes.SPET_LPARAM_IS_UNDEFINED,
            ulStreamNum = 0,
            ullAudioStreamOffset = 0,
            wParam = IntPtr.Zero,
            lParam = IntPtr.Zero,
        };
        IntPtr pEvent = Marshal.AllocHGlobal(Marshal.SizeOf<SPEVENT>());
        try
        {
            Marshal.StructureToPtr(ev, pEvent, false);
            site.AddEvents(pEvent, 1);
        }
        finally
        {
            Marshal.FreeHGlobal(pEvent);
        }
    }
}
