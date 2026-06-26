using System;
using System.Runtime.InteropServices;
using DotNetTtsWrapper.Models;
using VoiceGardenSAPIAdapter.SapiInterop;

namespace VoiceGardenSAPIAdapter;

public static class CredentialBuilder
{
    public static ITtsCredentials? FromTokenConfig(string engineName, ISpDataKey configKey)
    {
        string engine = engineName.ToLowerInvariant().Replace("-", "").Replace(" ", "");

        string? apiKey = TryGetString(configKey, "ApiKey");
        string? region = TryGetString(configKey, "Region");

        // SherpaOnnx tokens created by SherpaOnnxConfig use "Sherpa" as EngineType
        // and store explicit file paths (SherpaOnnxModelPath, SherpaOnnxTokens, etc.)
        if (engine == "sherpaonnx" || engine == "sherpa")
        {
            var modelFilePath = TryGetString(configKey, "SherpaOnnxModelPath");
            var tokensFilePath = TryGetString(configKey, "SherpaOnnxTokens");
            var dataDirPath = TryGetString(configKey, "SherpaOnnxDataDir");
            var lexiconPath = TryGetString(configKey, "SherpaOnnxLexicon");

            // If we have explicit file paths, use them
            if (!string.IsNullOrEmpty(modelFilePath))
            {
                return new SherpaOnnxCredentials
                {
                    ModelFilePath = modelFilePath,
                    TokensFilePath = tokensFilePath,
                    DataDirPath = dataDirPath,
                    LexiconFilePath = lexiconPath,
                    ModelId = TryGetString(configKey, "VoiceId"),
                };
            }

            // Fall back to ModelPath/ModelId style
            return new SherpaOnnxCredentials
            {
                ModelPath = TryGetString(configKey, "ModelPath"),
                ModelId = TryGetString(configKey, "ModelId"),
            };
        }

        return engine switch
        {
            "azuresdk" or "azure" => new AzureCredentials
            {
                SubscriptionKey = apiKey ?? "",
                Region = region ?? "eastus"
            },
            "google" => new GoogleCredentials
            {
                ApiKey = apiKey ?? ""
            },
            "polly" or "awspolly" => new PollyCredentials
            {
                AccessKeyId = apiKey ?? "",
                SecretAccessKey = TryGetString(configKey, "SecretKey") ?? "",
                Region = region ?? "us-east-1"
            },
            "openai" => new OpenAICredentials
            {
                ApiKey = apiKey ?? ""
            },
            "elevenlabs" => new ElevenLabsCredentials
            {
                ApiKey = apiKey ?? ""
            },
            "cartesia" => new CartesiaCredentials
            {
                ApiKey = apiKey ?? ""
            },
            "deepgram" => new DeepgramCredentials
            {
                ApiKey = apiKey ?? ""
            },
            _ => null
        };
    }

    private static string? TryGetString(ISpDataKey key, string valueName)
    {
        try
        {
            key.GetStringValue(valueName, out IntPtr pVal);
            string? val = Marshal.PtrToStringUni(pVal);
            Marshal.FreeCoTaskMem(pVal);
            return val;
        }
        catch
        {
            return null;
        }
    }
}
