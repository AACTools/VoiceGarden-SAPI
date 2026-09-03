using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VoiceGarden.UI.Services;

/// <summary>
/// Manages SherpaOnnx model catalog, download, and SAPI token promotion.
/// Replaces the functionality of SherpaOnnxConfig.exe.
/// </summary>
public class SherpaModelService
{
    private static readonly string ModelsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceGardenSAPIAdapter", "models");

    /// <summary>
    /// Serialises every archive download/extract/cleanup: the Voices tab can
    /// rescan (TryExtractArchives) while a download is writing the same
    /// archive, and two writers on one file throw sharing violations.
    /// </summary>
    private static readonly SemaphoreSlim ArchiveLock = new(1, 1);

    private const string SapiTokensRoot = @"SOFTWARE\Microsoft\Speech\Voices\Tokens";
    private const string OneCoreTokensRoot = @"SOFTWARE\Microsoft\Speech_OneCore\Voices\Tokens";
    private const string TtsEngineClsid = "{013AB33B-AD1A-401C-8BEE-F6E2B046A94E}";

    public class CatalogModel
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("model_type")] public string ModelType { get; set; } = "vits";
        [JsonPropertyName("sample_rate")] public int? SampleRate { get; set; }
        [JsonPropertyName("url")] public string Url { get; set; } = "";
        [JsonPropertyName("language")] public List<CatalogLanguage>? Language { get; set; }
        [JsonPropertyName("filesize_mb")] public double? FileSizeMb { get; set; }
        [JsonPropertyName("license")] public string? License { get; set; }
        [JsonPropertyName("license_url")] public string? LicenseUrl { get; set; }
        [JsonPropertyName("min_sherpa_onnx_version")] public string? MinSherpaOnnxVersion { get; set; }
        [JsonPropertyName("deprecated")] public bool? Deprecated { get; set; }
        [JsonPropertyName("quality")] public string? Quality { get; set; }
        [JsonPropertyName("quantization")] public string? Quantization { get; set; }
        [JsonPropertyName("num_speakers")] public int? NumSpeakers { get; set; }
    }

    public class CatalogLanguage
    {
        [JsonPropertyName("lang_code")] public string LangCode { get; set; } = "";
        [JsonPropertyName("language_name")] public string LanguageName { get; set; } = "";
        [JsonPropertyName("country")] public string Country { get; set; } = "";
    }

    public class InstalledModel
    {
        public string Id { get; set; } = "";
        public string Directory { get; set; } = "";
        public string? ModelPath { get; set; }
        public string? TokensPath { get; set; }
        public string? DataDir { get; set; }
        public string? VoicesPath { get; set; }
        public string? LexiconPath { get; set; }
        public bool IsPromoted { get; set; }

        /// <summary>
        /// 0=VITS, 1=Matcha, 2=Kokoro
        /// </summary>
        public int ModelType { get; set; } = 0;

        /// <summary>SAPI Gender attribute value: Male/Female/Neutral (Neutral = unknown).</summary>
        public string Gender { get; set; } = "Neutral";

        /// <summary>Registry quality tier (high/medium/low/x_low/int8/fp16), empty when unknown.</summary>
        public string Quality { get; set; } = "";

        /// <summary>Language tag from the catalog (e.g. "urd", "nl_BE", "en"). Empty when unknown.</summary>
        public string Language { get; set; } = "";
    }

    /// <summary>
    /// Load the model catalog from models.json (embedded or sidecar).
    /// Falls back to the pre-0.3.17 merged_models.json sidecar if present.
    /// </summary>
    public static async Task<List<CatalogModel>> LoadCatalogAsync()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "models.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "models.json"),
            Path.Combine(AppContext.BaseDirectory, "x64", "models.json"),
            Path.Combine(AppContext.BaseDirectory, "x86", "models.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "SherpaOnnxConfig", "models.json"),
            // Legacy sidecar from wrapper <= 0.3.16 installs
            Path.Combine(AppContext.BaseDirectory, "merged_models.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "merged_models.json"),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path);

                // The catalog is a dict keyed by model ID: { "id": { ... }, ... }
                var dict = JsonSerializer.Deserialize<Dictionary<string, CatalogModel>>(json);
                if (dict != null && dict.Count > 0)
                    return dict.Values.ToList();

                // Fallback: try as array
                var list = JsonSerializer.Deserialize<List<CatalogModel>>(json);
                return list ?? new();
            }
        }

        return new();
    }

    /// <summary>
    /// Legacy -> canonical model IDs from the sherpa-onnx registry
    /// canonicalisations (2026-08-10 and 2026-08-18 syncs). rust-tts-wrapper
    /// (>= 0.3.17) hard-fails on unknown model IDs, so installed directories
    /// using legacy names are renamed on scan (idempotent; the SAPI adapter
    /// performs the same migration). Pre-2026-08-10 names map straight to
    /// their 2026-08-18 canonical ID.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> LegacyModelIds = new Dictionary<string, string>
    {
        ["cantonese-fs-xiaomaiiwn"] = "cantonese-yue-xiaomaiiwn",
        ["icefall-fs-aishell3"] = "icefall-zh-aishell3",
        ["icefall-fs-baker"] = "icefall-zh-baker",
        ["icefall-fs-en"] = "icefall-zh_en-zh-en",
        ["icefall-fs-ljspeech"] = "icefall-en-ljspeech",
        ["icefall-fs-ljspeech-low"] = "icefall-en-ljspeech-low",
        ["icefall-fs-ljspeech-medium"] = "icefall-en-ljspeech-medium",
        ["inflect-fs-micro-v2"] = "inflect-en-micro-v2",
        ["inflect-fs-nano-v2"] = "inflect-en-nano-v2",
        ["kokoro-en-en-19"] = "kokoro-en-v0_19",
        ["kokoro-zh_en-int8-multi"] = "kokoro-zh_en-int8",
        ["ljs-fs-unknown"] = "ljs-en-ljspeech",
        ["matcha-fs-khadijah"] = "matcha-fa_en-khadijah",
        ["matcha-fs-musa"] = "matcha-fa_en-musa",
        ["melo-fs-en"] = "melo-en-melo-tts",
        ["melo-fs-zh_en"] = "melo-zh_en-melo-tts",
        ["micro-fs-v0_8"] = "micro-en-v0_8",
        ["mimic3-af-google-low"] = "mimic3-af_ZA-google-low",
        ["mimic3-af-google-nwu_low"] = "mimic3-af_ZA-google-low",
        ["mimic3-bn-multi"] = "mimic3-bn-multi_low",
        ["mimic3-el-rapunzelina"] = "mimic3-el_GR-rapunzelina_low",
        ["mimic3-el-rapunzelina_low"] = "mimic3-el_GR-rapunzelina_low",
        ["mimic3-es-m-ailabs_low"] = "mimic3-es_ES-m-low",
        ["mimic3-es-m-low"] = "mimic3-es_ES-m-low",
        ["mimic3-fa-haaniye"] = "mimic3-fa-haaniye_low",
        ["mimic3-fi-harri-tapani"] = "mimic3-fi_FI-harri-tapani",
        ["mimic3-gu-cmu-low"] = "mimic3-gu_IN-cmu-low",
        ["mimic3-hu-diana-low"] = "mimic3-hu_HU-diana-low",
        ["mimic3-ko-kss"] = "mimic3-ko_KO-kss_low",
        ["mimic3-ko-kss_low"] = "mimic3-ko_KO-kss_low",
        ["mimic3-ne-ne-low"] = "mimic3-ne_NP-ne-low",
        ["mimic3-pl-m-ailabs_low"] = "mimic3-pl_PL-m-low",
        ["mimic3-pl-m-low"] = "mimic3-pl_PL-m-low",
        ["mimic3-tn-google-low"] = "mimic3-tn_ZA-google-low",
        ["mimic3-tn-google-nwu_low"] = "mimic3-tn_ZA-google-low",
        ["mimic3-vi-vais1000"] = "mimic3-vi_VN-vais1000_low",
        ["mimic3-vi-vais1000_low"] = "mimic3-vi_VN-vais1000_low",
        ["mini-fs-v0_1-fp16"] = "mini-en-v0_1-fp16",
        ["mini-fs-v0_8"] = "mini-en-v0_8",
        ["nano-fs-v0_1-fp16"] = "nano-en-v0_1-fp16",
        ["nano-fs-v0_2-fp16"] = "nano-en-v0_2-fp16",
        ["nano-fs-v0_8-fp32"] = "nano-en-v0_8",
        ["nano-fs-v0_8-int8"] = "nano-en-v0_8-int8",
        ["piper-ar-SA_dii-high"] = "piper-ar_JO-SA_dii-high",
        ["piper-ar-SA_miro-high"] = "piper-ar_JO-SA_miro-high",
        ["piper-ar-SA_miro_V2-high"] = "piper-ar_JO-SA_miro_V2-high",
        ["piper-ar-kareem-low"] = "piper-ar_JO-kareem-low",
        ["piper-ar-kareem-medium"] = "piper-ar_JO-kareem-medium",
        ["piper-ca-upc_ona-low"] = "piper-ca_ES-upc_ona-low",
        ["piper-ca-upc_ona-medium"] = "piper-ca_ES-upc_ona-medium",
        ["piper-ca-upc_pau-low"] = "piper-ca_ES-upc_pau-low",
        ["piper-cs-jirka-low"] = "piper-cs_CZ-jirka-low",
        ["piper-cs-jirka-medium"] = "piper-cs_CZ-jirka-medium",
        ["piper-cy-bu_tts-medium"] = "piper-cy_GB-bu_tts-medium",
        ["piper-cy-gwryw_gogleddol-medium"] = "piper-cy_GB-gwryw_gogleddol-medium",
        ["piper-da-talesyntese-medium"] = "piper-da_DK-talesyntese-medium",
        ["piper-de-dii-high"] = "piper-de_DE-dii-high",
        ["piper-de-eva_k-low"] = "piper-de_DE-eva_k-low",
        ["piper-de-glados-high"] = "piper-de_DE-glados-high",
        ["piper-de-glados-low"] = "piper-de_DE-glados-low",
        ["piper-de-glados-medium"] = "piper-de_DE-glados-medium",
        ["piper-de-glados_turret-high"] = "piper-de_DE-glados_turret-high",
        ["piper-de-glados_turret-low"] = "piper-de_DE-glados_turret-low",
        ["piper-de-glados_turret-medium"] = "piper-de_DE-glados_turret-medium",
        ["piper-de-karlsson-low"] = "piper-de_DE-karlsson-low",
        ["piper-de-kerstin-low"] = "piper-de_DE-kerstin-low",
        ["piper-de-miro-high"] = "piper-de_DE-miro-high",
        ["piper-de-pavoque-low"] = "piper-de_DE-pavoque-low",
        ["piper-de-ramona-low"] = "piper-de_DE-ramona-low",
        ["piper-de-thorsten-high"] = "piper-de_DE-thorsten-high",
        ["piper-de-thorsten-low"] = "piper-de_DE-thorsten-low",
        ["piper-de-thorsten-medium"] = "piper-de_DE-thorsten-medium",
        ["piper-de-thorsten_emotional-medium"] = "piper-de_DE-thorsten_emotional-medium",
        ["piper-el-rapunzelina-low"] = "piper-el_GR-rapunzelina-low",
        ["piper-en-alan-low"] = "piper-en_GB-alan-low",
        ["piper-en-alan-medium"] = "piper-en_GB-alan-medium",
        ["piper-en-alba-medium"] = "piper-en_GB-alba-medium",
        ["piper-en-amy-low"] = "piper-en_US-amy-low",
        ["piper-en-amy-medium"] = "piper-en_US-amy-medium",
        ["piper-en-arctic-medium"] = "piper-en_US-arctic-medium",
        ["piper-en-aru-medium"] = "piper-en_GB-aru-medium",
        ["piper-en-bryce-medium"] = "piper-en_US-bryce-medium",
        ["piper-en-cori-high"] = "piper-en_GB-cori-high",
        ["piper-en-cori-medium"] = "piper-en_GB-cori-medium",
        ["piper-en-danny-low"] = "piper-en_US-danny-low",
        ["piper-en-dii-high"] = "piper-en_GB-dii-high",
        ["piper-en-glados"] = "piper-en_US-glados",
        ["piper-en-glados-high"] = "piper-en_US-glados-high",
        ["piper-en-hfc_female-medium"] = "piper-en_US-hfc_female-medium",
        ["piper-en-hfc_male-medium"] = "piper-en_US-hfc_male-medium",
        ["piper-en-jenny_dioco-medium"] = "piper-en_GB-jenny_dioco-medium",
        ["piper-en-joe-medium"] = "piper-en_US-joe-medium",
        ["piper-en-john-medium"] = "piper-en_US-john-medium",
        ["piper-en-kathleen-low"] = "piper-en_US-kathleen-low",
        ["piper-en-kristin-medium"] = "piper-en_US-kristin-medium",
        ["piper-en-kusal-medium"] = "piper-en_US-kusal-medium",
        ["piper-en-l2arctic-medium"] = "piper-en_US-l2arctic-medium",
        ["piper-en-lessac-high"] = "piper-en_US-lessac-high",
        ["piper-en-lessac-low"] = "piper-en_US-lessac-low",
        ["piper-en-lessac-medium"] = "piper-en_US-lessac-medium",
        ["piper-en-libritts-high"] = "piper-en_US-libritts-high",
        ["piper-en-libritts_r-medium"] = "piper-en_US-libritts_r-medium",
        ["piper-en-ljspeech-high"] = "piper-en_US-ljspeech-high",
        ["piper-en-ljspeech-medium"] = "piper-en_US-ljspeech-medium",
        ["piper-en-miro-high"] = "piper-en_GB-miro-high",
        ["piper-en-norman-medium"] = "piper-en_US-norman-medium",
        ["piper-en-northern_english_male-medium"] = "piper-en_GB-northern_english_male-medium",
        ["piper-en-reza_ibrahim-medium"] = "piper-en_US-reza_ibrahim-medium",
        ["piper-en-ryan-high"] = "piper-en_US-ryan-high",
        ["piper-en-ryan-low"] = "piper-en_US-ryan-low",
        ["piper-en-ryan-medium"] = "piper-en_US-ryan-medium",
        ["piper-en-sam-medium"] = "piper-en_US-sam-medium",
        ["piper-en-semaine-medium"] = "piper-en_GB-semaine-medium",
        ["piper-en-southern_english_female-low"] = "piper-en_GB-southern_english_female-low",
        ["piper-en-southern_english_female-medium"] = "piper-en_GB-southern_english_female-medium",
        ["piper-en-southern_english_female_medium"] = "piper-en_GB-southern_english_female_medium",
        ["piper-en-southern_english_male-medium"] = "piper-en_GB-southern_english_male-medium",
        ["piper-en-sweetbbak-amy"] = "piper-en_GB-sweetbbak-amy",
        ["piper-en-vctk-medium"] = "piper-en_GB-vctk-medium",
        ["piper-es-ald-medium"] = "piper-es_MX-ald-medium",
        ["piper-es-carlfm-low"] = "piper-es_ES-carlfm-low",
        ["piper-es-claude-high"] = "piper-es_MX-claude-high",
        ["piper-es-daniela-high"] = "piper-es_AR-daniela-high",
        ["piper-es-davefx-medium"] = "piper-es_ES-davefx-medium",
        ["piper-es-miro-high"] = "piper-es_ES-miro-high",
        ["piper-es-sharvard-medium"] = "piper-es_ES-sharvard-medium",
        ["piper-eu-antton-medium"] = "piper-eu_ES-antton-medium",
        ["piper-eu-maider-medium"] = "piper-eu_ES-maider-medium",
        ["piper-fa-amir-medium"] = "piper-fa_IR-amir-medium",
        ["piper-fa-ganji-medium"] = "piper-fa_IR-ganji-medium",
        ["piper-fa-ganji_adabi-medium"] = "piper-fa_IR-ganji_adabi-medium",
        ["piper-fa-gyro-medium"] = "piper-fa_IR-gyro-medium",
        ["piper-fa-reza_ibrahim-medium"] = "piper-fa_IR-reza_ibrahim-medium",
        ["piper-fa-rezahedayatfar-ibrahimwalk"] = "piper-fa_en-rezahedayatfar-ibrahimwalk",
        ["piper-fi-harri-low"] = "piper-fi_FI-harri-low",
        ["piper-fi-harri-medium"] = "piper-fi_FI-harri-medium",
        ["piper-fr-gilles-low"] = "piper-fr_FR-gilles-low",
        ["piper-fr-miro-high"] = "piper-fr_FR-miro-high",
        ["piper-fr-siwis-low"] = "piper-fr_FR-siwis-low",
        ["piper-fr-siwis-medium"] = "piper-fr_FR-siwis-medium",
        ["piper-fr-tjiho-model1"] = "piper-fr_FR-tjiho-model1",
        ["piper-fr-tjiho-model2"] = "piper-fr_FR-tjiho-model2",
        ["piper-fr-tjiho-model3"] = "piper-fr_FR-tjiho-model3",
        ["piper-fr-tom-medium"] = "piper-fr_FR-tom-medium",
        ["piper-fr-upmc-medium"] = "piper-fr_FR-upmc-medium",
        ["piper-fs-glados-medium"] = "piper-es-glados-medium",
        ["piper-fs-haaniye_low"] = "piper-fa-haaniye_low",
        ["piper-hi-pratham-medium"] = "piper-hi_IN-pratham-medium",
        ["piper-hi-priyamvada-medium"] = "piper-hi_IN-priyamvada-medium",
        ["piper-hi-rohan-medium"] = "piper-hi_IN-rohan-medium",
        ["piper-hu-anna-medium"] = "piper-hu_HU-anna-medium",
        ["piper-hu-berta-medium"] = "piper-hu_HU-berta-medium",
        ["piper-hu-imre-medium"] = "piper-hu_HU-imre-medium",
        ["piper-id-news_tts-medium"] = "piper-id_ID-news_tts-medium",
        ["piper-is-bui-medium"] = "piper-is_IS-bui-medium",
        ["piper-is-salka-medium"] = "piper-is_IS-salka-medium",
        ["piper-is-steinn-medium"] = "piper-is_IS-steinn-medium",
        ["piper-is-ugla-medium"] = "piper-is_IS-ugla-medium",
        ["piper-it-dii-high"] = "piper-it_IT-dii-high",
        ["piper-it-miro-high"] = "piper-it_IT-miro-high",
        ["piper-it-paola-medium"] = "piper-it_IT-paola-medium",
        ["piper-it-riccardo-low"] = "piper-it_IT-riccardo-low",
        ["piper-ka-natia-medium"] = "piper-ka_GE-natia-medium",
        ["piper-kk-iseke-low"] = "piper-kk_KZ-iseke-low",
        ["piper-kk-issai-high"] = "piper-kk_KZ-issai-high",
        ["piper-kk-raya-low"] = "piper-kk_KZ-raya-low-int8",
        ["piper-ku-berfin_renas-medium"] = "piper-ku_TR-berfin_renas-medium",
        ["piper-lb-marylux-medium"] = "piper-lb_LU-marylux-medium",
        ["piper-lv-aivars-medium"] = "piper-lv_LV-aivars-medium",
        ["piper-ml-arjun-medium"] = "piper-ml_IN-arjun-medium",
        ["piper-ml-meera-medium"] = "piper-ml_IN-meera-medium",
        ["piper-ne-chitwan-medium"] = "piper-ne_NP-chitwan-medium",
        ["piper-ne-google-low"] = "piper-ne_NP-google-low",
        ["piper-ne-google-medium"] = "piper-ne_NP-google-medium",
        ["piper-nl-alex-medium"] = "piper-nl_NL-alex-medium",
        ["piper-nl-dii-high"] = "piper-nl_NL-dii-high",
        ["piper-nl-miro-high"] = "piper-nl_NL-miro-high",
        ["piper-nl-nathalie-low"] = "piper-nl_BE-nathalie-low",
        ["piper-nl-nathalie-medium"] = "piper-nl_BE-nathalie-medium",
        ["piper-nl-pim-medium"] = "piper-nl_NL-pim-medium",
        ["piper-nl-rdh-low"] = "piper-nl_BE-rdh-low",
        ["piper-nl-rdh-medium"] = "piper-nl_BE-rdh-medium",
        ["piper-nl-ronnie-medium"] = "piper-nl_NL-ronnie-medium",
        ["piper-no-talesyntese-medium"] = "piper-no_NO-talesyntese-medium",
        ["piper-pl-bass-high"] = "piper-pl_PL-bass-high",
        ["piper-pl-darkman-medium"] = "piper-pl_PL-darkman-medium",
        ["piper-pl-gosia-medium"] = "piper-pl_PL-gosia-medium",
        ["piper-pl-jarvis_wg_glos-medium"] = "piper-pl_PL-jarvis_wg_glos-medium",
        ["piper-pl-justyna_wg_glos-medium"] = "piper-pl_PL-justyna_wg_glos-medium",
        ["piper-pl-mc_speech-medium"] = "piper-pl_PL-mc_speech-medium",
        ["piper-pl-meski_wg_glos-medium"] = "piper-pl_PL-meski_wg_glos-medium",
        ["piper-pl-zenski_wg_glos-medium"] = "piper-pl_PL-zenski_wg_glos-medium",
        ["piper-pt-cadu-medium"] = "piper-pt_BR-cadu-medium",
        ["piper-pt-dii-high"] = "piper-pt_PT-dii-high",
        ["piper-pt-edresson-low"] = "piper-pt_BR-edresson-low",
        ["piper-pt-faber-medium"] = "piper-pt_BR-faber-medium",
        ["piper-pt-jeff-medium"] = "piper-pt_BR-jeff-medium",
        ["piper-pt-miro-high"] = "piper-pt_PT-miro-high",
        ["piper-pt-tugao-medium"] = "piper-pt_PT-tugao-medium",
        ["piper-ro-mihai-medium"] = "piper-ro_RO-mihai-medium-int8",
        ["piper-ru-denis-medium"] = "piper-ru_RU-denis-medium",
        ["piper-ru-dmitri-medium"] = "piper-ru_RU-dmitri-medium",
        ["piper-ru-irina-medium"] = "piper-ru_RU-irina-medium",
        ["piper-ru-ruslan-medium"] = "piper-ru_RU-ruslan-medium",
        ["piper-sk-lili-medium"] = "piper-sk_SK-lili-medium",
        ["piper-sl-artur-medium"] = "piper-sl_SI-artur-medium",
        ["piper-sq-edon-medium"] = "piper-sq_AL-edon-medium",
        ["piper-sr-serbski_institut-medium"] = "piper-sr_RS-serbski_institut-medium-int8",
        ["piper-sv-alma-medium"] = "piper-sv_SE-alma-medium",
        ["piper-sv-lisa-medium"] = "piper-sv_SE-lisa-medium",
        ["piper-sv-nst-medium"] = "piper-sv_SE-nst-medium",
        ["piper-sw-lanfrica-medium"] = "piper-sw_CD-lanfrica-medium",
        ["piper-tr-dfki-medium"] = "piper-tr_TR-dfki-medium",
        ["piper-tr-fahrettin-medium"] = "piper-tr_TR-fahrettin-medium",
        ["piper-tr-fettah-medium"] = "piper-tr_TR-fettah-medium",
        ["piper-uk-lada-low"] = "piper-uk_UA-lada-low",
        ["piper-uk-ukrainian_tts-medium"] = "piper-uk_UA-ukrainian_tts-medium",
        ["piper-ur-fasih-medium"] = "piper-ur_PK-fasih-medium",
        ["piper-vi-25hours_single-low"] = "piper-vi_VN-25hours_single-low",
        ["piper-vi-vais1000-medium"] = "piper-vi_VN-vais1000-medium",
        ["piper-vi-vivos-low"] = "piper-vi_VN-vivos-low",
        ["piper-zh-chaowen-medium"] = "piper-zh_CN-chaowen-medium",
        ["piper-zh-huayan-medium"] = "piper-zh_CN-huayan-medium",
        ["piper-zh-xiao_ya-medium"] = "piper-zh_CN-xiao_ya-medium",
        ["tts-fs-khadijah"] = "matcha-fa_en-khadijah",
        ["tts-fs-musa"] = "matcha-fa_en-musa",
        ["vctk-fs-unknown"] = "vctk-en-vctk",
        ["vits-coqui-en-vctk"] = "coqui-en-vctk",
        ["zh-fs-abyssinvoker"] = "zh-zh-abyssinvoker",
        ["zh-fs-bronya"] = "zh-zh-bronya",
        ["zh-fs-doom"] = "zh-zh-doom",
        ["zh-fs-echo"] = "zh-zh-echo",
        ["zh-fs-eula"] = "zh-zh-eula",
        ["zh-fs-fanchen-C"] = "zh-zh-fanchen-C",
        ["zh-fs-fanchen-ZhiHuiLaoZhe"] = "zh-zh-fanchen-ZhiHuiLaoZhe",
        ["zh-fs-fanchen-new"] = "zh-zh-fanchen-new",
        ["zh-fs-fanchen-unity"] = "zh-zh-fanchen-unity",
        ["zh-fs-fanchen-wnj"] = "zh-zh-fanchen-wnj",
        ["zh-fs-keqing"] = "zh-zh-keqing",
        ["zh-fs-theresa"] = "zh-zh-theresa",
        ["zh-fs-unknown"] = "zh-zh-aishell3",
        ["zh-fs-zenyatta"] = "zh-zh-zenyatta",
    };

    /// <summary>
    /// Rename installed model directories still using legacy registry IDs to
    /// their canonical names, so the wrapper's registry lookups succeed and
    /// the catalog matches installed models. Best-effort; locked or in-use
    /// directories are left for the adapter's migration to retry.
    /// </summary>
    private static void MigrateLegacyModelDirs()
    {
        if (!Directory.Exists(ModelsDir)) return;

        foreach (var (legacy, canonical) in LegacyModelIds)
        {
            var legacyDir = Path.Combine(ModelsDir, legacy);
            var canonicalDir = Path.Combine(ModelsDir, canonical);
            if (!Directory.Exists(legacyDir) || Directory.Exists(canonicalDir))
                continue;

            try
            {
                Directory.Move(legacyDir, canonicalDir);
            }
            catch
            {
                // In use or locked — the SAPI adapter retries on voice init.
            }
        }
    }

    /// <summary>
    /// Derive a SAPI Gender attribute (Male/Female/Neutral) for a sherpa model.
    /// The registry carries no gender field, so this uses naming conventions:
    /// - word tokens female/woman/girl (checked first — "female" contains "male")
    ///   and male/man/boy in the id or display name;
    /// - the piper ecosystem's af_/am_ (adult female/male) underscore prefixes;
    /// - mimic3's single-letter m/f (m-ailabs / f-ailabs) variants.
    /// Multi-speaker models and the MMS family (whose ids are language codes and
    /// whose names are language names, e.g. the "Male" language of Ethiopia)
    /// are left Neutral rather than guessed.
    /// </summary>
    public static string DeriveSherpaGender(string id, string name, int numSpeakers)
    {
        if (numSpeakers > 1) return "Neutral";
        var lower = (id + " " + name).ToLowerInvariant();
        if (lower.StartsWith("mms_") || lower.Contains(" mms_")) return "Neutral";

        // Piper af_/am_ (e.g. hand-installed af_amy, am_adam voices). The
        // underscore form matters: "af-" / "am-" segments are Afrikaans /
        // Armenian language codes, not gender markers.
        if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"(?:^|[^a-z0-9])af_[a-z]")) return "Female";
        if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"(?:^|[^a-z0-9])am_[a-z]")) return "Male";

        var tokens = lower.Split(new[] { ' ', '-', '_', '.', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var isFemale = false;
        var isMale = false;
        var hasSingleM = false;
        var hasSingleF = false;
        foreach (var tok in tokens)
        {
            switch (tok)
            {
                case "female":
                case "woman":
                case "girl":
                case "women":
                    isFemale = true;
                    break;
                case "male":
                case "man":
                case "boy":
                case "men":
                    isMale = true;
                    break;
                case "m":
                    hasSingleM = true;
                    break;
                case "f":
                    hasSingleF = true;
                    break;
            }
        }

        if (isFemale) return "Female";
        if (isMale) return "Male";
        // mimic3 m-ailabs (male) / f-ailabs (female) variants
        if (lower.StartsWith("mimic3-"))
        {
            if (hasSingleF) return "Female";
            if (hasSingleM) return "Male";
        }
        return "Neutral";
    }

    /// <summary>
    /// Fill Gender/Quality on installed models from the bundled catalog
    /// (single load), so promotion can write them into the SAPI token
    /// attributes. Models missing from the catalog keep Neutral/empty.
    /// </summary>
    private static void EnrichWithCatalog(List<InstalledModel> models)
    {
        try
        {
            var catalog = LoadCatalogAsync().GetAwaiter().GetResult()
                .ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var model in models)
            {
                if (!catalog.TryGetValue(model.Id, out var cat)) continue;
                model.Quality = cat.Quality ?? "";
                model.Gender = DeriveSherpaGender(cat.Id, cat.Name, cat.NumSpeakers ?? 1);
                model.Language = cat.Language?.FirstOrDefault()?.LangCode
                    ?? cat.Language?.FirstOrDefault()?.LanguageName ?? "";
            }
        }
        catch
        {
            // Catalog unavailable — promote without gender/quality metadata.
        }
    }

    /// <summary>
    /// Scan the local models directory for installed models.
    /// </summary>
    public static List<InstalledModel> ScanInstalledModels()
    {
        var result = new List<InstalledModel>();
        if (!Directory.Exists(ModelsDir)) return result;

        MigrateLegacyModelDirs();

        // Check which are already promoted to HKLM
        var promoted = GetPromotedSherpaTokens();

        foreach (var dir in Directory.GetDirectories(ModelsDir))
        {
            var modelId = Path.GetFileName(dir);
            // A renamed directory may still be referenced by its legacy token name
            var legacyName = LegacyModelIds.FirstOrDefault(kv => kv.Value == modelId).Key;

            // Auto-extract any orphaned .tar.bz2 left from a failed/aborted extraction
            TryExtractArchives(dir);

            var installed = new InstalledModel
            {
                Id = modelId,
                Directory = dir,
                IsPromoted = promoted.Contains($"Sherpa-{modelId}")
                    || (legacyName != null && promoted.Contains($"Sherpa-{legacyName}")),
            };

            // Find model.onnx (could be in nested dir for Piper)
            var onnxFiles = System.IO.Directory.GetFiles(dir, "*.onnx", SearchOption.AllDirectories);
            if (onnxFiles.Length > 0)
            {
                // Prefer model.onnx over other names
                installed.ModelPath = onnxFiles.FirstOrDefault(f => Path.GetFileName(f).Equals("model.onnx", StringComparison.OrdinalIgnoreCase))
                    ?? onnxFiles[0];
                var modelDir = Path.GetDirectoryName(installed.ModelPath)!;

                var tokensPath = Path.Combine(modelDir, "tokens.txt");
                if (File.Exists(tokensPath))
                    installed.TokensPath = tokensPath;

                var dataDir = Path.Combine(modelDir, "espeak-ng-data");
                if (Directory.Exists(dataDir))
                    installed.DataDir = dataDir;

                var voicesPath = Path.Combine(modelDir, "voices.bin");
                if (File.Exists(voicesPath))
                    installed.VoicesPath = voicesPath;

                var lexiconPath = Path.Combine(modelDir, "lexicon.txt");
                if (File.Exists(lexiconPath))
                    installed.LexiconPath = lexiconPath;

                // Detect model type: Kokoro has voices.bin, Matcha has vocoder.onnx
                if (installed.VoicesPath != null || modelId.StartsWith("kokoro-"))
                    installed.ModelType = 2; // Kokoro
                else if (onnxFiles.Any(f => Path.GetFileName(f).Contains("vocoder")))
                    installed.ModelType = 1; // Matcha
                else
                    installed.ModelType = 0; // VITS

                // Best-effort: give sherpa-layout voices a piper sidecar so
                // the floravox engine (measured timing, lexicon G2P) can load
                // them. Re-runs when tokens.txt is newer than the sidecar.
                PiperSidecarGenerator.EnsureSidecar(modelDir, modelId);
            }

            result.Add(installed);
        }

        return result;
    }

    /// <summary>
    /// If a .tar.bz2 or .tar exists in the directory but no .onnx is present yet,
    /// extract it. Self-heals downloads that completed but never extracted.
    /// Uses built-in SharpCompress — no 7-Zip dependency.
    /// </summary>
    private static void TryExtractArchives(string dir)
    {
        // Downloads stage as *.part until complete — never touch those.
        if (Directory.GetFiles(dir, "*.onnx", SearchOption.AllDirectories).Length > 0)
        {
            // Clean up any leftover archives from a partial extraction
            foreach (var f in Directory.GetFiles(dir, "*.tar.bz2", SearchOption.TopDirectoryOnly))
                TryDelete(f);
            foreach (var f in Directory.GetFiles(dir, "*.tar", SearchOption.TopDirectoryOnly))
                TryDelete(f);
            return;
        }

        ArchiveLock.Wait();
        try
        {
            var bz2 = Directory.GetFiles(dir, "*.tar.bz2", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (bz2 != null)
            {
                var tarFile = bz2.Replace(".tar.bz2", ".tar");
                // Stage 1: bz2 → tar using SharpCompress
                try { ExtractBz2(bz2, tarFile); } catch { return; }
                // Stage 2: tar → contents
                if (File.Exists(tarFile))
                {
                    try { ExtractTar(tarFile, dir); } catch { }
                    TryDelete(tarFile);
                }
                TryDelete(bz2);
            }
            else
            {
                // Lone .tar
                var tar = Directory.GetFiles(dir, "*.tar", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (tar != null)
                {
                    try { ExtractTar(tar, dir); } catch { }
                    TryDelete(tar);
                }
            }
        }
        finally
        {
            ArchiveLock.Release();
        }
    }

    /// <summary>Extract a .bz2 file to an output file using SharpCompress.</summary>
    private static void ExtractBz2(string bz2Path, string outputPath)
    {
        using var input = File.OpenRead(bz2Path);
        using var decompressor = SharpCompress.Compressors.BZip2.BZip2Stream.Create(
            input, SharpCompress.Compressors.CompressionMode.Decompress, false, false);
        using var output = File.Create(outputPath);
        decompressor.CopyTo(output);
    }

    /// <summary>Extract a .tar archive to a directory using SharpCompress.</summary>
    private static void ExtractTar(string tarPath, string destDir)
    {
        using var archive = SharpCompress.Archives.Tar.TarArchive.OpenArchive(tarPath);
        foreach (var entry in archive.Entries)
        {
            if (!entry.IsDirectory)
            {
                using var entryStream = entry.OpenEntryStream();
                var fullPath = Path.Combine(destDir, entry.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                using var fileStream = File.Create(fullPath);
                entryStream.CopyTo(fileStream);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void SafeDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length == 0)
                Directory.Delete(dir, recursive: true);
        }
        catch { }
    }

    /// <summary>
    /// Download a model from the catalog URL.
    /// Handles two URL patterns:
    ///   1. Archive URL (ends in .tar.bz2 or .tar) — download + extract with 7-Zip
    ///   2. HuggingFace directory URL (no file extension) — download individual files
    ///      (model.onnx, tokens.txt) from that directory. Used by MMS models.
    /// </summary>
    public static async Task DownloadModelAsync(CatalogModel model, IProgress<(int percent, string status)>? progress = null)
    {
        if (string.IsNullOrEmpty(model.Url))
            throw new InvalidOperationException($"Model {model.Id} has no download URL");

        var destDir = Path.Combine(ModelsDir, model.Id);
        var lastSegment = model.Url.Split('/').Last();

        // Route 1: HuggingFace directory (MMS models) — no archive extension
        var isArchive = lastSegment.EndsWith(".tar.bz2") || lastSegment.EndsWith(".tar");
        if (!isArchive)
        {
            await DownloadHfDirectoryAsync(model.Url, destDir, model.Id, progress);
            return;
        }

        // Route 2: Single archive download + extract.
        // Downloads stage as "<name>.part" so directory scans never see a
        // partial archive, then extract under the archive lock (scans and
        // double-clicks used to race the same files and fail with
        // "being used by another process").
        var destFile = Path.Combine(destDir, lastSegment);
        var partFile = destFile + ".part";
        progress?.Report((0, $"Connecting to {lastSegment}..."));

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        using var response = await http.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            SafeDeleteDir(destDir);
            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} {response.StatusCode} for {lastSegment}");
        }

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        var totalMb = totalBytes > 0 ? totalBytes / (1024.0 * 1024.0) : 0;

        Directory.CreateDirectory(destDir);
        TryDelete(partFile);
        using var contentStream = await response.Content.ReadAsStreamAsync();
        using (var fileStream = File.Create(partFile))
        {
            var buffer = new byte[81920];
            long bytesRead = 0;
            int read;
            var lastReport = DateTime.UtcNow;

            while ((read = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read));
                bytesRead += read;
                if (totalBytes > 0)
                {
                    var now = DateTime.UtcNow;
                    if ((now - lastReport).TotalMilliseconds >= 250 || bytesRead == totalBytes)
                    {
                        lastReport = now;
                        var pct = (int)(bytesRead * 100 / totalBytes);
                        var doneMb = bytesRead / (1024.0 * 1024.0);
                        progress?.Report((pct, $"{pct}% ({doneMb:F0}/{totalMb:F0}MB)"));
                    }
                }
                else
                {
                    var doneMb = bytesRead / (1024.0 * 1024.0);
                    var now = DateTime.UtcNow;
                    if ((now - lastReport).TotalMilliseconds >= 500)
                    {
                        lastReport = now;
                        progress?.Report((0, $"{doneMb:F0}MB downloaded"));
                    }
                }
            }
        }

        // Download complete — promote the .part to the real archive name and
        // extract under the lock.
        await ArchiveLock.WaitAsync();
        try
        {
            TryDelete(destFile);
            File.Move(partFile, destFile);

            // Extract the archive using built-in SharpCompress (no 7-Zip needed)
            progress?.Report((100, "Extracting..."));
            if (lastSegment.EndsWith(".tar.bz2"))
            {
                var tarFile = destFile.Replace(".tar.bz2", ".tar");
                ExtractBz2(destFile, tarFile);
                if (File.Exists(tarFile))
                    ExtractTar(tarFile, destDir);
                TryDelete(tarFile);
            }
            else if (lastSegment.EndsWith(".tar"))
            {
                ExtractTar(destFile, destDir);
            }
            TryDelete(destFile);
        }
        catch (IOException ex) when (ex.Message.Contains("being used by another process"))
        {
            throw new InvalidOperationException(
                "The model's files are open in another download or scan — wait a moment and try again. " +
                "If it keeps failing, close and reopen VoiceGarden.", ex);
        }
        finally
        {
            ArchiveLock.Release();
        }

        // Zipvoice archives do not bundle the vocoder — fetch it into the
        // shared models dir so synthesis works out of the box.
        await EnsureZipvoiceVocoderAsync(model, progress);

        // Generate the piper sidecar while the catalog entry (with the
        // sample rate) is at hand, so the floravox engine can pick the voice
        // up on the next scan/promotion without needing sherpa layout.
        var extractedModelDir = FindExtractedModelDir(destDir);
        if (extractedModelDir != null)
            PiperSidecarGenerator.EnsureSidecar(extractedModelDir, model.Id, model.SampleRate);

        progress?.Report((100, "Done"));
    }

    /// <summary>The .onnx-bearing directory inside a freshly extracted model dir (flat or nested).</summary>
    private static string? FindExtractedModelDir(string modelDir)
    {
        var onnx = Directory.GetFiles(modelDir, "*.onnx", SearchOption.AllDirectories)
            .FirstOrDefault(f => !Path.GetFileName(f).Contains("vocoder", StringComparison.OrdinalIgnoreCase));
        return onnx is null ? null : Path.GetDirectoryName(onnx);
    }

    /// <summary>
    /// Zipvoice models need the vocos_24khz.onnx vocoder, which lives in a
    /// separate sherpa-onnx release and is resolved from the models base dir
    /// by the wrapper. Download it once and reuse for every zipvoice model.
    /// </summary>
    private static async Task EnsureZipvoiceVocoderAsync(CatalogModel model,
        IProgress<(int percent, string status)>? progress)
    {
        if (!string.Equals(model.ModelType, "zipvoice", StringComparison.OrdinalIgnoreCase))
            return;

        var vocoderPath = Path.Combine(ModelsDir, "vocos_24khz.onnx");
        if (File.Exists(vocoderPath) && new FileInfo(vocoderPath).Length > 0)
            return;

        const string vocoderUrl =
            "https://github.com/k2-fsa/sherpa-onnx/releases/download/vocoder-models/vocos_24khz.onnx";
        progress?.Report((100, "Fetching vocos vocoder (needed by zipvoice)..."));
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using var response = await http.GetAsync(vocoderUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using var src = await response.Content.ReadAsStreamAsync();
            await using var dst = File.Create(vocoderPath);
            await src.CopyToAsync(dst);
        }
        catch
        {
            // Leave it missing — the wrapper errors with the download URL.
            try { if (File.Exists(vocoderPath)) File.Delete(vocoderPath); } catch { }
        }
    }

    /// <summary>
    /// Download individual files from a HuggingFace directory URL.
    /// MMS models are stored as directories with model.onnx, tokens.txt, etc.
    /// </summary>
    private static async Task DownloadHfDirectoryAsync(string baseUrl, string destDir, string modelId,
        IProgress<(int percent, string status)>? progress)
    {
        // Files to download for MMS models (in priority order)
        var files = new[] { "model.onnx", "tokens.txt", "lexicon.txt", "espeak-ng-data" };

        Directory.CreateDirectory(destDir);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

        // First, probe which files exist by trying to download model.onnx (required)
        var modelUrl = $"{baseUrl}/model.onnx";
        progress?.Report((0, $"Connecting to {modelId}/model.onnx..."));

        using var modelResp = await http.GetAsync(modelUrl, HttpCompletionOption.ResponseHeadersRead);
        if (!modelResp.IsSuccessStatusCode)
        {
            SafeDeleteDir(destDir);
            throw new HttpRequestException(
                $"HTTP {(int)modelResp.StatusCode} {modelResp.StatusCode} for {modelId}/model.onnx");
        }

        // Download model.onnx with progress (this is the big file)
        await DownloadFileWithProgressAsync(http, modelResp, Path.Combine(destDir, "model.onnx"),
            "model.onnx", progress);

        // Download tokens.txt (required for MMS)
        var tokensUrl = $"{baseUrl}/tokens.txt";
        progress?.Report((100, "Downloading tokens.txt..."));
        try
        {
            await DownloadFileAsync(http, tokensUrl, Path.Combine(destDir, "tokens.txt"));
        }
        catch
        {
            // tokens.txt might not exist for all models — non-fatal
        }

        // Try optional files: lexicon.txt
        foreach (var optFile in new[] { "lexicon.txt" })
        {
            try
            {
                progress?.Report((100, $"Checking {optFile}..."));
                await DownloadFileAsync(http, $"{baseUrl}/{optFile}", Path.Combine(destDir, optFile));
            }
            catch { /* optional */ }
        }

        progress?.Report((100, "Done"));
    }

    private static async Task DownloadFileWithProgressAsync(
        HttpClient http, HttpResponseMessage response, string destPath, string fileName,
        IProgress<(int percent, string status)>? progress)
    {
        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        var totalMb = totalBytes > 0 ? totalBytes / (1024.0 * 1024.0) : 0;

        using var contentStream = await response.Content.ReadAsStreamAsync();
        using var fileStream = File.Create(destPath);

        var buffer = new byte[81920];
        long bytesRead = 0;
        int read;
        var lastReport = DateTime.UtcNow;

        while ((read = await contentStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read));
            bytesRead += read;
            if (totalBytes > 0)
            {
                var now = DateTime.UtcNow;
                if ((now - lastReport).TotalMilliseconds >= 250 || bytesRead == totalBytes)
                {
                    lastReport = now;
                    var pct = (int)(bytesRead * 100 / totalBytes);
                    var doneMb = bytesRead / (1024.0 * 1024.0);
                    progress?.Report((pct, $"{pct}% ({doneMb:F0}/{totalMb:F0}MB)"));
                }
            }
        }
    }

    private static async Task DownloadFileAsync(HttpClient http, string url, string destPath)
    {
        using var resp = await http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync();
        using var file = File.Create(destPath);
        await stream.CopyToAsync(file);
    }

    /// <summary>
    /// Promote all downloaded models to HKLM as SAPI tokens.
    /// </summary>
    public static (int promoted, int failed) PromoteAll(bool compatEnUs = false)
    {
        var models = ScanInstalledModels();
        EnrichWithCatalog(models);
        int promoted = 0, failed = 0;
        var errors = new List<string>();

        foreach (var model in models.Where(m => m.ModelPath != null))
        {
            try
            {
                PromoteSherpaModel(model);
                promoted++;
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{model.Id}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            try
            {
                var logPath = Path.Combine(Path.GetTempPath(), "VoiceGarden_promote_errors.log");
                File.WriteAllLines(logPath, errors);
            }
            catch { }
        }

        return (promoted, failed);
    }

    /// <summary>
    /// Generate a .reg file for all installed models and import it elevated.
    /// Much faster than relaunching the 116MB single-file exe.
    /// Returns (promoted, failed, errorMessage).
    /// </summary>
    public static (int promoted, int failed, string error) PromoteAllElevated()
    {
        var models = ScanInstalledModels().Where(m => m.ModelPath != null).ToList();
        if (models.Count == 0)
            return (0, 0, "No downloaded models found with a valid model.onnx");

        EnrichWithCatalog(models);

        // Generate .reg file in a shared location (C:\ProgramData) so the elevated
        // process (which may run as a different admin user) can read it.
        var regDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VoiceGardenSAPIAdapter");
        Directory.CreateDirectory(regDir);
        var regPath = Path.Combine(regDir, "promote.reg");
        var lines = new List<string> { "Windows Registry Editor Version 5.00", "" };

        foreach (var model in models)
        {
            AppendModelToReg(lines, model);
        }

        File.WriteAllLines(regPath, lines);

        // Import with reg.exe elevated
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("reg.exe", $"import \"{regPath}\"")
            {
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            };
            var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(30000);
            var rc = p?.ExitCode ?? -1;

            TryDelete(regPath);

            if (rc == 0)
                return (models.Count, 0, "");
            return (0, models.Count, $"reg import exited with code {rc}");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            TryDelete(regPath);
            return (0, 0, "UAC cancelled");
        }
        catch (Exception ex)
        {
            TryDelete(regPath);
            return (0, models.Count, ex.Message);
        }
    }

    /// <summary>
    /// Scan installed models and return one (enriched with catalog gender /
    /// quality) by id, or null when not installed.
    /// </summary>
    public static InstalledModel? GetInstalledModel(string modelId)
    {
        var models = ScanInstalledModels().Where(m => m.ModelPath != null).ToList();
        var model = models.FirstOrDefault(m => m.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase));
        if (model == null) return null;
        EnrichWithCatalog(models);
        return model;
    }

    /// <summary>
    /// Generate a .reg file for a specific set of installed models and import
    /// it elevated. Used by the Voices tab for single/bulk promotion of
    /// selected models without requiring the whole install to be promoted.
    /// Returns (promoted, failed, errorMessage).
    /// </summary>
    public static (int promoted, int failed, string error) PromoteModelsElevated(IEnumerable<string> modelIds)
    {
        var wanted = new HashSet<string>(modelIds, StringComparer.OrdinalIgnoreCase);
        var models = ScanInstalledModels()
            .Where(m => m.ModelPath != null && wanted.Contains(m.Id))
            .ToList();
        if (models.Count == 0)
            return (0, wanted.Count, "No downloaded models found with a valid model.onnx");

        EnrichWithCatalog(models);

        var regDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VoiceGardenSAPIAdapter");
        Directory.CreateDirectory(regDir);
        var regPath = Path.Combine(regDir, "promote_selected.reg");
        var lines = new List<string> { "Windows Registry Editor Version 5.00", "" };

        foreach (var model in models)
            AppendModelToReg(lines, model);

        File.WriteAllLines(regPath, lines);

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("reg.exe", $"import \"{regPath}\"")
            {
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            };
            var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(30000);
            var rc = p?.ExitCode ?? -1;

            TryDelete(regPath);

            if (rc == 0)
                return (models.Count, 0, "");
            return (0, models.Count, $"reg import exited with code {rc}");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            TryDelete(regPath);
            return (0, 0, "UAC cancelled");
        }
        catch (Exception ex)
        {
            TryDelete(regPath);
            return (0, models.Count, ex.Message);
        }
    }

    /// <summary>
    /// Resolve a model's catalog language to SAPI attributes; unresolvable
    /// languages stay en-US (the historical behaviour, and the fallback apps
    /// understand).
    /// </summary>
    private static (string locale, string langId) ResolveModelLocale(InstalledModel model) =>
        SapiLanguage.TryResolve(model.Language, out var locale, out var langId)
            ? (locale, langId)
            : (SapiLanguage.EnUsLocale, SapiLanguage.EnUsLangId);

    private static void AppendModelToReg(List<string> lines, InstalledModel model)
    {
        var (locale, langId) = ResolveModelLocale(model);

        // Alias tokens are rebuilt from scratch on every promote so toggling
        // the settings in Advanced takes effect next time voices are installed.
        foreach (var alias in SapiAliasSettings.AliasesFor(model.Language))
        {
            lines.Add($"[-HKEY_LOCAL_MACHINE\\{SapiTokensRoot}\\Sherpa-{model.Id}{alias.suffix}]");
            lines.Add($"[-HKEY_LOCAL_MACHINE\\{OneCoreTokensRoot}\\Sherpa-{model.Id}{alias.suffix}]");
        }

        AppendSherpaTokenToReg(lines, model, $"Sherpa-{model.Id}", $"Sherpa {model.Id}",
            locale, langId, aliasMarker: null);

        foreach (var alias in SapiAliasSettings.AliasesFor(model.Language))
        {
            AppendSherpaTokenToReg(lines, model, $"Sherpa-{model.Id}{alias.suffix}",
                $"Sherpa {model.Id} ({alias.marker} alias)", alias.locale, alias.langId, alias.marker);
        }
    }

    private static void AppendSherpaTokenToReg(List<string> lines, InstalledModel model, string tokenName,
        string friendlyName, string locale, string langId, string? aliasMarker)
    {
        var tokenPath = $@"HKEY_LOCAL_MACHINE\{SapiTokensRoot}\{tokenName}";

        // Main token key
        lines.Add($"[{tokenPath}]");
        lines.Add($"@=\"{friendlyName}\"");
        lines.Add($"\"CLSID\"=\"{TtsEngineClsid}\"");
        if (aliasMarker != null)
            lines.Add($"\"{SapiLanguage.AliasMarkerValue}\"=\"{aliasMarker}\"");

        // VoiceGardenConfig subkey
        lines.Add($"[{tokenPath}\\VoiceGardenConfig]");
        lines.Add("\"EngineType\"=\"Sherpa\"");
        lines.Add($"\"SherpaOnnxModelType\"=dword:{model.ModelType:X8}");
        lines.Add($"\"SherpaOnnxModelPath\"=\"{EscapeRegPath(model.ModelPath!)}\"");
        if (model.TokensPath != null)
            lines.Add($"\"SherpaOnnxTokens\"=\"{EscapeRegPath(model.TokensPath)}\"");
        if (model.DataDir != null)
            lines.Add($"\"SherpaOnnxDataDir\"=\"{EscapeRegPath(model.DataDir)}\"");
        if (model.VoicesPath != null)
            lines.Add($"\"SherpaOnnxVoices\"=\"{EscapeRegPath(model.VoicesPath)}\"");
        if (model.LexiconPath != null)
            lines.Add($"\"SherpaOnnxLexicon\"=\"{EscapeRegPath(model.LexiconPath)}\"");

        // Attributes subkey
        lines.Add($"[{tokenPath}\\Attributes]");
        lines.Add($"\"Name\"=\"{model.Id}\"");
        lines.Add($"\"Gender\"=\"{model.Gender}\"");
        lines.Add("\"Age\"=\"Adult\"");
        lines.Add($"\"Language\"=\"{langId}\"");
        lines.Add($"\"Locale\"=\"{locale}\"");
        lines.Add("\"Vendor\"=\"K2FSA\"");
        lines.Add("\"VoiceGardenType\"=\"Sherpa;Offline\"");
        if (!string.IsNullOrEmpty(model.Quality) && model.Quality != "unknown")
            lines.Add($"\"Quality\"=\"{model.Quality}\"");

        // Also register in Speech_OneCore for Chrome/Edge support
        var oneCorePath = $@"HKEY_LOCAL_MACHINE\{OneCoreTokensRoot}\{tokenName}";
        lines.Add($"[{oneCorePath}]");
        lines.Add($"@=\"{friendlyName}\"");
        lines.Add($"\"CLSID\"=\"{TtsEngineClsid}\"");
        lines.Add($"[{oneCorePath}\\VoiceGardenConfig]");
        lines.Add("\"EngineType\"=\"Sherpa\"");
        lines.Add($"\"SherpaOnnxModelType\"=dword:{model.ModelType:X8}");
        lines.Add($"\"SherpaOnnxModelPath\"=\"{EscapeRegPath(model.ModelPath!)}\"");
        if (model.TokensPath != null)
            lines.Add($"\"SherpaOnnxTokens\"=\"{EscapeRegPath(model.TokensPath)}\"");
        if (model.DataDir != null)
            lines.Add($"\"SherpaOnnxDataDir\"=\"{EscapeRegPath(model.DataDir)}\"");
        if (model.VoicesPath != null)
            lines.Add($"\"SherpaOnnxVoices\"=\"{EscapeRegPath(model.VoicesPath)}\"");
        if (model.LexiconPath != null)
            lines.Add($"\"SherpaOnnxLexicon\"=\"{EscapeRegPath(model.LexiconPath)}\"");
        lines.Add($"[{oneCorePath}\\Attributes]");
        lines.Add($"\"Name\"=\"{model.Id}\"");
        lines.Add($"\"Gender\"=\"{model.Gender}\"");
        lines.Add("\"Age\"=\"Adult\"");
        lines.Add($"\"Language\"=\"{langId}\"");
        lines.Add($"\"Locale\"=\"{locale}\"");
        lines.Add("\"Vendor\"=\"K2FSA\"");
        if (!string.IsNullOrEmpty(model.Quality) && model.Quality != "unknown")
            lines.Add($"\"Quality\"=\"{model.Quality}\"");
        lines.Add("");
    }

    private static string EscapeRegPath(string path) => path.Replace("\\", "\\\\");

    /// <summary>
    /// Promote a single SherpaOnnx model to HKLM: a primary token carrying
    /// the model's real language plus alias tokens per the Advanced settings.
    /// </summary>
    public static void PromoteSherpaModel(InstalledModel model)
    {
        if (model.ModelPath == null) return;

        var (locale, langId) = ResolveModelLocale(model);

        // Drop stale aliases first so settings changes apply on re-promote
        foreach (var alias in SapiAliasSettings.AliasesFor(model.Language))
            DeleteSherpaTokenPair($"Sherpa-{model.Id}{alias.suffix}");

        WriteSherpaToken(SapiTokensRoot, model, $"Sherpa-{model.Id}", $"Sherpa {model.Id}",
            locale, langId, aliasMarker: null);
        WriteSherpaToken(OneCoreTokensRoot, model, $"Sherpa-{model.Id}", $"Sherpa {model.Id}",
            locale, langId, aliasMarker: null);

        foreach (var alias in SapiAliasSettings.AliasesFor(model.Language))
        {
            WriteSherpaToken(SapiTokensRoot, model, $"Sherpa-{model.Id}{alias.suffix}",
                $"Sherpa {model.Id} ({alias.marker} alias)", alias.locale, alias.langId, alias.marker);
            WriteSherpaToken(OneCoreTokensRoot, model, $"Sherpa-{model.Id}{alias.suffix}",
                $"Sherpa {model.Id} ({alias.marker} alias)", alias.locale, alias.langId, alias.marker);
        }
    }

    private static void WriteSherpaToken(string tokensRoot, InstalledModel model, string tokenName,
        string friendlyName, string locale, string langId, string? aliasMarker)
    {
        var tokenPath = $@"{tokensRoot}\{tokenName}";

        using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(tokenPath, writable: true)
            ?? throw new InvalidOperationException("Cannot create HKLM token (admin required)");

        key.SetValue("", friendlyName, Microsoft.Win32.RegistryValueKind.String);
        key.SetValue("CLSID", TtsEngineClsid, Microsoft.Win32.RegistryValueKind.String);
        if (aliasMarker != null)
            key.SetValue(SapiLanguage.AliasMarkerValue, aliasMarker, Microsoft.Win32.RegistryValueKind.String);

        using var config = key.CreateSubKey("VoiceGardenConfig", writable: true);
        config.SetValue("EngineType", "Sherpa", Microsoft.Win32.RegistryValueKind.String);
        config.SetValue("SherpaOnnxModelType", model.ModelType, Microsoft.Win32.RegistryValueKind.DWord);
        config.SetValue("SherpaOnnxModelPath", model.ModelPath, Microsoft.Win32.RegistryValueKind.String);
        if (model.TokensPath != null)
            config.SetValue("SherpaOnnxTokens", model.TokensPath, Microsoft.Win32.RegistryValueKind.String);
        if (model.DataDir != null)
            config.SetValue("SherpaOnnxDataDir", model.DataDir, Microsoft.Win32.RegistryValueKind.String);
        if (model.VoicesPath != null)
            config.SetValue("SherpaOnnxVoices", model.VoicesPath, Microsoft.Win32.RegistryValueKind.String);
        if (model.LexiconPath != null)
            config.SetValue("SherpaOnnxLexicon", model.LexiconPath, Microsoft.Win32.RegistryValueKind.String);

        using var attrs = key.CreateSubKey("Attributes", writable: true);
        attrs.SetValue("Name", model.Id, Microsoft.Win32.RegistryValueKind.String);
        attrs.SetValue("Gender", model.Gender, Microsoft.Win32.RegistryValueKind.String);
        attrs.SetValue("Age", "Adult", Microsoft.Win32.RegistryValueKind.String);
        attrs.SetValue("Language", langId, Microsoft.Win32.RegistryValueKind.String);
        attrs.SetValue("Locale", locale, Microsoft.Win32.RegistryValueKind.String);
        attrs.SetValue("Vendor", "K2FSA", Microsoft.Win32.RegistryValueKind.String);
        attrs.SetValue("VoiceGardenType", "Sherpa;Offline", Microsoft.Win32.RegistryValueKind.String);
        if (!string.IsNullOrEmpty(model.Quality) && model.Quality != "unknown")
            attrs.SetValue("Quality", model.Quality, Microsoft.Win32.RegistryValueKind.String);
        else
            attrs.DeleteValue("Quality", throwOnMissingValue: false);
    }

    /// <summary>
    /// Remove a SherpaOnnx voice (and any of its alias tokens) from HKLM.
    /// </summary>
    public static void UnpromoteSherpaModel(string modelId)
    {
        DeleteSherpaTokenPair($"Sherpa-{modelId}");
        DeleteSherpaTokenPair($"Sherpa-{modelId}{SapiLanguage.EnUsAliasSuffix}");
        DeleteSherpaTokenPair($"Sherpa-{modelId}{SapiLanguage.ArabicAliasSuffix}");
    }

    private static void DeleteSherpaTokenPair(string tokenName)
    {
        try
        {
            Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(
                $@"{SapiTokensRoot}\{tokenName}", throwOnMissingSubKey: false);
            Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(
                $@"{OneCoreTokensRoot}\{tokenName}", throwOnMissingSubKey: false);
        }
        catch { }
    }

    public static HashSet<string> GetPromotedSherpaTokens()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(SapiTokensRoot);
        if (key == null) return result;
        foreach (var name in key.GetSubKeyNames())
        {
            if (name.StartsWith("Sherpa-", StringComparison.OrdinalIgnoreCase))
                result.Add(name);
        }
        return result;
    }

    public static string GetModelsDir() => ModelsDir;
}
