using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SherpaOnnxConfig
{
    internal static class AppDataLayout
    {
        private const string LegacyRootName = "VoiceGardenSAPIAdapter";
        private const string LegacyAltRootName = "VoiceGardensSAPIAdapter";

        public static string AdapterDataDir { get; } = ResolveAdapterDataDir();
        public static string ModelsDir { get; } = Path.Combine(AdapterDataDir, "models");

        private static string ResolveAdapterDataDir()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string preferred = ResolveInstallFolderNameFromBranding();
            var candidates = CandidateRootNames(preferred).ToList();

            foreach (string rootName in candidates)
            {
                string candidate = Path.Combine(local, rootName);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return Path.Combine(local, candidates.FirstOrDefault() ?? LegacyRootName);
        }

        private static IEnumerable<string> CandidateRootNames(string preferred)
        {
            var names = new List<string>();
            AddUnique(names, preferred);
            AddUnique(names, LegacyRootName);
            AddUnique(names, LegacyAltRootName);
            return names;
        }

        private static void AddUnique(List<string> names, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            if (names.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)))
                return;
            names.Add(value);
        }

        private static string ResolveInstallFolderNameFromBranding()
        {
            string? baseDir = AppContext.BaseDirectory;
            if (string.IsNullOrWhiteSpace(baseDir))
                return string.Empty;

            var dirs = new[]
            {
                baseDir,
                Directory.GetParent(baseDir)?.FullName,
                Directory.GetParent(Directory.GetParent(baseDir ?? string.Empty)?.FullName ?? string.Empty)?.FullName
            };

            foreach (string? dir in dirs)
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;
                string brandingPath = Path.Combine(dir, "branding.json");
                if (!File.Exists(brandingPath))
                    continue;

                try
                {
                    using FileStream fs = File.OpenRead(brandingPath);
                    using JsonDocument doc = JsonDocument.Parse(fs);
                    if (!doc.RootElement.TryGetProperty("install_folder_name", out JsonElement folder))
                        continue;
                    if (folder.ValueKind != JsonValueKind.String)
                        continue;
                    string? value = folder.GetString()?.Trim();
                    if (string.IsNullOrWhiteSpace(value))
                        continue;
                    if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                        continue;
                    return value;
                }
                catch
                {
                }
            }

            return string.Empty;
        }
    }
}

