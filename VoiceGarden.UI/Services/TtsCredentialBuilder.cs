using System;
using System.Collections.Generic;
using System.Linq;
using VoiceGarden.UI.Models;

namespace VoiceGarden.UI.Services;

/// <summary>
/// Builds the credential dictionary the Rust wrapper expects for an engine,
/// derived from the engine's declared credential keys. Shared by the
/// Credentials tab (validate), Voices tab (fetch/preview) and CLI paths.
/// </summary>
public static class TtsCredentialBuilder
{
    /// <summary>
    /// Build credentials for a cloud engine from its primary key and secondary
    /// value (region / user ID / secret access key depending on the engine).
    /// Returns null for unknown engines.
    /// </summary>
    public static Dictionary<string, string>? Build(string engineId, string key, string secondary)
    {
        var def = EngineDefinition.DiscoverAll()
            .FirstOrDefault(e => e.Id.Equals(engineId, StringComparison.OrdinalIgnoreCase));
        if (def == null)
        {
            // Edge / sherpaonnx / sapi need no credentials but are valid engines.
            var id = engineId.ToLowerInvariant();
            if (id is "edge" or "sherpaonnx" or "sapi") return new();
            return null;
        }

        var creds = new Dictionary<string, string>();
        foreach (var credKey in def.CredentialKeys)
        {
            var value = credKey switch
            {
                "apiKey" or "subscriptionKey" or "accessKeyId" or "token" => key,
                "region" or "userId" or "secretAccessKey" => secondary,
                "instanceId" => "",
                _ => key,
            };
            creds[credKey] = value;
        }

        // Polly needs a default region if not specified
        if (engineId.Equals("polly", StringComparison.OrdinalIgnoreCase) && !creds.ContainsKey("region"))
            creds["region"] = "us-east-1";

        return creds.Count > 0 ? creds : null;
    }
}
