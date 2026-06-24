using System;
using System.Runtime.InteropServices;
using DotNetTtsWrapper.Models;
using NaturalVoiceSAPIAdapter.SapiInterop;

namespace NaturalVoiceSAPIAdapter;

public static class CredentialBuilder
{
    public static ITtsCredentials? FromTokenConfig(string engineName, ISpDataKey configKey)
    {
        string engine = engineName.ToLowerInvariant().Replace("-", "").Replace(" ", "");

        string? apiKey = TryGetString(configKey, "ApiKey");
        string? region = TryGetString(configKey, "Region");

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
            "sherpaonnx" or "sherpa" => new SherpaOnnxCredentials
            {
                ModelPath = TryGetString(configKey, "ModelPath"),
                ModelId = TryGetString(configKey, "ModelId"),
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
