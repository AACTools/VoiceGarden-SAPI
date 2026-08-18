using System;
using System.Collections.Generic;

namespace VoiceGarden.UI.Services;

/// <summary>
/// Maps catalog/engine language tags to SAPI token attributes (Locale name +
/// hex LANGID) and decides alias behaviour for non-English voices.
///
/// SAPI's Attributes\Language value is one or more hex LANGIDs without
/// leading zeros ("409" = en-US, "1401" = ar-SA). When a language cannot be
/// resolved the caller falls back to en-US so English-only apps keep seeing
/// the voice.
/// </summary>
public static class SapiLanguage
{
    public const string EnUsLocale = "en-US";
    public const string EnUsLangId = "409";
    public const string ArabicLocale = "ar-SA";
    public const string ArabicLangId = "1401";

    /// <summary>Marker value written on alias tokens so they can be identified and cleaned up.</summary>
    public const string AliasMarkerValue = "VoiceGardenAlias";

    public const string EnUsAliasSuffix = "-enUS";
    public const string ArabicAliasSuffix = "-ar";

    /// <summary>
    /// Locale → hex LANGID for the languages present in the SherpaOnnx
    /// catalog and common cloud-voice locales. Keys are lowercase.
    /// </summary>
    private static readonly Dictionary<string, string> LocaleToLangId = new(StringComparer.OrdinalIgnoreCase)
    {
        // English
        ["en"] = "409", ["en-us"] = "409", ["en-us0"] = "409",
        ["en-gb"] = "809", ["en-au"] = "C09", ["en-in"] = "4009", ["en-ie"] = "1809", ["en-za"] = "1C09",
        ["eng"] = "409",
        // Chinese
        ["zh"] = "804", ["zho"] = "804", ["cmn"] = "804", ["zh-cn"] = "804",
        ["zh-tw"] = "404", ["zh-hk"] = "C04", ["zh-yue"] = "C04", ["yue"] = "C04",
        // Germanic
        ["de"] = "407", ["deu"] = "407", ["ger"] = "407", ["de-at"] = "C07", ["de-ch"] = "807",
        ["nl"] = "413", ["nld"] = "413", ["nl-nl"] = "413", ["nl-be"] = "813",
        ["sv"] = "41D", ["swe"] = "41D", ["da"] = "406", ["dan"] = "406",
        ["nb"] = "414", ["no"] = "414", ["nob"] = "414", ["nor"] = "414", ["fi"] = "40B", ["fin"] = "40B",
        ["is"] = "40F", ["isl"] = "40F", ["af"] = "436", ["afr"] = "436", ["yi"] = "43D",
        // Romance
        ["es"] = "C0A", ["spa"] = "C0A", ["es-es"] = "C0A", ["es-mx"] = "80A", ["es-ar"] = "2C0A", ["es-us"] = "540A",
        ["fr"] = "40C", ["fra"] = "40C", ["fre"] = "40C", ["fr-ca"] = "C0C",
        ["it"] = "410", ["ita"] = "410",
        ["pt"] = "416", ["por"] = "416", ["pt-br"] = "416", ["pt-pt"] = "816",
        ["ro"] = "418", ["ron"] = "418", ["rum"] = "418", ["ca"] = "403", ["cat"] = "403",
        ["gl"] = "456", ["glg"] = "456", ["eu"] = "42D", ["eus"] = "42D", ["baq"] = "42D",
        // Slavic
        ["ru"] = "419", ["rus"] = "419", ["uk"] = "422", ["ukr"] = "422",
        ["pl"] = "415", ["pol"] = "415", ["cs"] = "405", ["ces"] = "405", ["cze"] = "405",
        ["sk"] = "41B", ["slk"] = "41B", ["sl"] = "424", ["slv"] = "424",
        ["bg"] = "402", ["bul"] = "402", ["hr"] = "41A", ["hrv"] = "41A",
        ["sr"] = "1C1A", ["srp"] = "1C1A", ["bs"] = "141A", ["bos"] = "141A",
        ["mk"] = "42F", ["mkd"] = "42F",
        // Balkans / others
        ["sq"] = "41C", ["sqi"] = "41C", ["alb"] = "41C", ["el"] = "408", ["ell"] = "408", ["gre"] = "408",
        ["hu"] = "40E", ["hun"] = "40E",
        // South Asian
        ["hi"] = "439", ["hin"] = "439", ["bn"] = "445", ["ben"] = "445", ["bn-bd"] = "845",
        ["ta"] = "449", ["tam"] = "449", ["te"] = "44A", ["tel"] = "44A",
        ["ml"] = "44C", ["mal"] = "44C", ["kn"] = "44B", ["kan"] = "44B",
        ["gu"] = "447", ["guj"] = "447", ["mr"] = "44E", ["mar"] = "44E",
        ["pa"] = "446", ["pan"] = "446", ["si"] = "45B", ["sin"] = "45B",
        ["ne"] = "461", ["nep"] = "461",         ["sd"] = "859", ["snd"] = "859",
        ["ur"] = "420", ["urd"] = "420", ["ur-pk"] = "420",
        // Middle East / RTL
        ["ar"] = "1401", ["ara"] = "1401", ["ar-sa"] = "1401", ["ar-eg"] = "C01", ["ar-ae"] = "3801", ["ar-jo"] = "2C01",
        ["he"] = "40D", ["heb"] = "40D", ["he-il"] = "40D",
        ["fa"] = "429", ["fas"] = "429", ["per"] = "429", ["fa-ir"] = "429", ["fa-en"] = "429",
        ["ps"] = "463", ["pus"] = "463", ["dv"] = "465", ["div"] = "465",
        ["ckb"] = "492", ["ku-arab"] = "492", ["ug"] = "480", ["uig"] = "480",
        // Turkic / Central Asian
        ["tr"] = "41F", ["tur"] = "41F", ["az"] = "82C", ["aze"] = "82C",
        ["kk"] = "43F", ["kaz"] = "43F", ["ky"] = "440", ["kir"] = "440",
        ["uz"] = "843", ["uzb"] = "843", ["mn"] = "450", ["mon"] = "450",
        // East/Southeast Asian
        ["ja"] = "411", ["jpn"] = "411", ["ko"] = "412", ["kor"] = "412",
        ["vi"] = "42A", ["vie"] = "42A", ["th"] = "41E", ["tha"] = "41E",
        ["km"] = "453", ["khm"] = "453", ["lo"] = "454", ["lao"] = "454", ["my"] = "455", ["mya"] = "455",
        ["id"] = "421", ["ind"] = "421", ["ms"] = "43E", ["msa"] = "43E", ["may"] = "43E",
        ["tl"] = "464", ["tgl"] = "464", ["kmr"] = "43F",
        // African / other
        ["sw"] = "441", ["swa"] = "441", ["swh"] = "441",
        ["am"] = "45E", ["amh"] = "45E", ["ti"] = "473", ["tir"] = "473",
        ["so"] = "478", ["som"] = "478", ["ha"] = "468", ["hau"] = "468",
        ["yo"] = "46A", ["yor"] = "46A", ["zu"] = "430", ["zul"] = "430",
        ["cy"] = "452", ["cym"] = "452", ["wel"] = "452", ["ga"] = "83C", ["gle"] = "83C",
        ["gd"] = "43C", ["gla"] = "43C", ["br"] = "7E3", ["bre"] = "7E3",
        ["hy"] = "42B", ["hye"] = "42B", ["arm"] = "42B", ["hyw"] = "42B",
        ["ka"] = "437", ["kat"] = "437",
        ["lt"] = "427", ["lit"] = "427", ["lv"] = "426", ["lav"] = "426",
        ["et"] = "425", ["est"] = "425", ["eo"] = "409", ["epo"] = "409",
    };

    /// <summary>Primary subtags of right-to-left writing systems (aliases go under Arabic).</summary>
    private static readonly HashSet<string> RtlSubtags = new(StringComparer.OrdinalIgnoreCase)
    {
        "ar", "ara", "he", "heb", "fa", "fas", "per", "ur", "urd",
        "ps", "pus", "sd", "snd", "ckb", "dv", "div", "ug", "uig", "yi",
    };

    /// <summary>English language names → code, so catalog display names ("Urdu") also resolve.</summary>
    private static readonly Dictionary<string, string> NamesToCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["english"] = "en", ["chinese"] = "zh", ["cantonese"] = "yue", ["mandarin"] = "zh",
        ["german"] = "de", ["dutch"] = "nl", ["swedish"] = "sv", ["danish"] = "da", ["norwegian"] = "nb",
        ["finnish"] = "fi", ["icelandic"] = "is", ["afrikaans"] = "af", ["yiddish"] = "yi",
        ["spanish"] = "es", ["french"] = "fr", ["italian"] = "it", ["portuguese"] = "pt",
        ["romanian"] = "ro", ["catalan"] = "ca", ["galician"] = "gl", ["basque"] = "eu",
        ["russian"] = "ru", ["ukrainian"] = "uk", ["polish"] = "pl", ["czech"] = "cs",
        ["slovak"] = "sk", ["slovenian"] = "sl", ["bulgarian"] = "bg", ["croatian"] = "hr",
        ["serbian"] = "sr", ["bosnian"] = "bs", ["macedonian"] = "mk", ["albanian"] = "sq",
        ["greek"] = "el", ["hungarian"] = "hu",
        ["hindi"] = "hi", ["bengali"] = "bn", ["tamil"] = "ta", ["telugu"] = "te", ["malayalam"] = "ml",
        ["kannada"] = "kn", ["gujarati"] = "gu", ["marathi"] = "mr", ["punjabi"] = "pa",
        ["sinhala"] = "si", ["nepali"] = "ne", ["sindhi"] = "sd",
        ["urdu"] = "ur", ["arabic"] = "ar", ["hebrew"] = "he", ["persian"] = "fa", ["farsi"] = "fa",
        ["pashto"] = "ps", ["divehi"] = "dv", ["kurdish"] = "ckb", ["uyghur"] = "ug",
        ["turkish"] = "tr", ["azerbaijani"] = "az", ["kazakh"] = "kk", ["kyrgyz"] = "ky",
        ["uzbek"] = "uz", ["mongolian"] = "mn",
        ["japanese"] = "ja", ["korean"] = "ko", ["vietnamese"] = "vi", ["thai"] = "th",
        ["khmer"] = "km", ["lao"] = "lo", ["burmese"] = "my", ["indonesian"] = "id",
        ["malay"] = "ms", ["tagalog"] = "tl", ["filipino"] = "tl",
        ["swahili"] = "sw", ["amharic"] = "am", ["tigrinya"] = "ti", ["somali"] = "so",
        ["hausa"] = "ha", ["yoruba"] = "yo", ["zulu"] = "zu", ["welsh"] = "cy", ["irish"] = "ga",
        ["scottish gaelic"] = "gd", ["breton"] = "br", ["armenian"] = "hy", ["georgian"] = "ka",
        ["lithuanian"] = "lt", ["latvian"] = "lv", ["estonian"] = "et", ["esperanto"] = "eo",
    };

    /// <summary>
    /// Resolve a language tag (BCP-47 locale like "ur-PK", piper style
    /// "nl_BE", an ISO 639-3 code like "urd", or an English name like
    /// "Urdu") to a SAPI locale + hex LANGID. Returns false when unknown —
    /// callers then use en-US.
    /// </summary>
    public static bool TryResolve(string? language, out string localeName, out string langIdHex)
    {
        localeName = EnUsLocale;
        langIdHex = EnUsLangId;

        var raw = (language ?? "").Trim().Replace('_', '-');
        if (raw.Length == 0) return false;
        var tag = raw.ToLowerInvariant();

        // Direct hit on the whole tag (handles "en-us", "nl-be", "ur-pk", "urd", …)
        if (LocaleToLangId.TryGetValue(tag, out var direct))
        {
            localeName = LocaleDisplayName(tag);
            langIdHex = direct;
            return true;
        }

        // Try the primary subtag ("ur-PK" -> "ur", "fa_IR" -> "fa")
        var primary = tag.Split('-')[0];
        if (LocaleToLangId.TryGetValue(primary, out var byPrimary))
        {
            localeName = LocaleDisplayName(primary);
            langIdHex = byPrimary;
            return true;
        }

        // English language name ("urdu", "Persian")
        if (NamesToCode.TryGetValue(tag, out var code) && LocaleToLangId.TryGetValue(code, out var byName))
        {
            localeName = LocaleDisplayName(code);
            langIdHex = byName;
            return true;
        }

        return false;
    }

    /// <summary>True when the language writes right-to-left (Arabic, Hebrew, Persian, Urdu, …).</summary>
    public static bool IsRightToLeft(string? language)
    {
        var tag = Normalize(language);
        if (tag.Length == 0) return false;
        var primary = tag.Split('-')[0];
        if (RtlSubtags.Contains(primary) || RtlSubtags.Contains(tag)) return true;
        return NamesToCode.TryGetValue(tag, out var code) && RtlSubtags.Contains(code);
    }

    /// <summary>True when the language resolves to en-US (the alias would be redundant).</summary>
    public static bool IsEnglish(string? language)
    {
        var tag = Normalize(language);
        if (tag.Length == 0) return true; // unknown languages keep the en-US fallback
        var primary = tag.Split('-')[0];
        if (primary is "en" or "eng") return true;
        return NamesToCode.TryGetValue(tag, out var code) && code == "en";
    }

    private static string Normalize(string? language)
        => (language ?? "").Trim().Replace('_', '-').ToLowerInvariant();

    private static string LocaleDisplayName(string tag)
    {
        // Canonical display locale for the tag: region variants keep their
        // own name, plain codes get their default region appended.
        return tag switch
        {
            "en" or "eng" => "en-US",
            "zh" or "zho" or "cmn" => "zh-CN",
            "yue" => "zh-HK",
            "de" or "deu" or "ger" => "de-DE",
            "nl" or "nld" => "nl-NL",
            "sv" or "swe" => "sv-SE",
            "da" or "dan" => "da-DK",
            "nb" or "no" or "nob" or "nor" => "nb-NO",
            "fi" or "fin" => "fi-FI",
            "is" or "isl" => "is-IS",
            "af" or "afr" => "af-ZA",
            "yi" => "yi-001",
            "es" or "spa" => "es-ES",
            "fr" or "fra" or "fre" => "fr-FR",
            "it" or "ita" => "it-IT",
            "pt" or "por" => "pt-BR",
            "ro" or "ron" or "rum" => "ro-RO",
            "ca" or "cat" => "ca-ES",
            "gl" or "glg" => "gl-ES",
            "eu" or "eus" or "baq" => "eu-ES",
            "ru" or "rus" => "ru-RU",
            "uk" or "ukr" => "uk-UA",
            "pl" or "pol" => "pl-PL",
            "cs" or "ces" or "cze" => "cs-CZ",
            "sk" or "slk" => "sk-SK",
            "sl" or "slv" => "sl-SI",
            "bg" or "bul" => "bg-BG",
            "hr" or "hrv" => "hr-HR",
            "sr" or "srp" => "sr-Cyrl-RS",
            "bs" or "bos" => "bs-Latn-BA",
            "mk" or "mkd" => "mk-MK",
            "sq" or "sqi" or "alb" => "sq-AL",
            "el" or "ell" or "gre" => "el-GR",
            "hu" or "hun" => "hu-HU",
            "hi" or "hin" => "hi-IN",
            "bn" or "ben" => "bn-IN",
            "ta" or "tam" => "ta-IN",
            "te" or "tel" => "te-IN",
            "ml" or "mal" => "ml-IN",
            "kn" or "kan" => "kn-IN",
            "gu" or "guj" => "gu-IN",
            "mr" or "mar" => "mr-IN",
            "pa" or "pan" => "pa-IN",
            "si" or "sin" => "si-LK",
            "ne" or "nep" => "ne-NP",
            "sd" or "snd" => "sd-PK",
            "ur" or "urd" => "ur-PK",
            "ar" or "ara" => "ar-SA",
            "he" or "heb" => "he-IL",
            "fa" or "fas" or "per" => "fa-IR",
            "ps" or "pus" => "ps-AF",
            "dv" or "div" => "dv-MV",
            "ckb" => "ckb-IQ",
            "ug" or "uig" => "ug-CN",
            "tr" or "tur" => "tr-TR",
            "az" or "aze" => "az-Latn-AZ",
            "kk" or "kaz" => "kk-KZ",
            "ky" or "kir" => "ky-KG",
            "uz" or "uzb" => "uz-Latn-UZ",
            "mn" or "mon" => "mn-MN",
            "ja" or "jpn" => "ja-JP",
            "ko" or "kor" => "ko-KR",
            "vi" or "vie" => "vi-VN",
            "th" or "tha" => "th-TH",
            "km" or "khm" => "km-KH",
            "lo" or "lao" => "lo-LA",
            "my" or "mya" => "my-MM",
            "id" or "ind" => "id-ID",
            "ms" or "msa" or "may" => "ms-MY",
            "tl" or "tgl" => "fil-PH",
            "sw" or "swa" or "swh" => "sw-KE",
            "am" or "amh" => "am-ET",
            "ti" or "tir" => "ti-ER",
            "so" or "som" => "so-SO",
            "ha" or "hau" => "ha-Latn-NG",
            "yo" or "yor" => "yo-NG",
            "zu" or "zul" => "zu-ZA",
            "cy" or "cym" or "wel" => "cy-GB",
            "ga" or "gle" => "ga-IE",
            "gd" or "gla" => "gd-GB",
            "br" or "bre" => "br-FR",
            "hy" or "hye" or "arm" => "hy-AM",
            "ka" or "kat" => "ka-GE",
            "lt" or "lit" => "lt-LT",
            "lv" or "lav" => "lv-LV",
            "et" or "est" => "et-EE",
            "eo" or "epo" => "en-US", // Esperanto has no Windows locale; alias to en-US
            _ when tag.Contains('-') => TitleCaseLocale(tag), // regional tag ("nl-BE") — keep, properly cased
            _ => tag,
        };
    }

    /// <summary>"ur-pk" -> "ur-PK": lowercase language part, uppercase region part.</summary>
    private static string TitleCaseLocale(string tag)
    {
        var parts = tag.Split('-');
        for (var i = 1; i < parts.Length; i++)
        {
            if (parts[i].Length == 2 || parts[i].Length == 4)
                parts[i] = parts[i].ToUpperInvariant();
            else if (parts[i].Length == 3) // script subtags like "latn"
                parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i][1..];
        }
        return string.Join("-", parts);
    }
}
