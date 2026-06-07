using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DotNetTtsWrapper.Models;
using DotNetTtsWrapper.Engines;
using Microsoft.Win32;
using NaturalVoiceSAPIAdapter.SapiInterop;

namespace NaturalVoiceSAPIAdapter;

[ComVisible(true)]
[Guid("B8B9E38F-E5A2-4661-9FDE-4AC7377AA6F6")]
[ClassInterface(ClassInterfaceType.None)]
public class VoiceTokenEnumerator : IEnumSpObjectTokens
{
    private List<ISpObjectToken> _tokens = new();
    private int _currentPos;

    private static readonly string VoiceTokensBasePath = @"SOFTWARE\NaturalVoiceSAPIAdapter\VoiceTokens";

    public VoiceTokenEnumerator()
    {
        _currentPos = 0;
        try
        {
            InitDiscovery();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"VoiceTokenEnumerator init error: {ex}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void InitDiscovery()
    {
        try
        {
            DiscoverVoicesAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Voice discovery error: {ex}");
        }
    }

    private VoiceTokenEnumerator(List<ISpObjectToken> tokens, int currentPos)
    {
        _tokens = tokens;
        _currentPos = currentPos;
    }

    private async Task DiscoverVoicesAsync()
    {
        try
        {
            var localEngines = new[] { "sherpaonnx" };
            foreach (var engine in localEngines)
            {
                await DiscoverEngineVoices(engine, null);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Local engine discovery error: {ex}");
        }

        try
        {
            await DiscoverConfiguredCloudVoices();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cloud engine discovery error: {ex}");
        }
    }

    private async Task DiscoverEngineVoices(string engine, ITtsCredentials? credentials)
    {
        try
        {
            var client = TtsFactory.CreateClient(engine, credentials);
            if (client == null) return;

            List<TtsVoice> voices;
            try { voices = await client.GetVoicesAsync(); }
            catch { return; }

            foreach (var voice in voices)
            {
                try
                {
                    var token = CreateVoiceToken(engine, voice, credentials);
                    _tokens.Add(token);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Token creation error for {voice.Name}: {ex}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Engine {engine} discovery error: {ex}");
        }
    }

    private async Task DiscoverConfiguredCloudVoices()
    {
        var cloudEngines = new (string engine, string[] configKeys)[]
        {
            ("azure", new[] { "AZURE_SPEECH_KEY", "MICROSOFT_TOKEN" }),
            ("openai", new[] { "OPENAI_API_KEY" }),
            ("elevenlabs", new[] { "ELEVENLABS_API_KEY" }),
            ("google", new[] { "GOOGLE_API_KEY" }),
            ("polly", new[] { "AWS_ACCESS_KEY_ID" }),
        };

        foreach (var (engine, envKeys) in cloudEngines)
        {
            string? credValue = null;
            foreach (var key in envKeys)
            {
                credValue = Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrEmpty(credValue)) break;
            }

            if (string.IsNullOrEmpty(credValue)) continue;

            try
            {
                var creds = BuildCloudCredentials(engine);
                if (creds == null) continue;
                await DiscoverEngineVoices(engine, creds);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cloud engine {engine} discovery error: {ex}");
            }
        }
    }

    private static ITtsCredentials? BuildCloudCredentials(string engine)
    {
        return engine switch
        {
            "azure" => new AzureCredentials
            {
                SubscriptionKey = Env("AZURE_SPEECH_KEY") ?? Env("MICROSOFT_TOKEN") ?? "",
                Region = Env("AZURE_SPEECH_REGION") ?? Env("MICROSOFT_REGION") ?? "eastus"
            },
            "openai" => new OpenAICredentials { ApiKey = Env("OPENAI_API_KEY") ?? "" },
            "elevenlabs" => new ElevenLabsCredentials { ApiKey = Env("ELEVENLABS_API_KEY") ?? "" },
            "google" => new GoogleCredentials { ApiKey = Env("GOOGLE_API_KEY") ?? "" },
            "polly" => new PollyCredentials
            {
                AccessKeyId = Env("AWS_ACCESS_KEY_ID") ?? "",
                SecretAccessKey = Env("AWS_SECRET_ACCESS_KEY") ?? "",
                Region = Env("AWS_REGION") ?? "us-east-1"
            },
            _ => null
        };
    }

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name);

    private ISpObjectToken CreateVoiceToken(string engineName, TtsVoice voice, ITtsCredentials? credentials)
    {
        var token = new SpObjectToken();

        string safeId = voice.Id?.Replace("/", "_").Replace("\\", "_") ?? "default";
        string tokenId = $@"HKEY_CURRENT_USER\{VoiceTokensBasePath}\{engineName}_{safeId}";
        token.SetId(null, tokenId, true);
        token.SetStringValue(null, voice.Name ?? engineName);
        token.SetStringValue("EngineName", engineName);
        token.SetStringValue("VoiceId", voice.Id);
        token.SetStringValue("CLSID", "{013AB33B-AD1A-401C-8BEE-F6E2B046A94E}");

        var attrs = token.Data.GetOrCreateSubKey("Attributes");
        attrs.SetStringValue("Name", voice.Name ?? engineName);
        attrs.SetStringValue("Gender", voice.Gender.ToString());
        attrs.SetStringValue("Age", "Adult");
        attrs.SetStringValue("Vendor", voice.Provider ?? "DotNetTtsWrapper");

        string langStr = "en-US";
        if (voice.LanguageCodes?.Count > 0)
        {
            langStr = voice.LanguageCodes[0].Bcp47 ?? "en-US";
        }
        attrs.SetStringValue("Locale", langStr);

        try
        {
            ushort langId = LocaleToLangId(langStr);
            attrs.SetStringValue("Language", $"0x{langId:X4}");
        }
        catch { }

        attrs.SetStringValue("NaturalVoiceType", "DotNetTtsWrapper");

        var config = token.Data.GetOrCreateSubKey("NaturalVoiceConfig");
        config.SetStringValue("EngineName", engineName);
        config.SetStringValue("VoiceId", voice.Id);

        if (credentials != null)
        {
            StoreCredentials(config, credentials);
        }

        PersistTokenToRegistry(token);

        return token;
    }

    internal static void PersistTokenToRegistry(SpObjectToken token)
    {
        try
        {
            token.GetId(out IntPtr pId);
            string? tokenId = Marshal.PtrToStringUni(pId);
            Marshal.FreeCoTaskMem(pId);

            if (string.IsNullOrEmpty(tokenId)) return;

            string regPath = tokenId;
            if (regPath.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))
                regPath = regPath.Substring("HKEY_CURRENT_USER\\".Length);

            using var key = Registry.CurrentUser.CreateSubKey(regPath);
            if (key == null) return;

            PersistDataKey(key, token.Data, "");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Registry persist error: {ex.Message}");
        }
    }

    private static void PersistDataKey(RegistryKey parentKey, SpDataKey data, string prefix)
    {
        foreach (var kv in data.StringValues)
        {
            try { parentKey.SetValue(kv.Key, kv.Value, RegistryValueKind.String); }
            catch { }
        }

        foreach (var kv in data.DwordValues)
        {
            try { parentKey.SetValue(kv.Key, kv.Value, RegistryValueKind.DWord); }
            catch { }
        }

        foreach (var kv in data.SubKeys)
        {
            try
            {
                using var subKey = parentKey.CreateSubKey(kv.Key);
                if (subKey != null)
                    PersistDataKey(subKey, kv.Value, "");
            }
            catch { }
        }
    }

    private static void StoreCredentials(SpDataKey config, ITtsCredentials credentials)
    {
        switch (credentials)
        {
            case AzureCredentials azure:
                config.SetStringValue("ApiKey", azure.SubscriptionKey);
                config.SetStringValue("Region", azure.Region);
                break;
            case OpenAICredentials openai:
                config.SetStringValue("ApiKey", openai.ApiKey);
                break;
            case ElevenLabsCredentials eleven:
                config.SetStringValue("ApiKey", eleven.ApiKey);
                break;
            case GoogleCredentials google:
                config.SetStringValue("ApiKey", google.ApiKey);
                break;
            case PollyCredentials polly:
                config.SetStringValue("ApiKey", polly.AccessKeyId);
                config.SetStringValue("SecretKey", polly.SecretAccessKey);
                config.SetStringValue("Region", polly.Region);
                break;
            case SherpaOnnxCredentials sherpa:
                if (sherpa.ModelPath != null) config.SetStringValue("ModelPath", sherpa.ModelPath);
                if (sherpa.ModelId != null) config.SetStringValue("ModelId", sherpa.ModelId);
                break;
        }
    }

    private static ushort LocaleToLangId(string locale)
    {
        return locale.ToLowerInvariant() switch
        {
            "en-us" => 0x0409,
            "en-gb" => 0x0809,
            "zh-cn" => 0x0804,
            "zh-tw" => 0x0404,
            "ja-jp" => 0x0411,
            "ko-kr" => 0x0412,
            "de-de" => 0x0407,
            "fr-fr" => 0x040C,
            "es-es" => 0x0C0A,
            "it-it" => 0x0410,
            "pt-br" => 0x0416,
            "ru-ru" => 0x0419,
            _ => 0x0409,
        };
    }

    public int Next(uint celt, out ISpObjectToken pelt, out uint pceltFetched)
    {
        pelt = null!;
        pceltFetched = 0;

        try
        {
            if (_currentPos >= _tokens.Count)
                return SapiConstants.S_FALSE;

            int count = (int)Math.Min(celt, (uint)(_tokens.Count - _currentPos));
            if (count == 0)
                return SapiConstants.S_FALSE;

            if (celt == 1)
            {
                pelt = _tokens[_currentPos];
                _currentPos++;
                pceltFetched = 1;
                return SapiConstants.S_OK;
            }

            throw new NotImplementedException("Batch Next not implemented");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Next error: {ex}");
            return SapiConstants.E_FAIL;
        }
    }

    public int Skip(uint celt)
    {
        _currentPos += (int)celt;
        if (_currentPos > _tokens.Count)
            _currentPos = _tokens.Count;
        return _currentPos < _tokens.Count ? SapiConstants.S_OK : SapiConstants.S_FALSE;
    }

    public int Reset()
    {
        _currentPos = 0;
        return SapiConstants.S_OK;
    }

    public int Clone(out IEnumSpObjectTokens ppEnum)
    {
        ppEnum = new VoiceTokenEnumerator(_tokens, _currentPos);
        return SapiConstants.S_OK;
    }

    public int Item(uint Index, out ISpObjectToken ppToken)
    {
        if (Index >= _tokens.Count)
        {
            ppToken = null!;
            return SapiConstants.E_INVALIDARG;
        }
        ppToken = _tokens[(int)Index];
        return SapiConstants.S_OK;
    }

    public int GetCount(out uint pCount)
    {
        pCount = (uint)_tokens.Count;
        return SapiConstants.S_OK;
    }
}
